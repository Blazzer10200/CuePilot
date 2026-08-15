using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CuePilot;

internal enum LockpickingVisualState
{
    Hidden,
    Numbered,
    Intermediate,
    Spin,
    Open,
    Unexpected,
}

internal enum LockpickingTargetPhase
{
    None,
    Approaching,
    Ready,
}

internal sealed record LockpickingTargetObservation(
    double CenterX,
    double CenterY,
    double ApproachRadius,
    LockpickingTargetPhase Phase,
    double Confidence,
    int? Number = null,
    double ApproachRatio = 0,
    double RadialVelocity = 0,
    double? TimeToReadyMilliseconds = null,
    double FillDensity = 0);

internal sealed record LockpickingObservation(
    LockpickingVisualState State,
    double Confidence,
    double HudCenterX,
    double HudCenterY,
    double HudRadius,
    LockpickingTargetObservation? Target,
    int VisibleTargetCount,
    string PredictedAction,
    string Reason)
{
    internal static LockpickingObservation Hidden(string reason = "Lockpicking HUD not found.") => new(
        LockpickingVisualState.Hidden, 0, 0, 0, 0, null, 0, "WAIT", reason);
}

internal sealed record LockpickingDetectorEvidence(
    double HudConfidence,
    double OpenRingCoverage,
    double SpinRingCoverage,
    double BottomLabelSignal,
    IReadOnlyList<double> ArcProfile);

internal static class LockpickingDetector
{
    private const int CircleSamples = 72;
    private static readonly double[] RecordedHudRadiusRatios = [0.185, 0.205, 0.225, 0.245];
    private static readonly double[] CompatibleHudRadiusRatios =
        [0.145, 0.165, 0.185, 0.205, 0.225, 0.245, 0.265, 0.285, 0.305];

    internal static LockpickingObservation Analyze(Bitmap frame) => Analyze(frame, null);

    internal static LockpickingObservation Analyze(Bitmap frame, LockpickingObservation? previous)
    {
        using var pixels = new LockpickingPixels(frame);
        var hud = LocateHud(pixels, previous);
        if (hud.Score < 0.30)
        {
            return LockpickingObservation.Hidden($"HUD ring confidence {hud.Score:P0} is below the observe threshold.");
        }

        var openCoverage = RingCoverage(pixels, hud.X, hud.Y, hud.Radius * 0.58, hud.Radius * 0.025);
        var spinCoverage = Math.Max(
            RingCoverage(pixels, hud.X, hud.Y, hud.Radius * 0.63, hud.Radius * 0.025),
            RingCoverage(pixels, hud.X, hud.Y, hud.Radius * 0.69, hud.Radius * 0.025));
        var bottomSignal = RegionGreenDensity(
            pixels,
            hud.X - hud.Radius * 0.24,
            hud.Y + hud.Radius * 0.40,
            hud.Radius * 0.48,
            hud.Radius * 0.18);

        if (openCoverage > 0.62)
        {
            return Create(hud, LockpickingVisualState.Open, Math.Min(1, 0.55 + openCoverage * 0.45), null, 0,
                "GATED", "Full inner unlock ring detected. OPEN input remains calibration-gated.");
        }

        var strongSpinArc = spinCoverage > 0.23 && bottomSignal > 0.035;
        var earlySpinTransition = spinCoverage > 0.10 && bottomSignal > 0.055;
        if (strongSpinArc || earlySpinTransition)
        {
            return Create(hud, LockpickingVisualState.Spin,
                Math.Min(1, 0.48 + spinCoverage * 0.42 + Math.Min(0.1, bottomSignal)), null, 0,
                "GATED", "Partial inner arc and lower action label detected. SPIN input remains calibration-gated.");
        }

        var targets = FindTargets(pixels, hud, previous);
        if (targets.Active is not null)
        {
            var target = targets.Active;
            return Create(hud, LockpickingVisualState.Numbered,
                Math.Min(1, hud.Score * 0.45 + target.Confidence * 0.55), target, targets.VisibleCount, "WAIT",
                "Numbered target acquired. Temporal verification is required before READY can be reported.");
        }

        if (targets.VisibleCount > 0)
        {
            return Create(hud, LockpickingVisualState.Numbered,
                Math.Min(1, hud.Score * 0.78 + Math.Min(0.22, targets.VisibleCount * 0.035)), null, targets.VisibleCount,
                "WAIT", "Numbered targets are visible; waiting for a distinct shrinking approach ring.");
        }

        var centerSignal = RingCoverage(pixels, hud.X, hud.Y, hud.Radius * 0.16, hud.Radius * 0.02);
        if (centerSignal > 0.18)
        {
            return Create(hud, LockpickingVisualState.Intermediate,
                Math.Min(1, hud.Score * 0.8 + centerSignal * 0.2), null, 0, "WAIT",
                "HUD remains visible without a confident numbered target.");
        }

        return Create(hud, LockpickingVisualState.Intermediate, hud.Score * 0.75, null, 0, "WAIT",
            "HUD boundary is visible between recognized numbered or action states.");
    }

