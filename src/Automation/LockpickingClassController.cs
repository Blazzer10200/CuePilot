using System.Diagnostics;
using System.Drawing;

namespace CuePilot;

internal sealed record LockpickingAutomationUpdate(
    string PredictedAction,
    string Detail,
    int ActionCount,
    bool SpinActive);

internal interface ILockpickingInputDriver
{
    void MoveCursor(WindowTargetSettings target, int screenX, int screenY);
    void SendLeftButton(WindowTargetSettings target, bool up);
}

internal sealed class LockpickingInputDriver : ILockpickingInputDriver
{
    private readonly ForegroundInputBackend input = new();

    public void MoveCursor(WindowTargetSettings target, int screenX, int screenY) =>
        input.MoveCursor(target, screenX, screenY);

    public void SendLeftButton(WindowTargetSettings target, bool up) =>
        input.SendLeftButton(target, up);
}

internal sealed class LockpickingClassController : IDisposable
{
    private readonly object sync = new();
    private readonly ILockpickingInputDriver input;
    private readonly WindowTargetSettings target;
    private readonly LockpickingClassProfile profile;
    private CancellationTokenSource? spinCancellation;
    private Task? spinTask;
    private string? spinFault;
    private int lastClickedTarget;
    private int spinConfirmations;
    private int actionCount;
    private bool spinActive;
    private bool spinStarted;

    internal LockpickingClassController(
        WindowTargetSettings target,
        LockpickingClassProfile profile,
        ILockpickingInputDriver? input = null)
    {
        this.target = target.Copy();
        this.profile = profile;
        this.input = input ?? new LockpickingInputDriver();
    }

    internal int ActionCount
    {
        get { lock (sync) return actionCount; }
    }

    internal bool SpinActive
    {
        get { lock (sync) return spinActive; }
    }

    internal bool SpinStarted
    {
        get { lock (sync) return spinStarted; }
    }

    internal async Task<LockpickingAutomationUpdate> HandleAsync(
        LockpickingObservation observation,
        Rectangle windowBounds,
        CancellationToken token)
    {
        ThrowIfSpinFaulted();

        var expectedTargetNumber = lastClickedTarget + 1;
        const double minimumObservationConfidence = 0.42;
        const double minimumTargetConfidence = 0.45;

        if (observation.State == LockpickingVisualState.Numbered
            && observation.Target is { Phase: LockpickingTargetPhase.Ready, Number: not null } readyTarget
            && observation.PredictedAction == "CLICK (OBSERVE ONLY)"
            && readyTarget.HasLiteralNumber
            && observation.Confidence >= minimumObservationConfidence
            && readyTarget.Confidence >= minimumTargetConfidence
            && readyTarget.Number.Value == expectedTargetNumber
            && IsInsideHud(observation, readyTarget))
        {
            StopSpin();
            var screenX = windowBounds.Left + (int)Math.Round(readyTarget.CenterX * windowBounds.Width);
            var screenY = windowBounds.Top + (int)Math.Round(readyTarget.CenterY * windowBounds.Height);
            input.MoveCursor(target, screenX, screenY);
            input.SendLeftButton(target, false);
            try
            {
                await Task.Delay(18, token);
            }
            finally
            {
                input.SendLeftButton(target, true);
            }

            lastClickedTarget = readyTarget.Number.Value;
            lock (sync) actionCount++;
            return Current(
                $"CLICKED TARGET {readyTarget.Number.Value}",
                $"Class {profile.VehicleClass} controller clicked verified target {readyTarget.Number.Value} at screen ({screenX}, {screenY}) within window {windowBounds.Left},{windowBounds.Top} {windowBounds.Width}x{windowBounds.Height}.");
        }

        if (observation.State == LockpickingVisualState.Spin)
        {
            spinConfirmations++;
            if (spinConfirmations >= 2 && !SpinActive)
            {
                StartSpin(observation, windowBounds, token);
            }

            return Current(
                SpinActive ? $"ROTATING CLASS {profile.VehicleClass}" : "VERIFY SPIN",
                SpinActive
                    ? $"Running the bounded Class {profile.VehicleClass} clockwise orbit; visual disappearance or Pause stops input."
                    : "SPIN requires one more matching frame before cursor motion begins.");
        }

        spinConfirmations = 0;
        StopSpin();
        return Current(observation.PredictedAction, observation.Reason);
    }

