using System.Diagnostics;
using System.Drawing.Imaging;

namespace WorkflowLooper;

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
    double CaptureMilliseconds);

internal sealed class FrameLease : IDisposable
{
    internal FrameLease(Bitmap bitmap, Rectangle targetBounds, FrameSourceStatus status)
    {
        Bitmap = bitmap;
        TargetBounds = targetBounds;
        Status = status;
    }

    internal Bitmap Bitmap { get; }
    internal Rectangle TargetBounds { get; }
    internal FrameSourceStatus Status { get; }

    public void Dispose() => Bitmap.Dispose();
}

internal interface IFrameSource : IDisposable
{
    string Name { get; }
    bool TryCapture(WindowTargetSettings target, Rectangle relativeRegion, out FrameLease? frame, out FrameSourceStatus status);
}

internal static class FrameSourceFactory
{
    internal static IFrameSource Create() => new GdiFrameSource();
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
            frame = new FrameLease(bitmap, resolved.Bounds, status);
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
