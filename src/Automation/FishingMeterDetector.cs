using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WorkflowLooper;

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

internal static class FishingMeterReacquisition
{
    // FiveM briefly fades/recomposes the circular UI around a pulse. A meter is
    // only considered gone after this many consecutive, successfully-captured
    // detector misses; capture failures are handled separately by the engine.
    internal const int ConsecutiveMissingSamplesBeforeComplete = 36;

    internal static bool HasEnded(int consecutiveMissingSamples, double highestProgress) =>
        consecutiveMissingSamples >= ConsecutiveMissingSamplesBeforeComplete && highestProgress < 0.55;

    internal static bool ShouldCollectAfterMeterLoss(int consecutiveMissingSamples, double highestProgress) =>
        consecutiveMissingSamples >= ConsecutiveMissingSamplesBeforeComplete && highestProgress >= 0.55;
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

    internal static FishingMeterObservation Analyze(Bitmap bitmap)
    {
        if (bitmap.Width < 80 || bitmap.Height < 80)
        {
            return FishingMeterObservation.Missing;
        }

        using var pixels = new BitmapPixels(bitmap);
        var shortest = Math.Min(pixels.Width, pixels.Height);
        var approximateRadius = shortest * 0.20;
        var expectedCenterX = pixels.Width / 2d;
        var expectedCenterY = pixels.Height / 2d;
        var centerX = expectedCenterX;
        var centerY = expectedCenterY;
        var searchDistance = Math.Max(4, (int)Math.Round(shortest * 0.08));
        var searchStep = Math.Max(2, shortest / 80);
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
        // drift into the animated background.
        var refinedX = centerX;
        var refinedY = centerY;
        for (var offsetY = -searchStep; offsetY <= searchStep; offsetY++)
        {
            for (var offsetX = -searchStep; offsetX <= searchStep; offsetX++)
            {
                var candidate = DarkDiskScore(pixels, centerX + offsetX, centerY + offsetY, approximateRadius);
                if (candidate > bestDarkness)
                {
                    bestDarkness = candidate;
                    refinedX = centerX + offsetX;
                    refinedY = centerY + offsetY;
                }
            }
        }

        centerX = refinedX;
        centerY = refinedY;
        var ringRadius = FindTensionRing(pixels, centerX, centerY, approximateRadius, out var ringStrength);
        var progress = MeasureProgress(pixels, centerX, centerY, approximateRadius);
        var caughtStrength = MeasureCaughtMark(pixels, centerX, centerY, approximateRadius);
        var failureStrength = MeasureFailureMark(pixels, centerX, centerY, approximateRadius);
        var promptStrength = MeasurePrompt(pixels);
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
        var visible = bestDarkness >= 0.56
            && ((ringStrength >= 0.16
                    && (progress >= 0.03 || caughtStrength >= 0.015 || failureStrength >= 0.02
                        || initialRing && promptStrength >= 0.012))
                || completedArc);
        if (!visible)
        {
            return FishingMeterObservation.Missing;
        }

        var confidence = Math.Clamp(
            (bestDarkness - 0.45) * 1.8 + ringStrength * 1.5 + progress * 0.35 + promptStrength * 4,
            0,
            1);
        return new FishingMeterObservation(
            true,
            Math.Clamp(ringRadius / approximateRadius, 0, 1.2),
            Math.Clamp(progress, 0, 1),
            caughtStrength >= 0.035 && progress >= 0.35,
            confidence,
            failureStrength >= 0.02);
    }

