using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CuePilot;

internal readonly record struct FishingMeterObservation(
    bool IsVisible,
    double TensionRatio,
    double ProgressRatio,
    bool IsCaught,
    double Confidence,
    bool IsFailed = false)
{
    internal static FishingMeterObservation Missing => new(false, 0, 0, false, 0);
}

internal readonly record struct FishingMeterEvidence(
    double DarkDisk,
    double DiskContrast,
    double RingStrength,
    double RingRadiusRatio,
    double ProgressStrength,
    double CaughtStrength,
    double FailureStrength,
    double LmbPromptStrength,
    double CenterOffsetX,
    double CenterOffsetY,
    double CandidateConfidence,
    bool PassedVisibility,
    string DecisionReason);

internal readonly record struct FishingMeterCandidateEvidence(
    int RegionIndex,
    Rectangle Region,
    FishingMeterEvidence Evidence,
    bool IsTracked = false,
    double Scale = 1);

internal sealed record FishingMeterFrameAnalysis(
    FishingMeterObservation Observation,
    FishingMeterCandidateEvidence? PrimaryCandidate,
    int CandidateCount)
{
    internal bool UsedTrackedRegion => PrimaryCandidate?.IsTracked == true;
}

internal static class FishingMeterReacquisition
{
    // FiveM briefly fades/recomposes the circular UI around a pulse. A meter is
    // only considered gone after this much continuously unavailable time;
    // capture failures are handled separately by the engine. Time keeps this
    // grace period stable when bright-scene analysis takes longer than usual.
    internal static readonly TimeSpan MissingDurationBeforeComplete = TimeSpan.FromSeconds(2);

    internal static bool HasEnded(TimeSpan missingDuration, double highestProgress) =>
        missingDuration >= MissingDurationBeforeComplete && highestProgress < 0.55;

    internal static bool ShouldCollectAfterMeterLoss(TimeSpan missingDuration, double highestProgress) =>
        missingDuration >= MissingDurationBeforeComplete && highestProgress >= 0.55;
}

internal sealed class FishingMeterStabilityGate(int requiredMatches = 2, int toleratedMisses = 1)
{
    private int matches;
    private int misses;

    internal bool Observe(FishingMeterObservation observation)
    {
        if (observation.IsVisible && !observation.IsCaught && !observation.IsFailed)
        {
            matches++;
            misses = 0;
            return matches >= requiredMatches;
        }

        if (!observation.IsVisible && matches > 0 && misses < toleratedMisses)
        {
            misses++;
            return false;
        }

        matches = 0;
        misses = 0;
        return false;
    }
}

internal sealed class FishingFailureGate(int requiredMatches = 2)
{
    private int matches;

    internal bool Observe(FishingMeterObservation observation)
    {
        matches = observation.IsFailed ? matches + 1 : 0;
        return matches >= requiredMatches;
    }
}

internal static class FishingMeterDetector
{
    private const int AngleSamples = 120;
    private const int DarknessAngleSamples = 48;
    private static readonly (double X, double Y)[] MeterDirections = CreateDirections(AngleSamples);
    private static readonly (double X, double Y)[] DarknessDirections = CreateDirections(DarknessAngleSamples);
    private static readonly Lazy<LmbPromptTemplate> LmbTemplate = new(LoadLmbPromptTemplate);

    internal static FishingMeterObservation Analyze(Bitmap bitmap) => Analyze(bitmap, out _);

    internal static FishingMeterObservation Analyze(Bitmap bitmap, out FishingMeterEvidence evidence) =>
        Analyze(bitmap, out evidence, requireActiveIdentity: false);

    internal static FishingMeterObservation Analyze(
        Bitmap bitmap,
        out FishingMeterEvidence evidence,
        bool requireActiveIdentity)
    {
        evidence = default;
        if (bitmap.Width < 80 || bitmap.Height < 80)
        {
            return FishingMeterObservation.Missing;
        }

        using var pixels = new BitmapPixels(bitmap);
        var shortest = Math.Min(pixels.Width, pixels.Height);
        var approximateRadius = shortest * 0.20;
        var expectedCenterX = pixels.Width >= pixels.Height * 1.15 ? pixels.Width * 0.40 : pixels.Width / 2d;
        var expectedCenterY = pixels.Height / 2d;
        var searchStep = Math.Max(2, shortest / 80);
        var calibratedCenter = FindDarkestCenter(
            pixels,
            expectedCenterX,
            expectedCenterY,
            approximateRadius,
            searchDistance: 0,
            searchStep);
        var observation = AnalyzeCenter(
            pixels,
            calibratedCenter.X,
            calibratedCenter.Y,
            expectedCenterX,
            expectedCenterY,
            approximateRadius,
            requireActiveIdentity,
            out evidence);
        if (observation.IsVisible)
        {
            return observation;
        }

        // A calibrated center is the strongest location hint. Only fall back to
        // the wider dark-disk search when it contains no meter evidence. This
        // prevents reflective water from pulling an otherwise valid daylight
        // meter away from its known UI position while retaining tolerance for a
        // slightly shifted HUD layout.
        var searchDistance = Math.Max(4, (int)Math.Round(shortest * 0.08));
        var fallbackCenter = FindDarkestCenter(
            pixels,
            expectedCenterX,
            expectedCenterY,
            approximateRadius,
            searchDistance,
            searchStep);
        if (Math.Abs(fallbackCenter.X - calibratedCenter.X) < 0.5
            && Math.Abs(fallbackCenter.Y - calibratedCenter.Y) < 0.5)
        {
            return FishingMeterObservation.Missing;
        }

        var fallbackObservation = AnalyzeCenter(
            pixels,
            fallbackCenter.X,
            fallbackCenter.Y,
            expectedCenterX,
            expectedCenterY,
            approximateRadius,
            requireActiveIdentity,
            out var fallbackEvidence);
        if (fallbackObservation.IsVisible)
        {
            evidence = fallbackEvidence;
            return fallbackObservation;
        }

        if (fallbackEvidence.CandidateConfidence > evidence.CandidateConfidence)
        {
            evidence = fallbackEvidence;
        }

        return FishingMeterObservation.Missing;
    }