    internal static LockpickingDetectorEvidence Inspect(Bitmap frame)
    {
        using var pixels = new LockpickingPixels(frame);
        var hud = LocateHud(pixels, null);
        return new LockpickingDetectorEvidence(
            hud.Score,
            RingCoverage(pixels, hud.X, hud.Y, hud.Radius * 0.58, hud.Radius * 0.025),
            Math.Max(
                RingCoverage(pixels, hud.X, hud.Y, hud.Radius * 0.63, hud.Radius * 0.025),
                RingCoverage(pixels, hud.X, hud.Y, hud.Radius * 0.69, hud.Radius * 0.025)),
            RegionGreenDensity(
                pixels,
                hud.X - hud.Radius * 0.24,
                hud.Y + hud.Radius * 0.40,
                hud.Radius * 0.48,
                hud.Radius * 0.18),
            Enumerable.Range(0, 9)
                .Select(index => RingCoverage(
                    pixels,
                    hud.X,
                    hud.Y,
                    hud.Radius * (0.42 + index * 0.05),
                    hud.Radius * 0.02))
                .ToArray());
    }

    private static LockpickingObservation Create(
        HudCandidate hud,
        LockpickingVisualState state,
        double confidence,
        LockpickingTargetObservation? target,
        int targetCount,
        string predictedAction,
        string reason) => new(
            state,
            confidence,
            hud.X / hud.Width,
            hud.Y / hud.Height,
            hud.Radius / Math.Min(hud.Width, hud.Height),
            target is null ? null : target with
            {
                CenterX = target.CenterX / hud.Width,
                CenterY = target.CenterY / hud.Height,
                ApproachRadius = target.ApproachRadius / Math.Min(hud.Width, hud.Height),
            },
            targetCount,
            predictedAction,
            reason);

    private static HudCandidate LocateHud(LockpickingPixels pixels, LockpickingObservation? previous)
    {
        var minimum = Math.Min(pixels.Width, pixels.Height);
        var best = new HudCandidate(0, 0, 0, 0, pixels.Width, pixels.Height);
        if (previous is not null && previous.HudRadius > 0)
        {
            var priorX = previous.HudCenterX * pixels.Width;
            var priorY = previous.HudCenterY * pixels.Height;
            var priorRadius = previous.HudRadius * minimum;
            var trackedXyStep = Math.Max(2, minimum / 270d);
            var trackedRadiusStep = Math.Max(2, minimum / 300d);
            for (var y = priorY - trackedXyStep; y <= priorY + trackedXyStep; y += trackedXyStep)
            {
                for (var x = priorX - trackedXyStep; x <= priorX + trackedXyStep; x += trackedXyStep)
                {
                    for (var radius = priorRadius - trackedRadiusStep; radius <= priorRadius + trackedRadiusStep; radius += trackedRadiusStep)
                    {
                        var outer = RingCoverage(pixels, x, y, radius, radius * 0.018);
                        var center = RingCoverage(pixels, x, y, radius * 0.16, radius * 0.025);
                        var dark = InteriorDarkness(pixels, x, y, radius);
                        var score = outer * 0.68 + center * 0.19 + dark * 0.13;
                        if (score > best.Score)
                        {
                            best = new HudCandidate(x, y, radius, score, pixels.Width, pixels.Height);
                        }
                    }
                }
            }
            if (best.Score >= 0.27)
            {
                return best;
            }
            best = new HudCandidate(0, 0, 0, 0, pixels.Width, pixels.Height);
        }

        // Preserve the measured 16:9 acquisition path exactly. Besides being
        // faster, its grid alignment is part of the live calibration evidence.
        for (var yRatio = 0.34; yRatio <= 0.66; yRatio += 0.04)
        {
            for (var xRatio = 0.58; xRatio <= 0.84; xRatio += 0.025)
            {
                foreach (var radiusRatio in RecordedHudRadiusRatios)
                {
                    var x = xRatio * pixels.Width;
                    var y = yRatio * pixels.Height;
                    var radius = radiusRatio * minimum;
                    ConsiderHudCandidate(pixels, x, y, radius, ref best);
                }
            }
        }

        if (best.Score >= 0.30)
        {
            return RefineHudCandidate(pixels, best, minimum);
        }

        // GTA/FiveM keeps HUD elements in a centered safe viewport on wide
        // displays. Search that height-based canvas with a denser horizontal
        // grid and a wider scale range for ultrawide and windowed layouts.
        var safeViewport = GameViewportGeometry.CenteredSafeViewport(
            new Rectangle(0, 0, pixels.Width, pixels.Height));
        for (var yRatio = 0.28; yRatio <= 0.72; yRatio += 0.04)
        {
            for (var safeXRatio = 0.50; safeXRatio <= 0.90; safeXRatio += 0.0125)
            {
                foreach (var radiusRatio in CompatibleHudRadiusRatios)
                {
                    var x = safeViewport.MapX(safeXRatio);
                    var y = safeViewport.MapY(yRatio);
                    var radius = radiusRatio * minimum;
                    ConsiderHudCandidate(pixels, x, y, radius, ref best);
                }
            }
        }

        if (best.Score <= 0)
        {
            return best;
        }

        return RefineHudCandidate(pixels, best, minimum);
    }