    internal void Stop()
    {
        StopSpin();
        try { input.SendLeftButton(target, true); } catch { }
    }

    internal static Point SpinPoint(
        Point center,
        double radius,
        double startingAngleDegrees,
        double elapsedSeconds,
        double degreesPerSecond)
    {
        var angle = (startingAngleDegrees + degreesPerSecond * elapsedSeconds) * Math.PI / 180d;
        return new Point(
            center.X + (int)Math.Round(Math.Cos(angle) * radius),
            center.Y + (int)Math.Round(Math.Sin(angle) * radius));
    }

    internal static bool IsInsideHud(
        LockpickingObservation observation,
        LockpickingTargetObservation target)
    {
        if (observation.HudRadius <= 0)
        {
            return false;
        }

        var dx = target.CenterX - observation.HudCenterX;
        var dy = target.CenterY - observation.HudCenterY;
        return Math.Sqrt(dx * dx + dy * dy) <= observation.HudRadius * 0.9;
    }

    private void StartSpin(LockpickingObservation observation, Rectangle windowBounds, CancellationToken outerToken)
    {
        StopSpin();
        var center = new Point(
            windowBounds.Left + (int)Math.Round(observation.HudCenterX * windowBounds.Width),
            windowBounds.Top + (int)Math.Round(observation.HudCenterY * windowBounds.Height));
        var hudRadius = observation.HudRadius * Math.Min(windowBounds.Width, windowBounds.Height);
        var radius = Math.Max(12, hudRadius * profile.SpinRadiusRatio);
        var startingAngle = CursorAngle(center);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        lock (sync)
        {
            spinCancellation = linked;
            spinActive = true;
            spinStarted = true;
            actionCount++;
        }

        spinTask = Task.Run(() => RunSpinAsync(center, radius, startingAngle, linked.Token), CancellationToken.None);
    }

    private async Task RunSpinAsync(Point center, double radius, double startingAngle, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            while (clock.Elapsed.TotalSeconds < profile.MaximumSpinSeconds)
            {
                token.ThrowIfCancellationRequested();
                var point = SpinPoint(
                    center,
                    radius,
                    startingAngle,
                    clock.Elapsed.TotalSeconds,
                    profile.SpinDegreesPerSecond);
                input.MoveCursor(target, point.X, point.Y);
                await Task.Delay(8, token);
            }

            lock (sync)
            {
                if (!token.IsCancellationRequested)
                {
                    spinFault = $"Class {profile.VehicleClass} SPIN exceeded the calibrated {profile.MaximumSpinSeconds:0.0} s safety limit.";
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal state transition, stop, or emergency release.
        }
        catch (Exception exception)
        {
            lock (sync) spinFault = exception.Message;
        }
        finally
        {
            lock (sync) spinActive = false;
        }
    }

    private void StopSpin()
    {
        CancellationTokenSource? cancellation;
        lock (sync)
        {
            cancellation = spinCancellation;
            spinCancellation = null;
            spinActive = false;
        }
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void ThrowIfSpinFaulted()
    {
        string? fault;
        lock (sync)
        {
            fault = spinFault;
            spinFault = null;
        }
        if (!string.IsNullOrWhiteSpace(fault))
        {
            throw new InvalidOperationException(fault);
        }
    }

    private LockpickingAutomationUpdate Current(string predictedAction, string detail) => new(
        predictedAction,
        detail,
        ActionCount,
        SpinActive);

    private static double CursorAngle(Point center)
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return -90;
        }
        return Math.Atan2(cursor.Y - center.Y, cursor.X - center.X) * 180d / Math.PI;
    }

    public void Dispose()
    {
        Stop();
        spinTask = null;
    }
}
