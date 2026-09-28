using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using Box = Vortice.Mathematics.Box;

namespace CuePilot;

internal sealed class DxgiFrameSource(TimeSpan? gpuWaitLimit = null) : IFrameSource
{
    private const uint AcquireTimeoutMilliseconds = 250;
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Texture2D? staging;
    private Size stagingSize;
    private Format stagingFormat;
    private Rectangle outputBounds;
    private bool gpuPriorityRequested;
    private bool gpuPriorityRaised;

    public string Name => "DXGI Desktop Duplication";

    // At default priority each readback queues behind a GPU-bound game's frames: live
    // Pickpocket capture went from ~3.5 ms median / 25 ms worst frame age with the raise
    // to 8.5 ms median / 62 ms without it, and 23 frames missed the 40 ms timing gate
    // (2026-09-28). Keeping the raise on for whole runs made the game and system hitch,
    // so callers request it only for the few seconds that need it.
    public void SetGpuPriority(bool raised)
    {
        if (gpuPriorityRequested == raised)
        {
            return;
        }

        gpuPriorityRequested = raised;
        ApplyGpuPriority();
    }

    // Best-effort: HIGH needs elevation (CuePilot runs as admin). Without it capture
    // still works, only slower under load, which the capture detail and timings show.
    // Thread priority belongs to the device; the scheduling class belongs to the process
    // and outlives the device, so it is always set back explicitly.
    private void ApplyGpuPriority()
    {
        var threadRaised = false;
        if (device is not null)
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            threadRaised = dxgiDevice.SetGPUThreadPriority(gpuPriorityRequested ? 7 : 0).Success && gpuPriorityRequested;
        }