    private static HudCandidate RefineHudCandidate(LockpickingPixels pixels, HudCandidate best, double minimum)
    {
        var refined = best;
        var xyStep = Math.Max(2, minimum / 180d);
        var radiusStep = Math.Max(2, minimum / 240d);
        for (var y = best.Y - xyStep * 2; y <= best.Y + xyStep * 2; y += xyStep)
        {
            for (var x = best.X - xyStep * 2; x <= best.X + xyStep * 2; x += xyStep)
            {
                for (var radius = best.Radius - radiusStep * 2; radius <= best.Radius + radiusStep * 2; radius += radiusStep)
                {
                    var outer = RingCoverage(pixels, x, y, radius, radius * 0.018);
                    var center = RingCoverage(pixels, x, y, radius * 0.16, radius * 0.025);
                    var dark = InteriorDarkness(pixels, x, y, radius);
                    var score = outer * 0.68 + center * 0.19 + dark * 0.13;
                    if (score > refined.Score)
                    {
                        refined = new HudCandidate(x, y, radius, score, pixels.Width, pixels.Height);
                    }
                }
            }
        }
        return refined;
    }

    private static void ConsiderHudCandidate(
        LockpickingPixels pixels,
        double x,
        double y,
        double radius,
        ref HudCandidate best)
    {
        if (radius <= 0
            || x - radius < 0
            || y - radius < 0
            || x + radius >= pixels.Width
            || y + radius >= pixels.Height)
        {
            return;
        }

        var outer = RingCoverage(pixels, x, y, radius, radius * 0.018);
        var center = RingCoverage(pixels, x, y, radius * 0.16, radius * 0.025);
        var dark = InteriorDarkness(pixels, x, y, radius);
        var score = outer * 0.68 + center * 0.19 + dark * 0.13;
        if (score > best.Score)
        {
            best = new HudCandidate(x, y, radius, score, pixels.Width, pixels.Height);
        }
    }

