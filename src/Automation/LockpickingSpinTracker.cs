using System.Diagnostics;
using System.Drawing;

namespace CuePilot;

internal sealed record LockpickingSpinTelemetry(
    bool CursorVisible,
    double CursorX,
    double CursorY,
    double AngleDegrees,
    double RadiusRatio,
    double AngularVelocityDegreesPerSecond,
    double ClockwiseTravelDegrees,
    double ElapsedMilliseconds,
    int CapturedFrames = 0);

internal sealed class LockpickingSpinTracker
{
    private bool tracking;
    private long startedAt;
    private long previousTimestamp;
    private double previousAngle;
    private double filteredVelocity;
    private double clockwiseTravel;

    internal LockpickingSpinTelemetry? Track(
        LockpickingObservation observation,
        bool cursorVisible,
        NativeMethods.CursorPoint cursor,
        Rectangle windowBounds,
        long timestamp)
    {
        if (observation.State != LockpickingVisualState.Spin || observation.HudRadius <= 0)
        {
            Reset();
            return null;
        }

        if (!tracking)
        {
            tracking = true;
            startedAt = timestamp;
        }

        var elapsed = Math.Max(0, (timestamp - startedAt) * 1000d / Stopwatch.Frequency);
        if (!cursorVisible || windowBounds.Width <= 0 || windowBounds.Height <= 0)
        {
            return new LockpickingSpinTelemetry(false, 0, 0, 0, 0, 0, clockwiseTravel, elapsed);
        }

        var localX = cursor.X - windowBounds.Left;
        var localY = cursor.Y - windowBounds.Top;
        var hudX = observation.HudCenterX * windowBounds.Width;
        var hudY = observation.HudCenterY * windowBounds.Height;
        var hudRadius = observation.HudRadius * Math.Min(windowBounds.Width, windowBounds.Height);
        var deltaX = localX - hudX;
        var deltaY = localY - hudY;
        var angle = NormalizeDegrees(Math.Atan2(deltaY, deltaX) * 180d / Math.PI);
        var radiusRatio = hudRadius > 0 ? Math.Sqrt(deltaX * deltaX + deltaY * deltaY) / hudRadius : 0;

        if (previousTimestamp > 0 && timestamp > previousTimestamp)
        {
            var angleDelta = ShortestAngleDelta(previousAngle, angle);
            var seconds = (timestamp - previousTimestamp) / (double)Stopwatch.Frequency;
            var rawVelocity = angleDelta / seconds;
            filteredVelocity = filteredVelocity == 0 ? rawVelocity : filteredVelocity * 0.35 + rawVelocity * 0.65;
            if (angleDelta > 0)
            {
                clockwiseTravel += angleDelta;
            }
        }

        previousTimestamp = timestamp;
        previousAngle = angle;
        return new LockpickingSpinTelemetry(
            true,
            Math.Clamp(localX / (double)windowBounds.Width, 0, 1),
            Math.Clamp(localY / (double)windowBounds.Height, 0, 1),
            angle,
            radiusRatio,
            filteredVelocity,
            clockwiseTravel,
            elapsed);
    }

    internal void Reset()
    {
        tracking = false;
        startedAt = 0;
        previousTimestamp = 0;
        previousAngle = 0;
        filteredVelocity = 0;
        clockwiseTravel = 0;
    }

    private static double NormalizeDegrees(double value) => (value % 360 + 360) % 360;

    private static double ShortestAngleDelta(double from, double to) => (to - from + 540) % 360 - 180;
}
