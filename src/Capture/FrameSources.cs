using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace CuePilot;

internal enum FrameSourceState
{
    Ready,
    TargetUnavailable,
    TargetMinimized,
    CaptureFailed,
}

internal sealed record FrameSourceStatus(
    FrameSourceState State,
    string Backend,
    string Detail,
    TimeSpan FrameAge,
    double CaptureMilliseconds,
    uint AccumulatedFrames = 1);

internal sealed class FrameLease : IDisposable
{
    internal FrameLease(Bitmap bitmap, FrameSourceStatus status)
    {
        Bitmap = bitmap;
        Status = status;
    }

    internal Bitmap Bitmap { get; }
    internal FrameSourceStatus Status { get; private set; }

    internal void UpdateStatus(FrameSourceStatus status) => Status = status;

    public void Dispose() => Bitmap.Dispose();
}

internal interface IFrameSource : IDisposable
{
    string Name { get; }
    bool TryCapture(WindowTargetSettings target, Rectangle relativeRegion, out FrameLease? frame, out FrameSourceStatus status);
}

internal static class FrameSourceFactory
{
    internal static IFrameSource Create()
    {
        IFrameSource? primary = null;
        IFrameSource? fallback = null;
        try
        {
            primary = new DxgiFrameSource();
            fallback = new GdiFrameSource();
            var combined = new FallbackFrameSource(primary, fallback);
            primary = null;
            fallback = null;
            return combined;
        }
        finally
        {
            primary?.Dispose();
            fallback?.Dispose();
        }
    }
}

internal sealed class FallbackFrameSource(IFrameSource primary, IFrameSource fallback) : IFrameSource
{
    public string Name => primary.Name;

    public bool TryCapture(WindowTargetSettings target, Rectangle relativeRegion, out FrameLease? frame, out FrameSourceStatus status)
    {
        if (primary.TryCapture(target, relativeRegion, out frame, out status))
        {
            if (AcceptVisibleFrame(ref frame, ref status)) return true;
        }

        if (status.State is FrameSourceState.TargetUnavailable or FrameSourceState.TargetMinimized
            || status.Detail.Contains("no longer foreground", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var primaryFailure = status;
        if (fallback.TryCapture(target, relativeRegion, out frame, out status))
        {
            if (AcceptVisibleFrame(ref frame, ref status))
            {
                status = status with
                {
                    Detail = $"Fallback active after {primaryFailure.Backend} failed: {primaryFailure.Detail}",
                };
                frame!.UpdateStatus(status);
                return true;
            }
        }

        status = status with
        {
            Detail = $"{primaryFailure.Backend}: {primaryFailure.Detail} Fallback {status.Backend}: {status.Detail}",
        };
        return false;
    }

    private static bool AcceptVisibleFrame(ref FrameLease? frame, ref FrameSourceStatus status)
    {
        if (frame is not null && !CapturedFrameHealth.IsNearlyUniformBlack(frame.Bitmap))
        {
            return true;
        }

        frame?.Dispose();
        frame = null;
        status = status with
        {
            State = FrameSourceState.CaptureFailed,
            Detail = "Capture returned a nearly uniform black frame. Exclusive fullscreen may be blocking desktop capture; switch FiveM to Borderless Windowed and run Verify setup again.",
        };
        return false;
    }

    public void Dispose()
    {
        primary.Dispose();
        fallback.Dispose();
    }
}

internal static class CapturedFrameHealth
{
    internal static bool IsNearlyUniformBlack(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return true;

        var minimum = 255;
        var maximum = 0;
        const int columns = 17;
        const int rows = 11;
        for (var row = 0; row < rows; row++)
        {
            var y = Math.Min(bitmap.Height - 1, (row * 2 + 1) * bitmap.Height / (rows * 2));
            for (var column = 0; column < columns; column++)
            {
                var x = Math.Min(bitmap.Width - 1, (column * 2 + 1) * bitmap.Width / (columns * 2));
                var color = bitmap.GetPixel(x, y);
                var brightness = Math.Max(color.R, Math.Max(color.G, color.B));
                minimum = Math.Min(minimum, brightness);
                maximum = Math.Max(maximum, brightness);
                if (maximum > 8 || maximum - minimum > 4) return false;
            }
        }

        return maximum <= 8 && maximum - minimum <= 4;
    }
}

internal sealed class GdiFrameSource : IFrameSource
{
    public string Name => "Desktop GDI";

    public bool TryCapture(WindowTargetSettings target, Rectangle relativeRegion, out FrameLease? frame, out FrameSourceStatus status)
    {
        frame = null;
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            status = new FrameSourceStatus(FrameSourceState.TargetUnavailable, Name, detail, TimeSpan.MaxValue, 0);
            return false;
        }

        if (resolved.IsMinimized)
        {
            status = new FrameSourceStatus(FrameSourceState.TargetMinimized, Name,
                "The target is minimized.", TimeSpan.MaxValue, 0);
            return false;
        }

        if (!resolved.IsForeground)
        {
            status = new FrameSourceStatus(FrameSourceState.CaptureFailed, Name,
                "FiveM is no longer foreground. Bring it to the front before continuing.", TimeSpan.MaxValue, 0);
            return false;
        }

        var region = new Rectangle(
            resolved.Bounds.Left + relativeRegion.Left,
            resolved.Bounds.Top + relativeRegion.Top,
            relativeRegion.Width,
            relativeRegion.Height);
        var clock = Stopwatch.StartNew();
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
            status = new FrameSourceStatus(FrameSourceState.Ready, Name, "Desktop frame ready.",
                TimeSpan.Zero, clock.Elapsed.TotalMilliseconds);
            frame = new FrameLease(bitmap, status);
            return true;
        }
        catch (Exception exception)
        {
            bitmap.Dispose();
            status = new FrameSourceStatus(FrameSourceState.CaptureFailed, Name, exception.Message,
                TimeSpan.MaxValue, clock.Elapsed.TotalMilliseconds);
            return false;
        }
    }

    public void Dispose()
    {
    }
}