        using var process = Process.GetCurrentProcess();
        var scheduling = NativeMethods.D3DKMTSetProcessSchedulingPriorityClass(process.Handle,
            gpuPriorityRequested ? NativeMethods.GpuSchedulingPriorityHigh : NativeMethods.GpuSchedulingPriorityNormal);
        gpuPriorityRaised = threadRaised && scheduling == 0;
    }

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

        try
        {
            EnsureDuplication(region);
            var acquireTimeout = gpuWaitLimit is { } limit
                ? (uint)Math.Min(AcquireTimeoutMilliseconds, limit.TotalMilliseconds)
                : AcquireTimeoutMilliseconds;
            var result = duplication!.AcquireNextFrame(acquireTimeout, out var frameInfo, out var desktopResource);
            if (result.Failure)
            {
                if (result.Code == Vortice.DXGI.ResultCode.AccessLost.Code)
                {
                    Reset();
                }

                // Under a saturated GPU the duplication often delivers no frame at all
                // (2026-09-26: 14 acquire timeouts in one fight), which is the same
                // condition as a late readback for a caller that can use another source.
                status = new FrameSourceStatus(FrameSourceState.CaptureFailed, Name,
                    $"Desktop duplication could not acquire a frame ({result.Description}).",
                    TimeSpan.MaxValue, clock.Elapsed.TotalMilliseconds,
                    GpuBusy: gpuWaitLimit is not null && result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code);
                return false;
            }

            using (desktopResource)
            {
                try
                {
                    // Copy only the requested region on the GPU. A full-desktop copy queues
                    // behind a GPU-bound game and stretched each sample past 150 ms.
                    using var source = desktopResource.QueryInterface<ID3D11Texture2D>();
                    var staging = EnsureStagingTexture(source.Description, region.Size);
                    var sourceX = region.Left - outputBounds.Left;
                    var sourceY = region.Top - outputBounds.Top;
                    context!.CopySubresourceRegion(staging, 0, 0, 0, 0, source, 0,
                        new Box(sourceX, sourceY, 0, sourceX + region.Width, sourceY + region.Height, 1));
                    if (!TryMap(staging, out var mapped))
                    {
                        status = new FrameSourceStatus(FrameSourceState.CaptureFailed, Name,
                            $"The GPU did not return the frame within {gpuWaitLimit!.Value.TotalMilliseconds:0} ms; the game is saturating it.",
                            TimeSpan.MaxValue, clock.Elapsed.TotalMilliseconds, GpuBusy: true);
                        return false;
                    }
                    try
                    {
                        var bitmap = CopyMapped(mapped, region.Size);
                        var frameAge = CalculateFrameAge(frameInfo.LastPresentTime);
                        status = new FrameSourceStatus(FrameSourceState.Ready, Name,
                            gpuPriorityRaised ? "Desktop duplication frame ready; GPU priority raised." : "Desktop duplication frame ready.",
                            frameAge, clock.Elapsed.TotalMilliseconds,
                            frameInfo.AccumulatedFrames,
                            frameInfo.LastPresentTime > 0 ? frameInfo.LastPresentTime * 1000d / Stopwatch.Frequency : null);
                        frame = new FrameLease(bitmap, status);
                        return true;
                    }
                    finally
                    {
                        context.Unmap(staging, 0);
                    }
                }
                finally
                {
                    duplication.ReleaseFrame();
                }
            }
        }
        catch (Exception exception)
        {
            Reset();
            status = new FrameSourceStatus(FrameSourceState.CaptureFailed, Name, exception.Message,
                TimeSpan.MaxValue, clock.Elapsed.TotalMilliseconds);
            return false;
        }
    }

    // A blocking Map waits for the copy to reach the front of the GPU queue, which under
    // a saturated GPU has taken over 10 s. With a limit, poll without blocking and give
    // up so the caller can use another source. The abandoned copy just completes later.
    private bool TryMap(ID3D11Texture2D staging, out MappedSubresource mapped)
    {
        if (gpuWaitLimit is not { } limit)
        {
            context!.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out mapped).CheckError();
            return true;
        }

        context!.Flush();
        var deadline = Stopwatch.GetTimestamp() + (long)(limit.TotalSeconds * Stopwatch.Frequency);
        var spinner = new SpinWait();
        while (true)
        {
            var result = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out mapped);
            if (result.Code != Vortice.DXGI.ResultCode.WasStillDrawing.Code)
            {
                result.CheckError();
                return true;
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                return false;
            }

            spinner.SpinOnce(sleep1Threshold: -1);
        }
    }

    private static TimeSpan CalculateFrameAge(long lastPresentTime)
    {
        if (lastPresentTime <= 0)
        {
            // DXGI reports zero when the acquired frame contains no new desktop
            // image. Reusing it is fine for display, but not for motion timing.
            return TimeSpan.MaxValue;
        }
        var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - lastPresentTime);
        return TimeSpan.FromSeconds(elapsedTicks / (double)Stopwatch.Frequency);
    }

    private void EnsureDuplication(Rectangle region)
    {
        if (duplication is not null && outputBounds.Contains(region))
        {
            return;
        }

        Reset();
        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        };
        D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out device,
            out context).CheckError();

        // A recreated device starts at default thread priority.
        if (gpuPriorityRequested)
        {
            ApplyGpuPriority();
        }

        using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
        dxgiDevice.GetAdapter(out var adapter).CheckError();
        using (adapter)
        {
            for (uint index = 0; ; index++)
            {
                var enumResult = adapter.EnumOutputs(index, out var output);
                if (enumResult.Failure)
                {
                    break;
                }

                using (output)
                {
                    var coordinates = output.Description.DesktopCoordinates;
                    var candidate = Rectangle.FromLTRB(
                        coordinates.Left,
                        coordinates.Top,
                        coordinates.Right,
                        coordinates.Bottom);
                    if (!candidate.Contains(region))
                    {
                        continue;
                    }

                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    duplication = output1.DuplicateOutput(device);
                    outputBounds = candidate;
                    return;
                }
            }
        }

        throw new InvalidOperationException("The target capture region is not fully inside a desktop output.");
    }

    private ID3D11Texture2D EnsureStagingTexture(Texture2DDescription source, Size size)
    {
        if (staging is not null && stagingSize == size && stagingFormat == source.Format)
        {
            return staging;
        }

        staging?.Dispose();
        staging = device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)size.Width,
            Height = (uint)size.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = source.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
        stagingSize = size;
        stagingFormat = source.Format;
        return staging;
    }

    private static unsafe Bitmap CopyMapped(MappedSubresource mapped, Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        var bitmapData = bitmap.LockBits(
            new Rectangle(Point.Empty, size),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        var rowBytes = size.Width * 4L;
        try
        {
            for (var y = 0; y < size.Height; y++)
            {
                Buffer.MemoryCopy(
                    (byte*)mapped.DataPointer + (long)y * mapped.RowPitch,
                    (byte*)bitmapData.Scan0 + (long)y * bitmapData.Stride,
                    rowBytes,
                    rowBytes);
            }
        }
        catch
        {
            bitmap.UnlockBits(bitmapData);
            bitmap.Dispose();
            throw;
        }

        bitmap.UnlockBits(bitmapData);
        return bitmap;
    }

    private void Reset()
    {
        staging?.Dispose();
        staging = null;
        stagingSize = Size.Empty;
        duplication?.Dispose();
        duplication = null;
        context?.Dispose();
        context = null;
        device?.Dispose();
        device = null;
        outputBounds = Rectangle.Empty;
    }

    public void Dispose()
    {
        SetGpuPriority(false);
        Reset();
    }
}
