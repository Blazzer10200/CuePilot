using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace CuePilot;

internal sealed class DxgiFrameSource : IFrameSource
{
    private const uint AcquireTimeoutMilliseconds = 250;
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGIOutputDuplication? duplication;
    private Rectangle outputBounds;

    public string Name => "DXGI Desktop Duplication";

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
            var result = duplication!.AcquireNextFrame(AcquireTimeoutMilliseconds, out var frameInfo, out var desktopResource);
            if (result.Failure)
            {
                if (result.Code == Vortice.DXGI.ResultCode.AccessLost.Code)
                {
                    Reset();
                }

                status = new FrameSourceStatus(FrameSourceState.CaptureFailed, Name,
                    $"Desktop duplication could not acquire a frame ({result.Description}).",
                    TimeSpan.MaxValue, clock.Elapsed.TotalMilliseconds);
                return false;
            }

            using (desktopResource)
            {
                try
                {
                    using var source = desktopResource.QueryInterface<ID3D11Texture2D>();
                    using var staging = CreateStagingTexture(source.Description);
                    context!.CopyResource(staging, source);
                    var mapResult = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped);
                    mapResult.CheckError();
                    try
                    {
                        var bitmap = CopyRegion(mapped, region);
                        var frameAge = CalculateFrameAge(frameInfo.LastPresentTime);
                        status = new FrameSourceStatus(FrameSourceState.Ready, Name,
                            "Desktop duplication frame ready.", frameAge, clock.Elapsed.TotalMilliseconds,
                            frameInfo.AccumulatedFrames);
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

    private ID3D11Texture2D CreateStagingTexture(Texture2DDescription source)
    {
        var description = source;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        return device!.CreateTexture2D(description);
    }

    private Bitmap CopyRegion(MappedSubresource mapped, Rectangle region)
    {
        var sourceX = region.Left - outputBounds.Left;
        var sourceY = region.Top - outputBounds.Top;
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        var bitmapData = bitmap.LockBits(
            new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        var row = new byte[region.Width * 4];
        try
        {
            for (var y = 0; y < region.Height; y++)
            {
                var sourceRow = IntPtr.Add(mapped.DataPointer,
                    checked((sourceY + y) * (int)mapped.RowPitch + sourceX * 4));
                Marshal.Copy(sourceRow, row, 0, row.Length);
                Marshal.Copy(row, 0, IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride), row.Length);
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
        duplication?.Dispose();
        duplication = null;
        context?.Dispose();
        context = null;
        device?.Dispose();
        device = null;
        outputBounds = Rectangle.Empty;
    }

    public void Dispose() => Reset();
}