    private static TargetDetectionResult FindTargets(
        LockpickingPixels pixels,
        HudCandidate hud,
        LockpickingObservation? previous)
    {
        var candidates = new List<TargetCandidate>();
        var step = Math.Max(3, (int)Math.Round(hud.Radius / 42));
        // The numbered target's stable outline is about 13% of the HUD radius.
        // The animated approach ring is a separate, larger circle that contracts
        // from roughly 1.5x that outline toward coincidence.
        var targetRadius = hud.Radius * 0.13;
        var left = (int)Math.Max(0, hud.X - hud.Radius * 0.74);
        var right = (int)Math.Min(pixels.Width - 1, hud.X + hud.Radius * 0.74);
        var top = (int)Math.Max(0, hud.Y - hud.Radius * 0.74);
        var bottom = (int)Math.Min(pixels.Height - 1, hud.Y + hud.Radius * 0.74);
        for (var y = top; y <= bottom; y += step)
        {
            for (var x = left; x <= right; x += step)
            {
                var distance = Math.Sqrt(Math.Pow(x - hud.X, 2) + Math.Pow(y - hud.Y, 2));
                if (distance < hud.Radius * 0.24 || distance > hud.Radius * 0.76)
                {
                    continue;
                }

                var quickTargetRing = RingCoverage(pixels, x, y, targetRadius, targetRadius * 0.10, 12);
                if (quickTargetRing < 0.075)
                {
                    continue;
                }
                var targetRing = RingCoverage(pixels, x, y, targetRadius, targetRadius * 0.12, 48);
                if (targetRing < 0.14)
                {
                    continue;
                }
                var fill = DiskGreenDensity(pixels, x, y, targetRadius * 0.62);
                var bestApproach = 0d;
                var bestApproachRadius = targetRadius;
                for (var ratio = 1.22; ratio <= 1.74; ratio += 0.04)
                {
                    var radius = targetRadius * ratio;
                    var coverage = RingCoverage(pixels, x, y, radius, targetRadius * 0.05, 64);
                    if (coverage > bestApproach)
                    {
                        bestApproach = coverage;
                        bestApproachRadius = radius;
                    }
                }
                var bright = fill > 0.27;
                var coincident = !bright && bestApproachRadius <= targetRadius * 1.26 && bestApproach > 0.10;
                var score = targetRing * 0.55 + Math.Min(1, fill * 1.6) * 0.13 + bestApproach * 0.32;
                if (score >= 0.20)
                {
                    candidates.Add(new TargetCandidate(x, y, bestApproachRadius, bestApproach, fill, bright, coincident, score));
                }
            }
        }

        var clustered = new List<TargetCandidate>();
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score))
        {
            if (clustered.Any(existing => Distance(existing.X, existing.Y, candidate.X, candidate.Y) < targetRadius * 1.65))
            {
                continue;
            }
            clustered.Add(candidate);
            if (clustered.Count == 10)
            {
                break;
            }
        }

        var approachActive = clustered
            .Where(candidate => !candidate.Bright && candidate.ApproachCoverage >= 0.10)
            .OrderByDescending(candidate => candidate.Coincident)
            .ThenByDescending(candidate => candidate.ApproachCoverage)
            .ThenByDescending(candidate => candidate.Score)
            .FirstOrDefault();
        TargetCandidate? continuation = null;
        if (previous?.Target is not null)
        {
            var previousX = previous.Target.CenterX * pixels.Width;
            var previousY = previous.Target.CenterY * pixels.Height;
            continuation = clustered
                .Where(candidate => Distance(candidate.X, candidate.Y, previousX, previousY) <= targetRadius * 1.35)
                .OrderBy(candidate => Distance(candidate.X, candidate.Y, previousX, previousY))
                .ThenByDescending(candidate => candidate.Score)
                .FirstOrDefault();
        }

        // A bright fill is the live READY cue, not merely a completed target. Keep
        // following the same circle for that transition, then prefer the next
        // outlined target on the following frame.
        var previousWasBright = previous?.Target?.FillDensity >= 0.27;
        var active = previousWasBright
            ? approachActive ?? continuation
            : continuation ?? approachActive;

        return new TargetDetectionResult(
            active is null ? null : new LockpickingTargetObservation(
                active.X,
                active.Y,
                active.ApproachRadius,
                LockpickingTargetPhase.Approaching,
                Math.Clamp(active.Score, 0, 1),
                ApproachRatio: active.ApproachRadius / targetRadius,
                FillDensity: active.FillDensity),
            clustered.Count);
    }

    private static double RingCoverage(
        LockpickingPixels pixels,
        double centerX,
        double centerY,
        double radius,
        double tolerance,
        int samples = CircleSamples)
    {
        var strong = 0d;
        for (var index = 0; index < samples; index++)
        {
            var angle = index * Math.PI * 2 / samples;
            var best = 0d;
            for (var offset = -tolerance; offset <= tolerance; offset += Math.Max(1, tolerance))
            {
                var sampleRadius = radius + offset;
                var x = (int)Math.Round(centerX + Math.Cos(angle) * sampleRadius);
                var y = (int)Math.Round(centerY + Math.Sin(angle) * sampleRadius);
                best = Math.Max(best, pixels.GreenStrength(x, y));
            }
            strong += Math.Min(1, best * 1.45);
        }
        return strong / samples;
    }

    private static double DiskGreenDensity(LockpickingPixels pixels, double centerX, double centerY, double radius)
    {
        var step = Math.Max(1, (int)Math.Round(radius / 5));
        var total = 0;
        var signal = 0d;
        for (var y = (int)(centerY - radius); y <= centerY + radius; y += step)
        {
            for (var x = (int)(centerX - radius); x <= centerX + radius; x += step)
            {
                if (Distance(x, y, centerX, centerY) > radius)
                {
                    continue;
                }
                total++;
                signal += pixels.GreenStrength(x, y);
            }
        }
        return total == 0 ? 0 : signal / total;
    }

    private static double RegionGreenDensity(
        LockpickingPixels pixels,
        double left,
        double top,
        double width,
        double height)
    {
        var step = Math.Max(1, (int)Math.Round(Math.Min(width, height) / 16));
        var total = 0;
        var signal = 0d;
        for (var y = (int)top; y < top + height; y += step)
        {
            for (var x = (int)left; x < left + width; x += step)
            {
                total++;
                signal += pixels.GreenStrength(x, y);
            }
        }
        return total == 0 ? 0 : signal / total;
    }

    private static double InteriorDarkness(LockpickingPixels pixels, double centerX, double centerY, double radius)
    {
        var inside = 0d;
        var outside = 0d;
        var samples = 32;
        for (var index = 0; index < samples; index++)
        {
            var angle = index * Math.PI * 2 / samples;
            inside += pixels.Luminance(
                (int)Math.Round(centerX + Math.Cos(angle) * radius * 0.72),
                (int)Math.Round(centerY + Math.Sin(angle) * radius * 0.72));
            outside += pixels.Luminance(
                (int)Math.Round(centerX + Math.Cos(angle) * radius * 1.08),
                (int)Math.Round(centerY + Math.Sin(angle) * radius * 1.08));
        }
        var difference = outside / samples - inside / samples;
        return Math.Clamp((difference + 8) / 58, 0, 1);
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    private sealed record HudCandidate(double X, double Y, double Radius, double Score, int Width, int Height);
    private sealed record TargetCandidate(
        double X,
        double Y,
        double ApproachRadius,
        double ApproachCoverage,
        double FillDensity,
        bool Bright,
        bool Coincident,
        double Score);
    private sealed record TargetDetectionResult(LockpickingTargetObservation? Active, int VisibleCount);

    private sealed class LockpickingPixels : IDisposable
    {
        private readonly Bitmap bitmap;
        private readonly bool ownsBitmap;
        private readonly BitmapData data;
        private readonly byte[] bytes;
        private readonly int stride;

        internal LockpickingPixels(Bitmap source)
        {
            if (source.PixelFormat != PixelFormat.Format32bppArgb)
            {
                bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, source.Width, source.Height),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel);
                ownsBitmap = true;
            }
            else
            {
                bitmap = source;
            }
            Width = bitmap.Width;
            Height = bitmap.Height;
            data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            stride = Math.Abs(data.Stride);
            bytes = new byte[stride * Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        }

        internal int Width { get; }
        internal int Height { get; }

        internal double GreenStrength(int x, int y)
        {
            if ((uint)x >= Width || (uint)y >= Height)
            {
                return 0;
            }
            var offset = y * stride + x * 4;
            var blue = bytes[offset];
            var green = bytes[offset + 1];
            var red = bytes[offset + 2];
            var dominance = green - Math.Max(red, blue);
            var brightness = (green - 35) / 170d;
            return Math.Clamp(dominance / 95d, 0, 1) * Math.Clamp(brightness, 0, 1);
        }

        internal double Luminance(int x, int y)
        {
            if ((uint)x >= Width || (uint)y >= Height)
            {
                return 0;
            }
            var offset = y * stride + x * 4;
            return (bytes[offset] * 0.0722 + bytes[offset + 1] * 0.7152 + bytes[offset + 2] * 0.2126) / 255d;
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