    private static FishingMeterObservation AnalyzeCenter(
        BitmapPixels pixels,
        double centerX,
        double centerY,
        double expectedCenterX,
        double expectedCenterY,
        double approximateRadius,
        bool requireActiveIdentity,
        out FishingMeterEvidence evidence)
    {
        var darkness = DarkDiskScore(pixels, centerX, centerY, approximateRadius);
        var diskContrast = DiskContrastScore(pixels, centerX, centerY, approximateRadius);
        var ringRadius = FindTensionRing(pixels, centerX, centerY, approximateRadius, out var ringStrength);
        var progress = MeasureProgress(pixels, centerX, centerY, approximateRadius);
        var caughtStrength = MeasureCaughtMark(pixels, centerX, centerY, approximateRadius);
        var failureStrength = MeasureFailureMark(pixels, centerX, centerY, approximateRadius);
        // The warm tension ring alone is not unique: the reel, HUD controls,
        // and character clothing can create similar dark/warm circles during
        // the cast. The initial minigame's ring is still close to the center;
        // a large apparent ring without progress is a false HUD match in the
        // supplied cast scene. Once progress/catch/failure appears, tension can
        // legitimately expand across the full circle.
        var initialRing = ringRadius / approximateRadius <= 0.72;
        // Once the completion arc has filled, FiveM can replace the warm
        // tension ring with lime. Treat that substantial circular progress
        // signal as its own meter proof; otherwise a valid bright-day caught
        // frame falls into the visual-loss/reacquisition path.
        var completedArc = progress >= 0.18;
        var activeMeter = ringStrength >= 0.16
            && (progress >= 0.03
                || darkness >= 0.18 && (caughtStrength >= 0.015 || failureStrength >= 0.02));
        // Before progress appears, demand a substantially coherent ring as well
        // as the nearby prompt. Daylight character/reel edges can satisfy the
        // old weak-ring rule, but the recorded initial meter remains well above
        // this conservative floor throughout its pulse animation.
        var diskEvidence = darkness >= 0.56 || diskContrast >= 0.12;
        var startupCandidate = initialRing
            && ringStrength >= 0.30
            && diskEvidence
            && progress < 0.03
            && caughtStrength < 0.015
            && failureStrength < 0.02;
        // Identity is measured on every plausible meter shape, not only the
        // zero-progress startup frame. A live session can enter this detector
        // after the progress arc has already begun, and acquisition still must
        // prove the adjacent LMB keycap before any mouse input is allowed.
        var plausibleMeterShape = startupCandidate
            || requireActiveIdentity && (activeMeter || completedArc);
        var lmbPromptStrength = plausibleMeterShape
            ? MeasureLmbPrompt(pixels, centerX, centerY, approximateRadius)
            : 0;
        // A verified Increase Tension / LMB prompt proves this is the active
        // minigame, even when orange scenery contaminates the red-mark probe.
        // This exception is acquisition-only; after lock, the failure mark
        // remains authoritative because the prompt disappears on failure.
        var failed = failureStrength >= 0.02
            && !(requireActiveIdentity && lmbPromptStrength >= 0.90);
        var currentLmbProof = lmbPromptStrength >= 0.97;
        var legacyLmbProof = HasLegacyLmbIdentity(lmbPromptStrength, darkness, diskContrast);
        var initialMeter = initialRing && ringStrength >= 0.35 && (currentLmbProof || legacyLmbProof);
        var visible = diskEvidence && (activeMeter || initialMeter || completedArc);
        var decisionReason = visible
            ? failed ? "Failure meter passed all visibility gates"
                : completedArc ? "Completed progress arc passed visibility"
                : activeMeter ? "Active meter passed ring and progress gates"
                : "Startup meter passed disk, ring, and LMB gates"
            : !diskEvidence ? $"Disk evidence failed: dark {darkness:P0} / contrast {diskContrast:P0}"
            : !initialRing && progress < 0.03 ? $"Startup ring radius {ringRadius / approximateRadius:P0} exceeds 72%"
            : ringStrength < 0.30 ? $"Ring strength {ringStrength:P0} is below the 30% startup gate"
            : ringStrength < 0.35 ? $"Ring strength {ringStrength:P0} is below the 35% acceptance gate"
            : !currentLmbProof && !legacyLmbProof ? $"LMB signature {lmbPromptStrength:P0} did not satisfy the 97% current or legacy gate"
            : "Candidate did not satisfy an active, startup, completed, or failure meter path";
        var confidence = Math.Clamp(
            (darkness - 0.45) * 1.5 + diskContrast * 0.8 + ringStrength * 1.5
                + progress * 0.35 + lmbPromptStrength * 0.22,
            0,
            1);
        evidence = new FishingMeterEvidence(
            darkness,
            diskContrast,
            ringStrength,
            ringRadius / approximateRadius,
            progress,
            caughtStrength,
            failureStrength,
            lmbPromptStrength,
            (centerX - expectedCenterX) / approximateRadius,
            (centerY - expectedCenterY) / approximateRadius,
            confidence,
            visible,
            decisionReason);
        if (!visible)
        {
            return FishingMeterObservation.Missing;
        }

        return new FishingMeterObservation(
            true,
            Math.Clamp(ringRadius / approximateRadius, 0, 1.2),
            Math.Clamp(progress, 0, 1),
            caughtStrength >= 0.035 && progress >= 0.35,
            confidence,
            failed);
    }

    internal static bool HasCurrentLmbIdentity(FishingMeterEvidence evidence) =>
        evidence.LmbPromptStrength >= 0.90;

    private static bool HasLegacyLmbIdentity(double strength, double darkness, double contrast) =>
        strength >= 0.45 && darkness >= 0.65 && contrast >= 0.18;

    private static (double X, double Y) FindDarkestCenter(
        BitmapPixels pixels,
        double expectedCenterX,
        double expectedCenterY,
        double approximateRadius,
        int searchDistance,
        int searchStep)
    {
        var centerX = expectedCenterX;
        var centerY = expectedCenterY;
        var bestDarkness = 0d;
        for (var offsetY = -searchDistance; offsetY <= searchDistance; offsetY += searchStep)
        {
            for (var offsetX = -searchDistance; offsetX <= searchDistance; offsetX += searchStep)
            {
                var candidate = DarkDiskScore(pixels, expectedCenterX + offsetX, expectedCenterY + offsetY, approximateRadius);
                if (candidate <= bestDarkness)
                {
                    continue;
                }

                bestDarkness = candidate;
                centerX = expectedCenterX + offsetX;
                centerY = expectedCenterY + offsetY;
            }
        }

        // Refine around the best coarse location without allowing the search to
        // drift farther into the animated background.
        var refinedX = centerX;
        var refinedY = centerY;
        for (var offsetY = -searchStep; offsetY <= searchStep; offsetY++)
        {
            for (var offsetX = -searchStep; offsetX <= searchStep; offsetX++)
            {
                var candidate = DarkDiskScore(pixels, centerX + offsetX, centerY + offsetY, approximateRadius);
                if (candidate <= bestDarkness)
                {
                    continue;
                }

                bestDarkness = candidate;
                refinedX = centerX + offsetX;
                refinedY = centerY + offsetY;
            }
        }

        return (refinedX, refinedY);
    }