    private static double DarkDiskScore(BitmapPixels bitmap, double centerX, double centerY, double radius)
    {
        var dark = 0;
        var count = 0;
        for (var radialStep = 3; radialStep <= 9; radialStep++)
        {
            var sampleRadius = radius * radialStep / 10d;
            for (var angleIndex = 0; angleIndex < 48; angleIndex++)
            {
                var color = Sample(bitmap, centerX, centerY, sampleRadius, angleIndex, 48);
                if (Luminance(color) < 62)
                {
                    dark++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : dark / (double)count;
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

    private static double MeasurePrompt(BitmapPixels bitmap)
    {
        // The game's static "Increase Tension / LMB" prompt sits immediately
        // to the right of the meter and is visible before the lime progress arc
        // has developed. It distinguishes the initial meter from reel geometry.
        var left = (int)Math.Round(bitmap.Width * 0.70);
        var right = bitmap.Width - 1;
        var top = (int)Math.Round(bitmap.Height * 0.18);
        var bottom = (int)Math.Round(bitmap.Height * 0.55);
        var brightNeutral = 0;
        var count = 0;
        for (var y = top; y <= bottom; y += 2)
        {
            for (var x = left; x <= right; x += 2)
            {
                var color = bitmap.GetPixel(x, y);
                var brightest = Math.Max(color.R, Math.Max(color.G, color.B));
                var darkest = Math.Min(color.R, Math.Min(color.G, color.B));
                if ((color.R + color.G + color.B) / 3 >= 150 && brightest - darkest <= 45)
                {
                    brightNeutral++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : brightNeutral / (double)count;
    }

    private static Color Sample(BitmapPixels bitmap, double centerX, double centerY, double radius, int angleIndex, int angleCount)
    {
        var angle = angleIndex * Math.PI * 2 / angleCount;
        var x = Math.Clamp((int)Math.Round(centerX + Math.Cos(angle) * radius), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)Math.Round(centerY + Math.Sin(angle) * radius), 0, bitmap.Height - 1);
        return bitmap.GetPixel(x, y);
    }

    private static bool IsWarmRing(Color color) =>
        color.R >= 78 && color.R >= color.B + 8 && color.R >= color.G + 4;

    private static bool IsLime(Color color) =>
        color.G >= 120 && color.G >= color.R + 18 && color.G >= color.B + 10;

    private static double Luminance(Color color) => color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;

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

internal static class FishingMeterService
{
    private const double DecisiveConfidence = 0.92;

    internal static Rectangle GetRelativeCaptureRegion(Size size) =>
        GetCaptureRegion(new Rectangle(Point.Empty, size));

    internal static FishingMeterObservation AnalyzeFrame(Bitmap frame)
    {
        var best = FishingMeterObservation.Missing;
        foreach (var region in GetCaptureRegions(new Rectangle(Point.Empty, frame.Size)))
        {
            using var meter = frame.Clone(region, PixelFormat.Format32bppArgb);
            var observation = FishingMeterDetector.Analyze(meter);
            if (!observation.IsVisible || best.IsVisible && observation.Confidence <= best.Confidence)
            {
                continue;
            }

            best = observation;
            if (best.Confidence >= DecisiveConfidence)
            {
                return best;
            }
        }

        return best;
    }

    internal static Rectangle GetCaptureRegion(Rectangle bounds)
    {
        // Retain the original center-oriented region for diagnostics and callers
        // that request one region. Live capture uses the complete candidate set.
        return GetCaptureRegion(bounds, 0.536, 0.443);
    }

    internal static IReadOnlyList<Rectangle> GetCaptureRegions(Rectangle bounds)
    {
        // FiveM positions the fishing UI relative to the active camera/UI layout.
        // The supplied bright-day frames place the meter left-of-center, while the
        // recorded 2560x1440 frame puts it right-of-center. Probe those observed
        // positions with compact square captures rather than inspecting the full
        // desktop or assuming one fixed coordinate.
        var positions = new[]
        {
            (0.536, 0.443),
            (0.35, 0.53),
            (0.44, 0.50),
            (0.50, 0.50),
            (0.666, 0.55),
        };
        return positions
            .Select(position => GetCaptureRegion(bounds, position.Item1, position.Item2))
            .Distinct()
            .ToArray();
    }

    private static Rectangle GetCaptureRegion(Rectangle bounds, double horizontal, double vertical)
    {
        // The supplied 1080p recording puts the black meter disk at roughly
        // 100 px across. A 24% capture square makes that disk approximately
        // two fifths of the crop, which matches the ring sampler's radius
        // range. The former 37% crop made the disk too small for its expected radius
        // and caused intermittent losses during the bright daytime pulse UI.
        var size = Math.Clamp((int)Math.Round(bounds.Height * 0.24), 240, 400);
        size = Math.Min(size, Math.Min(bounds.Width, bounds.Height));
        var centerX = bounds.Left + (int)Math.Round(bounds.Width * horizontal);
        var centerY = bounds.Top + (int)Math.Round(bounds.Height * vertical);
        var left = Math.Clamp(centerX - size / 2, bounds.Left, bounds.Right - size);
        var top = Math.Clamp(centerY - size / 2, bounds.Top, bounds.Bottom - size);
        return new Rectangle(left, top, size, size);
    }

    internal static FishingMeterObservation Observe(IFrameSource frameSource, WindowTargetSettings target, out FrameSourceStatus status)
    {
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            status = new FrameSourceStatus(FrameSourceState.TargetUnavailable, frameSource.Name, detail, TimeSpan.MaxValue, 0);
            return FishingMeterObservation.Missing;
        }

        // The pulse UI animates while the five candidate positions are being
        // inspected. Capturing them one at a time can therefore combine pixels
        // from different UI states and lose a meter that is visibly present.
        // Capture one coherent FiveM frame, then inspect every calibrated
        // candidate region inside that exact frame.
        var wholeTarget = new Rectangle(Point.Empty, resolved.Bounds.Size);
        if (!frameSource.TryCapture(target, wholeTarget, out var frame, out status) || frame is null)
        {
            return FishingMeterObservation.Missing;
        }

        using (frame)
        {
            return AnalyzeFrame(frame.Bitmap);
        }
    }

}

internal sealed class FishingDiagnosticLog : IDisposable
{
    private readonly StreamWriter writer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly string directory;

    internal FishingDiagnosticLog()
    {
        directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkflowLooper",
            "diagnostics");
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

    internal void CaptureFirstLoss(IFrameSource frameSource, WindowTargetSettings target)
    {
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            FishingLoopDiagnosticLog.Write("meter_loss_capture_failed", detail);
            return;
        }

        var region = new Rectangle(Point.Empty, resolved.Bounds.Size);
        if (!frameSource.TryCapture(target, region, out var frame, out var status) || frame is null)
        {
            FishingLoopDiagnosticLog.Write("meter_loss_capture_failed", status.Detail);
            return;
        }

        using (frame)
        {
            var fileName = $"meter-loss-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.png";
            var path = Path.Combine(directory, fileName);
            frame.Bitmap.Save(path, ImageFormat.Png);
            FishingLoopDiagnosticLog.Write("meter_loss_capture", $"file={fileName};capture_ms={status.CaptureMilliseconds:F1}");
        }
    }

    public void Dispose() => writer.Dispose();
}

internal static class FishingLoopDiagnosticLog
{
    private static readonly object Sync = new();

    internal static void Write(string eventName, string detail = "")
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkflowLooper",
            "diagnostics");
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