    private static double DarkDiskScore(BitmapPixels bitmap, double centerX, double centerY, double radius)
    {
        var dark = 0;
        var count = 0;
        for (var radialStep = 3; radialStep <= 9; radialStep++)
        {
            var sampleRadius = radius * radialStep / 10d;
            for (var angleIndex = 0; angleIndex < DarknessAngleSamples; angleIndex++)
            {
                var color = Sample(bitmap, centerX, centerY, sampleRadius, angleIndex, DarknessAngleSamples);
                if (Luminance(color) < 62)
                {
                    dark++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : dark / (double)count;
    }

    private static double DiskContrastScore(BitmapPixels bitmap, double centerX, double centerY, double radius)
    {
        var innerLuminance = 0d;
        var innerCount = 0;
        var outerLuminance = 0d;
        var outerCount = 0;
        for (var angleIndex = 0; angleIndex < DarknessAngleSamples; angleIndex++)
        {
            for (var radialStep = 3; radialStep <= 8; radialStep++)
            {
                innerLuminance += Luminance(Sample(
                    bitmap, centerX, centerY, radius * radialStep / 10d, angleIndex, DarknessAngleSamples));
                innerCount++;
            }

            for (var radialStep = 11; radialStep <= 14; radialStep++)
            {
                outerLuminance += Luminance(Sample(
                    bitmap, centerX, centerY, radius * radialStep / 10d, angleIndex, DarknessAngleSamples));
                outerCount++;
            }
        }

        if (innerCount == 0 || outerCount == 0)
        {
            return 0;
        }

        var innerAverage = innerLuminance / innerCount;
        var outerAverage = outerLuminance / outerCount;
        return Math.Clamp((outerAverage - innerAverage) / Math.Max(48, outerAverage), 0, 1);
    }

    private static double FindTensionRing(BitmapPixels bitmap, double centerX, double centerY, double meterRadius, out double strength)
    {
        // The initial pulse state has only a small warm inner ring. Start the
        // radial search inside the old 35% floor so a just-appeared meter is
        // not discarded before its outer progress arc has rendered.
        var start = Math.Max(6, (int)Math.Round(meterRadius * 0.18));
        var end = Math.Max(start + 1, (int)Math.Round(meterRadius * 0.94));
        var bestRadius = start;
        var bestScore = 0d;

        for (var radius = start; radius <= end; radius++)
        {
            var warmCoverage = 0d;
            var contrast = 0d;
            for (var angleIndex = 0; angleIndex < AngleSamples; angleIndex++)
            {
                var color = Sample(bitmap, centerX, centerY, radius, angleIndex, AngleSamples);
                if (IsWarmRing(color))
                {
                    warmCoverage += 1;
                }
                else if (color.R > 72 && color.R > color.B + 5)
                {
                    warmCoverage += 0.35;
                }

                var inside = Sample(bitmap, centerX, centerY, Math.Max(1, radius - 2), angleIndex, AngleSamples);
                var outside = Sample(bitmap, centerX, centerY, radius + 2, angleIndex, AngleSamples);
                contrast += Math.Max(0, Luminance(color) - (Luminance(inside) + Luminance(outside)) / 2);
            }

            warmCoverage /= AngleSamples;
            contrast /= AngleSamples;
            // The center glow is warm but changes slowly across radii. The
            // controlled boundary is a thin circular edge with high radial
            // contrast, so contrast must dominate the selection.
            var score = warmCoverage + contrast / 16d;
            if (score > bestScore)
            {
                bestScore = score;
                bestRadius = radius;
            }
        }

        strength = Math.Clamp(bestScore / 2.5, 0, 1);
        return bestRadius;
    }

    private static double MeasureProgress(BitmapPixels bitmap, double centerX, double centerY, double meterRadius)
    {
        var greenAngles = 0;
        for (var angleIndex = 0; angleIndex < AngleSamples; angleIndex++)
        {
            var green = false;
            for (var radialStep = 105; radialStep <= 132; radialStep += 3)
            {
                var color = Sample(bitmap, centerX, centerY, meterRadius * radialStep / 100d, angleIndex, AngleSamples);
                if (IsLime(color))
                {
                    green = true;
                    break;
                }
            }

            if (green)
            {
                greenAngles++;
            }
        }

        return greenAngles / (double)AngleSamples;
    }

    private static double MeasureCaughtMark(BitmapPixels bitmap, double centerX, double centerY, double meterRadius)
    {
        var green = 0;
        var count = 0;
        var limit = (int)Math.Round(meterRadius * 0.62);
        var step = Math.Max(1, limit / 12);
        for (var y = -limit; y <= limit; y += step)
        {
            for (var x = -limit; x <= limit; x += step)
            {
                if (x * x + y * y > limit * limit)
                {
                    continue;
                }

                var pixelX = Math.Clamp((int)Math.Round(centerX + x), 0, bitmap.Width - 1);
                var pixelY = Math.Clamp((int)Math.Round(centerY + y), 0, bitmap.Height - 1);
                if (IsLime(bitmap.GetPixel(pixelX, pixelY)))
                {
                    green++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : green / (double)count;
    }

    private static double MeasureFailureMark(BitmapPixels bitmap, double centerX, double centerY, double meterRadius)
    {
        // The failure state is a large saturated red X in the middle of the same
        // dark disk. Keep this separate from the thin warm tension ring so the
        // controller can recast instead of pulsing on a finished minigame.
        var red = 0;
        var count = 0;
        var limit = (int)Math.Round(meterRadius * 0.62);
        var step = Math.Max(1, limit / 14);
        for (var y = -limit; y <= limit; y += step)
        {
            for (var x = -limit; x <= limit; x += step)
            {
                if (x * x + y * y > limit * limit)
                {
                    continue;
                }

                var pixelX = Math.Clamp((int)Math.Round(centerX + x), 0, bitmap.Width - 1);
                var pixelY = Math.Clamp((int)Math.Round(centerY + y), 0, bitmap.Height - 1);
                var color = bitmap.GetPixel(pixelX, pixelY);
                if (color.R >= 180 && color.G <= 155 && color.B <= 175
                    && color.R >= color.G + 55 && color.R >= color.B + 35)
                {
                    red++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : red / (double)count;
    }

    private static double MeasureLmbPrompt(
        BitmapPixels bitmap,
        double centerX,
        double centerY,
        double meterRadius)
    {
        // The startup state has no progress arc yet, so prove it with the real
        // outlined LMB keycap rather than a generic count of bright pixels.
        var template = LmbTemplate.Value;
        var baseScale = Math.Min(bitmap.Width, bitmap.Height) / 259d;
        var best = 0d;
        foreach (var scaleFactor in new[] { 0.65, 0.80, 0.90, 1d, 1.10, 1.20 })
        {
            var scale = baseScale * scaleFactor;
            var width = Math.Max(24, (int)Math.Round(template.Width * scale));
            var height = Math.Max(15, (int)Math.Round(template.Height * scale));
            var minimumX = Math.Max(0, (int)Math.Round(centerX + meterRadius * 1.12));
            var maximumX = Math.Min(bitmap.Width - width, (int)Math.Round(centerX + meterRadius * 2.08));
            var minimumY = Math.Max(0, (int)Math.Round(centerY - meterRadius * 1.30));
            var maximumY = Math.Min(bitmap.Height - height, (int)Math.Round(centerY - meterRadius * 0.24));
            if (maximumX < minimumX || maximumY < minimumY) continue;
            for (var y = minimumY; y <= maximumY; y++)
            {
                for (var x = minimumX; x <= maximumX; x++)
                {
                    var foregroundMatches = 0;
                    foreach (var point in template.Foreground)
                    {
                        var color = bitmap.GetPixel(
                            x + (int)Math.Round(point.X * scale),
                            y + (int)Math.Round(point.Y * scale));
                        if (IsNeutral(color, 115, 70)) foregroundMatches++;
                    }

                    var glyphBackgroundMatches = 0;
                    foreach (var point in template.GlyphBackground)
                    {
                        var color = bitmap.GetPixel(
                            x + (int)Math.Round(point.X * scale),
                            y + (int)Math.Round(point.Y * scale));
                        if (Luminance(color) <= 115) glyphBackgroundMatches++;
                    }

                    var outerBackgroundMatches = 0;
                    foreach (var point in template.OuterBackground)
                    {
                        var color = bitmap.GetPixel(
                            x + (int)Math.Round(point.X * scale),
                            y + (int)Math.Round(point.Y * scale));
                        if (Luminance(color) <= 115) outerBackgroundMatches++;
                    }

                    var contrastMatches = 0;
                    var contrastSamples = 0;
                    foreach (var pair in template.ContrastPairs)
                    {
                        var foregroundX = x + (int)Math.Round(pair.Foreground.X * scale);
                        var foregroundY = y + (int)Math.Round(pair.Foreground.Y * scale);
                        var backgroundX = x + (int)Math.Round(pair.Background.X * scale);
                        var backgroundY = y + (int)Math.Round(pair.Background.Y * scale);
                        if (foregroundX == backgroundX && foregroundY == backgroundY) continue;
                        contrastSamples++;
                        var foreground = Luminance(bitmap.GetPixel(foregroundX, foregroundY));
                        var background = Luminance(bitmap.GetPixel(backgroundX, backgroundY));
                        if (foreground - background >= 35) contrastMatches++;
                    }

                    var foregroundScore = foregroundMatches / (double)template.Foreground.Length;
                    var glyphBackgroundScore = glyphBackgroundMatches / (double)template.GlyphBackground.Length;
                    var outerBackgroundScore = outerBackgroundMatches / (double)template.OuterBackground.Length;
                    var contrastScore = contrastSamples == 0 ? 0 : contrastMatches / (double)contrastSamples;
                    best = Math.Max(best,
                        foregroundScore * 0.35
                            + glyphBackgroundScore * 0.20
                            + outerBackgroundScore * 0.05
                            + contrastScore * 0.40);
                }
            }
        }

        return best;
    }

    private static LmbPromptTemplate LoadLmbPromptTemplate()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("CuePilot.Vision.TensionReady.png")
            ?? throw new InvalidOperationException("Missing fishing LMB prompt reference.");
        using var reference = new Bitmap(stream);
        var foreground = new List<Point>();
        var glyphBackground = new List<Point>();
        var outerBackground = new List<Point>();
        for (var y = 1; y < reference.Height - 1; y += 2)
        {
            for (var x = 1; x < reference.Width - 1; x += 2)
            {
                var color = reference.GetPixel(x, y);
                var insideGlyph = x is >= 7 and <= 45 && y is >= 6 and <= 26;
                if (insideGlyph && IsNeutral(color, 145, 70)) foreground.Add(new Point(x, y));
                else if (insideGlyph && Luminance(color) <= 72) glyphBackground.Add(new Point(x, y));
                else if (Luminance(color) <= 72) outerBackground.Add(new Point(x, y));
            }
        }

        if (foreground.Count < 12 || glyphBackground.Count < 30 || outerBackground.Count < 30)
            throw new InvalidOperationException("Fishing LMB prompt reference is incomplete.");
        var sampledForeground = SampleEvenly(foreground, 48);
        return new LmbPromptTemplate(
            reference.Width,
            reference.Height,
            sampledForeground,
            SampleEvenly(glyphBackground, 48),
            SampleEvenly(outerBackground, 32),
            CreateLmbContrastPairs(reference, sampledForeground));
    }

    private static LmbContrastPair[] CreateLmbContrastPairs(
        Bitmap reference,
        IReadOnlyList<Point> foreground)
    {
        var pairs = new List<LmbContrastPair>();
        foreach (var point in foreground)
        {
            var foregroundLuminance = Luminance(reference.GetPixel(point.X, point.Y));
            var bestDifference = 0d;
            var bestBackground = Point.Empty;
            for (var offsetY = -4; offsetY <= 4; offsetY++)
            {
                for (var offsetX = -4; offsetX <= 4; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0) continue;
                    var x = point.X + offsetX;
                    var y = point.Y + offsetY;
                    if (x < 0 || x >= reference.Width || y < 0 || y >= reference.Height) continue;
                    var difference = foregroundLuminance - Luminance(reference.GetPixel(x, y));
                    if (difference <= bestDifference) continue;
                    bestDifference = difference;
                    bestBackground = new Point(x, y);
                }
            }

            if (bestDifference >= 50)
            {
                pairs.Add(new LmbContrastPair(point, bestBackground));
            }
        }

        if (pairs.Count < 20)
            throw new InvalidOperationException("Fishing LMB prompt reference has insufficient local contrast.");
        return pairs.ToArray();
    }

    private static Point[] SampleEvenly(IReadOnlyList<Point> points, int maximum)
    {
        if (points.Count <= maximum) return points.ToArray();
        return Enumerable.Range(0, maximum)
            .Select(index => points[index * (points.Count - 1) / (maximum - 1)])
            .Distinct()
            .ToArray();
    }

    private static bool IsNeutral(Color color, int minimumBrightness, int maximumSpread)
    {
        var brightest = Math.Max(color.R, Math.Max(color.G, color.B));
        var darkest = Math.Min(color.R, Math.Min(color.G, color.B));
        return (color.R + color.G + color.B) / 3 >= minimumBrightness && brightest - darkest <= maximumSpread;
    }

    private static Color Sample(BitmapPixels bitmap, double centerX, double centerY, double radius, int angleIndex, int angleCount)
    {
        var direction = angleCount switch
        {
            AngleSamples => MeterDirections[angleIndex],
            DarknessAngleSamples => DarknessDirections[angleIndex],
            _ => CreateDirection(angleIndex, angleCount),
        };
        var x = Math.Clamp((int)Math.Round(centerX + direction.X * radius), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)Math.Round(centerY + direction.Y * radius), 0, bitmap.Height - 1);
        return bitmap.GetPixel(x, y);
    }

    private static (double X, double Y)[] CreateDirections(int count)
    {
        var directions = new (double X, double Y)[count];
        for (var index = 0; index < count; index++)
        {
            directions[index] = CreateDirection(index, count);
        }

        return directions;
    }

    private static (double X, double Y) CreateDirection(int index, int count)
    {
        var angle = index * Math.PI * 2 / count;
        return (Math.Cos(angle), Math.Sin(angle));
    }

    private static bool IsWarmRing(Color color) =>
        color.R >= 48 && color.R >= color.B + 7 && color.R >= color.G + 3;

    private static bool IsLime(Color color) =>
        color.G >= 78 && color.G >= color.R + 14 && color.G >= color.B + 8;

    private static double Luminance(Color color) => color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;

    private sealed record LmbPromptTemplate(
        int Width,
        int Height,
        Point[] Foreground,
        Point[] GlyphBackground,
        Point[] OuterBackground,
        LmbContrastPair[] ContrastPairs);

    private readonly record struct LmbContrastPair(Point Foreground, Point Background);

    private sealed class BitmapPixels : IDisposable
    {
        private readonly Bitmap bitmap;
        private readonly bool ownsBitmap;
        private readonly BitmapData data;
        private readonly byte[] bytes;

        internal BitmapPixels(Bitmap source)
        {
            Width = source.Width;
            Height = source.Height;
            if (source.PixelFormat == PixelFormat.Format32bppArgb)
            {
                bitmap = source;
            }
            else
            {
                bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                ownsBitmap = true;
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawImageUnscaled(source, 0, 0);
            }

            data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            bytes = new byte[Math.Abs(data.Stride) * Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        }

        internal int Width { get; }
        internal int Height { get; }

        internal Color GetPixel(int x, int y)
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
            var row = data.Stride >= 0 ? y : Height - 1 - y;
            var offset = row * Math.Abs(data.Stride) + x * 4;
            return Color.FromArgb(bytes[offset + 2], bytes[offset + 1], bytes[offset]);
        }

        public void Dispose()
        {
            bitmap.UnlockBits(data);
            if (ownsBitmap)
            {
                bitmap.Dispose();
            }
        }
    }
}

internal enum FishingControlAction
{
    None,
    Pulse,
    Complete,
}

internal readonly record struct FishingControlDecision(FishingControlAction Action, int PulseMilliseconds, double VelocityPerSecond)
{
    internal static FishingControlDecision None => new(FishingControlAction.None, 0, 0);
}

internal sealed class FishingTensionController
{
    private readonly double pulseBelowRatio;
    private readonly double targetRatio;
    private readonly int minimumPulseMilliseconds;
    private readonly int maximumPulseMilliseconds;
    private readonly long minimumRestTicks;
    private double? lastTension;
    private long lastObservationAt;
    private long? lastPulseAt;

    internal FishingTensionController(
        int pulseBelowPercent,
        int targetPercent,
        int minimumPulseMilliseconds,
        int maximumPulseMilliseconds,
        int minimumRestMilliseconds)
    {
        pulseBelowRatio = Math.Clamp(pulseBelowPercent / 100d, 0.25, 0.75);
        targetRatio = Math.Clamp(targetPercent / 100d, pulseBelowRatio + 0.05, 0.85);
        this.minimumPulseMilliseconds = Math.Clamp(minimumPulseMilliseconds, 20, 100);
        this.maximumPulseMilliseconds = Math.Clamp(maximumPulseMilliseconds, this.minimumPulseMilliseconds, 120);
        minimumRestTicks = Math.Clamp(minimumRestMilliseconds, 20, 250) * Stopwatch.Frequency / 1_000;
    }

    internal FishingControlDecision Observe(FishingMeterObservation observation, long timestamp)
    {
        if (observation.IsFailed)
        {
            return FishingControlDecision.None;
        }

        if (observation.IsCaught)
        {
            return new FishingControlDecision(FishingControlAction.Complete, 0, 0);
        }

        if (!observation.IsVisible)
        {
            lastTension = null;
            lastObservationAt = 0;
            return FishingControlDecision.None;
        }

        var velocity = 0d;
        if (lastTension is not null && lastObservationAt > 0 && timestamp > lastObservationAt)
        {
            var seconds = (timestamp - lastObservationAt) / (double)Stopwatch.Frequency;
            velocity = (observation.TensionRatio - lastTension.Value) / seconds;
        }

        lastTension = observation.TensionRatio;
        lastObservationAt = timestamp;
        var projected = observation.TensionRatio + Math.Max(0, velocity) * 0.12;

        if (projected > pulseBelowRatio || lastPulseAt is not null && timestamp - lastPulseAt.Value < minimumRestTicks)
        {
            return new FishingControlDecision(FishingControlAction.None, 0, velocity);
        }

        var usableRange = Math.Max(0.05, targetRatio - 0.25);
        var deficit = Math.Clamp((targetRatio - projected) / usableRange, 0, 1);
        var duration = minimumPulseMilliseconds
            + (int)Math.Round((maximumPulseMilliseconds - minimumPulseMilliseconds) * deficit);
        if (velocity > 0.05)
        {
            duration = Math.Max(minimumPulseMilliseconds, (int)Math.Round(duration * 0.6));
        }

        duration = Math.Clamp(duration, minimumPulseMilliseconds, maximumPulseMilliseconds);
        lastPulseAt = timestamp;
        return new FishingControlDecision(FishingControlAction.Pulse, duration, velocity);
    }
}

internal sealed class FishingMeterTracker
{
    private double? centerXRatio;
    private double? centerYRatio;
    private double regionHeightRatio = 0.24;

    internal bool HasLock => centerXRatio is not null && centerYRatio is not null;

    internal void Reset()
    {
        centerXRatio = null;
        centerYRatio = null;
        regionHeightRatio = 0.24;
    }

    internal Rectangle? GetRegion(Rectangle bounds, double scale = 1)
    {
        if (centerXRatio is null || centerYRatio is null) return null;
        var height = Math.Clamp(
            (int)Math.Round(bounds.Height * regionHeightRatio * scale),
            Math.Min(180, bounds.Height),
            Math.Min(440, bounds.Height));
        var width = Math.Min(bounds.Width, (int)Math.Round(height * 1.30));
        var centerX = bounds.Left + (int)Math.Round(bounds.Width * centerXRatio.Value);
        var centerY = bounds.Top + (int)Math.Round(bounds.Height * centerYRatio.Value);
        var left = Math.Clamp(centerX - (int)Math.Round(width * 0.40), bounds.Left, bounds.Right - width);
        var top = Math.Clamp(centerY - height / 2, bounds.Top, bounds.Bottom - height);
        return new Rectangle(left, top, width, height);
    }

    internal void Update(Rectangle frameBounds, FishingMeterCandidateEvidence candidate)
    {
        var shortest = Math.Min(candidate.Region.Width, candidate.Region.Height);
        var radius = shortest * 0.20;
        var expectedX = candidate.Region.Width >= candidate.Region.Height * 1.15
            ? candidate.Region.Width * 0.40
            : candidate.Region.Width / 2d;
        var actualX = candidate.Region.Left + expectedX + candidate.Evidence.CenterOffsetX * radius;
        var actualY = candidate.Region.Top + candidate.Region.Height / 2d + candidate.Evidence.CenterOffsetY * radius;
        centerXRatio = Math.Clamp((actualX - frameBounds.Left) / frameBounds.Width, 0, 1);
        centerYRatio = Math.Clamp((actualY - frameBounds.Top) / frameBounds.Height, 0, 1);
        regionHeightRatio = Math.Clamp(candidate.Region.Height / (double)frameBounds.Height, 0.12, 0.42);
    }
}

internal static class FishingMeterService
{
    private const double DecisiveConfidence = 0.92;

    internal static FishingMeterObservation AnalyzeFrame(Bitmap frame) =>
        AnalyzeFrameDetailed(frame).Observation;

    internal static FishingMeterFrameAnalysis AnalyzeFrameDetailed(
        Bitmap frame,
        FishingMeterTracker? tracker = null)
    {
        var frameBounds = new Rectangle(Point.Empty, frame.Size);
        FishingMeterObservation best = FishingMeterObservation.Missing;
        FishingMeterCandidateEvidence? primary = null;
        var candidateCount = 0;
        var inspected = new HashSet<Rectangle>();

        bool Inspect(Rectangle region, int index, bool tracked = false, double scale = 1)
        {
            if (region.Width < 80 || region.Height < 80 || !inspected.Add(region)) return false;
            candidateCount++;
            using var meter = frame.Clone(region, PixelFormat.Format32bppArgb);
            var observation = FishingMeterDetector.Analyze(
                meter,
                out var evidence,
                requireActiveIdentity: tracker is not null && !tracker.HasLock);
            // Every fresh lock must prove the adjacent live LMB prompt. This
            // includes apparent failure frames: warm scenery can resemble the
            // red failure mark, while a genuine failure is only actionable
            // after this tracker has already observed the active meter.
            var requiresIdentity = tracker is not null && !tracker.HasLock;
            if (observation.IsVisible
                && requiresIdentity
                && !FishingMeterDetector.HasCurrentLmbIdentity(evidence))
            {
                observation = FishingMeterObservation.Missing;
                evidence = evidence with
                {
                    PassedVisibility = false,
                    DecisionReason = $"Meter acquisition requires the verified LMB identity; strongest signature was {evidence.LmbPromptStrength:P0}",
                };
            }
            var candidate = new FishingMeterCandidateEvidence(index, region, evidence, tracked, scale);
            if (observation.IsVisible)
            {
                if (!best.IsVisible || observation.Confidence > best.Confidence)
                {
                    best = observation;
                    primary = candidate;
                }
            }
            else if (!best.IsVisible
                && (primary is null
                    || tracked && !primary.Value.IsTracked
                    || tracked == primary.Value.IsTracked
                        && evidence.CandidateConfidence > primary.Value.Evidence.CandidateConfidence))
            {
                primary = candidate;
            }

            return observation.IsVisible;
        }

        var trackedRegion = tracker?.GetRegion(frameBounds);
        if (trackedRegion is { } firstTracked && Inspect(firstTracked, -1, tracked: true))
        {
            tracker!.Update(frameBounds, primary!.Value);
            return new FishingMeterFrameAnalysis(best, primary, candidateCount);
        }

        // Tiny regression/reference images are already tightly cropped around
        // the meter. Inspecting them directly avoids manufacturing a second
        // crop that cuts off the LMB keycap.
        if (frame.Width <= 512 && frame.Height <= 512 && Inspect(frameBounds, -2))
        {
            tracker?.Update(frameBounds, primary!.Value);
            return new FishingMeterFrameAnalysis(best, primary, candidateCount);
        }

        var staticRegions = GetCaptureRegions(frameBounds);
        for (var index = 0; index < staticRegions.Count; index++)
        {
            _ = Inspect(staticRegions[index], index);
            if (best.IsVisible && best.Confidence >= DecisiveConfidence)
            {
                tracker?.Update(frameBounds, primary!.Value);
                return new FishingMeterFrameAnalysis(best, primary, candidateCount);
            }
        }

        if (best.IsVisible)
        {
            tracker?.Update(frameBounds, primary!.Value);
            return new FishingMeterFrameAnalysis(best, primary, candidateCount);
        }

        if (tracker?.HasLock == true)
        {
            foreach (var scale in new[] { 0.90, 1.10 })
            {
                if (tracker.GetRegion(frameBounds, scale) is not { } scaled) continue;
                if (!Inspect(scaled, scale < 1 ? -3 : -4, tracked: true, scale)) continue;
                tracker.Update(frameBounds, primary!.Value);
                return new FishingMeterFrameAnalysis(best, primary, candidateCount);
            }
        }

        return new FishingMeterFrameAnalysis(best, primary, candidateCount);
    }

    internal static IReadOnlyList<FishingMeterCandidateEvidence> InspectFrame(Bitmap frame)
    {
        var regions = GetCaptureRegions(new Rectangle(Point.Empty, frame.Size));
        var evidence = new FishingMeterCandidateEvidence[regions.Count];
        for (var index = 0; index < regions.Count; index++)
        {
            using var meter = frame.Clone(regions[index], PixelFormat.Format32bppArgb);
            _ = FishingMeterDetector.Analyze(meter, out var candidate);
            evidence[index] = new FishingMeterCandidateEvidence(index, regions[index], candidate);
        }

        return evidence;
    }

    internal static IReadOnlyList<Rectangle> GetCaptureRegions(Rectangle bounds)
    {
        // FiveM positions the fishing UI relative to the active camera/UI layout.
        // The supplied bright-day frames place the meter left-of-center, while the
        // recorded 2560x1440 frame puts it right-of-center. Probe those observed
        // positions with bounded rectangular captures rather than inspecting the
        // full desktop or assuming one fixed coordinate and UI scale.
        var primaryPositions = new[]
        {
            (0.536, 0.443),
            (0.35, 0.53),
            (0.44, 0.50),
            (0.50, 0.50),
            (0.666, 0.55),
        };
        // The user-supplied daylight captures span a second HUD scale and a
        // wider set of camera-relative positions. Probe only those measured
        // centers at the larger scale. This is a bounded image-pyramid fallback,
        // not a full-screen sliding window, so the live loop stays predictable.
        var expandedPositions = new[]
        {
            (0.367, 0.369),
            (0.287, 0.431),
            (0.406, 0.550),
        };
        var safeViewport = GameViewportGeometry.CenteredSafeViewport(bounds);
        IEnumerable<Rectangle> RegionsFor(GameSafeViewport viewport) => primaryPositions
            .Select(position => GetCaptureRegion(bounds, viewport, position.Item1, position.Item2, 0.24))
            .Concat(expandedPositions.Select(position =>
                GetCaptureRegion(bounds, viewport, position.Item1, position.Item2, 0.46)));

        var regions = RegionsFor(safeViewport);
        if (Math.Abs(safeViewport.Left - bounds.Left) >= 1
            || Math.Abs(safeViewport.Width - bounds.Width) >= 1)
        {
            // Some FiveM resources position NUI relative to the full visible
            // frame rather than the virtual 16:9 canvas. Probe both on every
            // non-16:9 layout, including 3440x1440 and 5120x1440 ultrawide.
            regions = regions.Concat(RegionsFor(new GameSafeViewport(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height)));
        }

        return regions.Distinct().ToArray();
    }

    private static Rectangle GetCaptureRegion(
        Rectangle bounds,
        GameSafeViewport safeViewport,
        double horizontal,
        double vertical,
        double heightRatio = 0.24)
    {
        // The supplied 1080p recording puts the black meter disk at roughly
        // 100 px across. The primary 24% capture makes that disk approximately
        // two fifths of the crop, which matches the ring sampler's radius range.
        // The measured 46% fallback handles the alternate UI scale without
        // replacing the faster primary path.
        var height = Math.Clamp((int)Math.Round(bounds.Height * heightRatio), 240, 400);
        height = Math.Min(height, Math.Min(bounds.Width, bounds.Height));
        var width = Math.Min(bounds.Width, (int)Math.Round(height * 1.30));
        var centerX = (int)Math.Round(safeViewport.MapX(horizontal));
        var centerY = (int)Math.Round(safeViewport.MapY(vertical));
        // Keep the circular meter at 40% of the crop width, leaving a narrow
        // right-hand lane for the actual Increase Tension/LMB keycap.
        var left = Math.Clamp(centerX - (int)Math.Round(width * 0.40), bounds.Left, bounds.Right - width);
        var top = Math.Clamp(centerY - height / 2, bounds.Top, bounds.Bottom - height);
        return new Rectangle(left, top, width, height);
    }

    internal static FishingMeterObservation Observe(IFrameSource frameSource, WindowTargetSettings target, out FrameSourceStatus status)
    {
        using var sample = CaptureAndAnalyze(frameSource, target, null, out status);
        return sample?.Analysis.Observation ?? FishingMeterObservation.Missing;
    }

    internal static FishingMeterFrameSample? CaptureAndAnalyze(
        IFrameSource frameSource,
        WindowTargetSettings target,
        FishingMeterTracker? tracker,
        out FrameSourceStatus status)
    {
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            status = new FrameSourceStatus(FrameSourceState.TargetUnavailable, frameSource.Name, detail, TimeSpan.MaxValue, 0);
            return null;
        }

        // The pulse UI animates while the five candidate positions are being
        // inspected. Capturing them one at a time can therefore combine pixels
        // from different UI states and lose a meter that is visibly present.
        // Capture one coherent FiveM frame, then inspect every calibrated
        // candidate region inside that exact frame.
        var wholeTarget = new Rectangle(Point.Empty, resolved.Bounds.Size);
        if (!frameSource.TryCapture(target, wholeTarget, out var frame, out status) || frame is null)
        {
            return null;
        }

        try
        {
            return new FishingMeterFrameSample(frame, AnalyzeFrameDetailed(frame.Bitmap, tracker));
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

}

internal sealed class FishingMeterFrameSample(
    FrameLease frame,
    FishingMeterFrameAnalysis analysis) : IDisposable
{
    internal FrameLease Frame { get; } = frame;
    internal FishingMeterFrameAnalysis Analysis { get; } = analysis;
    internal FishingMeterObservation Observation => Analysis.Observation;
    public void Dispose() => Frame.Dispose();
}

internal sealed class FishingDiagnosticLog : IDisposable
{
    private readonly StreamWriter writer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly string directory;

    internal FishingDiagnosticLog(string? diagnosticsDirectory = null)
    {
        directory = diagnosticsDirectory ?? AppPaths.DiagnosticsDirectory;
        Directory.CreateDirectory(directory);
        writer = new StreamWriter(Path.Combine(directory, "last-fishing.csv"), false);
        writer.WriteLine("elapsed_ms,visible,tension_percent,progress_percent,caught,failed,confidence_percent,lmb,event,pulse_ms");
        writer.AutoFlush = true;
    }

    internal void Write(FishingMeterObservation observation, bool holding, string eventName = "sample", int pulseMilliseconds = 0)
    {
        writer.WriteLine(string.Join(',',
            clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            observation.IsVisible ? "1" : "0",
            (observation.TensionRatio * 100).ToString("F1", CultureInfo.InvariantCulture),
            (observation.ProgressRatio * 100).ToString("F1", CultureInfo.InvariantCulture),
            observation.IsCaught ? "1" : "0",
            observation.IsFailed ? "1" : "0",
            (observation.Confidence * 100).ToString("F1", CultureInfo.InvariantCulture),
            holding ? "down" : "up",
            eventName,
            pulseMilliseconds.ToString(CultureInfo.InvariantCulture)));
    }

    internal string CaptureEvidence(
        string eventName,
        Bitmap exactFrame,
        FishingMeterFrameAnalysis analysis)
    {
        var safeEventName = eventName.Equals("meter-lock", StringComparison.OrdinalIgnoreCase)
            ? "meter-lock"
            : "meter-loss";
        var imageName = $"{safeEventName}-latest.png";
        var metadataName = $"{safeEventName}-latest.json";
        var imagePath = Path.Combine(directory, imageName);
        var metadataPath = Path.Combine(directory, metadataName);
        using var annotated = new Bitmap(exactFrame);
        using (var graphics = Graphics.FromImage(annotated))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (analysis.PrimaryCandidate is { } candidate)
            {
                var region = candidate.Region;
                var shortest = Math.Min(region.Width, region.Height);
                var meterRadius = shortest * 0.20;
                var expectedCenterX = region.Width >= region.Height * 1.15
                    ? region.Width * 0.40
                    : region.Width / 2d;
                var centerX = region.Left + expectedCenterX + candidate.Evidence.CenterOffsetX * meterRadius;
                var centerY = region.Top + region.Height / 2d + candidate.Evidence.CenterOffsetY * meterRadius;
                var accent = analysis.Observation.IsVisible
                    ? Color.FromArgb(232, 120, 231, 200)
                    : Color.FromArgb(232, 244, 186, 92);
                using var regionPen = new Pen(Color.FromArgb(190, accent), 2);
                using var meterPen = new Pen(accent, 3);
                graphics.DrawRectangle(regionPen, region);
                graphics.DrawEllipse(
                    meterPen,
                    (float)(centerX - meterRadius),
                    (float)(centerY - meterRadius),
                    (float)(meterRadius * 2),
                    (float)(meterRadius * 2));
                graphics.DrawLine(meterPen, (float)centerX - 8, (float)centerY, (float)centerX + 8, (float)centerY);
                graphics.DrawLine(meterPen, (float)centerX, (float)centerY - 8, (float)centerX, (float)centerY + 8);

                var label = string.Create(CultureInfo.InvariantCulture,
                    $"{safeEventName}  dark {candidate.Evidence.DarkDisk:P0}  contrast {candidate.Evidence.DiskContrast:P0}  ring {candidate.Evidence.RingStrength:P0}  LMB {candidate.Evidence.LmbPromptStrength:P0}  progress {candidate.Evidence.ProgressStrength:P0}");
                using var font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold, GraphicsUnit.Point);
                var labelSize = graphics.MeasureString(label, font);
                var labelX = Math.Clamp(region.Left, 8, Math.Max(8, annotated.Width - (int)labelSize.Width - 20));
                var labelY = Math.Max(8, region.Top - (int)labelSize.Height - 12);
                using var labelBackground = new SolidBrush(Color.FromArgb(220, 10, 22, 24));
                using var labelBrush = new SolidBrush(Color.FromArgb(245, 224, 239, 235));
                graphics.FillRectangle(labelBackground, labelX - 6, labelY - 4, labelSize.Width + 12, labelSize.Height + 8);
                graphics.DrawString(label, font, labelBrush, labelX, labelY);
            }
        }

        annotated.Save(imagePath, ImageFormat.Png);
        var primary = analysis.PrimaryCandidate;
        var metadata = new
        {
            eventName = safeEventName,
            capturedAt = DateTimeOffset.Now,
            visible = analysis.Observation.IsVisible,
            tension = analysis.Observation.TensionRatio,
            progress = analysis.Observation.ProgressRatio,
            confidence = analysis.Observation.Confidence,
            caught = analysis.Observation.IsCaught,
            failed = analysis.Observation.IsFailed,
            tracked = analysis.UsedTrackedRegion,
            candidateCount = analysis.CandidateCount,
            region = primary is null ? null : new
            {
                x = primary.Value.Region.X,
                y = primary.Value.Region.Y,
                width = primary.Value.Region.Width,
                height = primary.Value.Region.Height,
                scale = primary.Value.Scale,
            },
            evidence = primary is null ? null : new
            {
                darkDisk = primary.Value.Evidence.DarkDisk,
                diskContrast = primary.Value.Evidence.DiskContrast,
                ringStrength = primary.Value.Evidence.RingStrength,
                ringRadius = primary.Value.Evidence.RingRadiusRatio,
                lmbPrompt = primary.Value.Evidence.LmbPromptStrength,
                progressStrength = primary.Value.Evidence.ProgressStrength,
                caughtStrength = primary.Value.Evidence.CaughtStrength,
                failureStrength = primary.Value.Evidence.FailureStrength,
                candidateConfidence = primary.Value.Evidence.CandidateConfidence,
            },
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        FishingLoopDiagnosticLog.Write(
            $"{safeEventName.Replace('-', '_')}_capture",
            $"file={imageName};tracked={analysis.UsedTrackedRegion};candidates={analysis.CandidateCount}");
        return imagePath;
    }

    public void Dispose() => writer.Dispose();
}

internal static class FishingLoopDiagnosticLog
{
    private static readonly object Sync = new();

    internal static void Write(string eventName, string detail = "")
    {
        var directory = AppPaths.DiagnosticsDirectory;
        Directory.CreateDirectory(directory);
        var line = string.Join(',',
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            eventName.Replace(',', '_'),
            detail.Replace(',', ';'));
        lock (Sync)
        {
            File.AppendAllText(Path.Combine(directory, "fishing-loop.csv"), line + Environment.NewLine);
        }
    }
}
