namespace CuePilot;

internal sealed record InputCapability(bool Ready, string Backend, string Detail, bool SupportsCoveredWindow);

internal interface IInputBackend
{
    string Name { get; }
    bool SupportsCoveredWindow { get; }
    InputCapability Probe(WindowTargetSettings target);
    void SendKey(WindowTargetSettings target, InputKey key, bool up);
    void SendLeftButton(WindowTargetSettings target, bool up);
}

internal sealed class ForegroundInputBackend : IInputBackend
{
    public string Name => "Physical scan-code input";
    public bool SupportsCoveredWindow => false;

    public InputCapability Probe(WindowTargetSettings target) => WindowTargetService.IsTargetForeground(target)
        ? new(true, Name, "FiveM is foreground and physical input is ready.", false)
        : new(false, Name, "Physical input requires FiveM to be foreground.", false);

    public void SendKey(WindowTargetSettings target, InputKey key, bool up)
    {
        EnsureForeground(target);
        InputSender.SendVirtualKey(key, up);
    }

    public void SendLeftButton(WindowTargetSettings target, bool up)
    {
        EnsureForeground(target);
        InputSender.SendLeftButton(up);
    }

    internal void MoveCursor(WindowTargetSettings target, int screenX, int screenY)
    {
        EnsureForeground(target);
        InputSender.MoveCursorAbsolute(screenX, screenY);
    }

    private static void EnsureForeground(WindowTargetSettings target)
    {
        if (!WindowTargetService.IsTargetForeground(target))
        {
            throw new InvalidOperationException("Physical input requires FiveM to be foreground.");
        }
    }
}

internal static class ForegroundInputReadiness
{
    private const int AutomaticForegroundChecks = 100;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(100);

    internal static async Task<bool> WaitAsync(
        InputDeliveryMode mode,
        Func<bool> isForeground,
        CancellationToken token,
        int maximumChecks = AutomaticForegroundChecks,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(isForeground);
        if (isForeground())
        {
            return true;
        }
        if (mode == InputDeliveryMode.Foreground || maximumChecks <= 0)
        {
            return false;
        }

        delay ??= static (duration, cancellation) => Task.Delay(duration, cancellation);
        for (var check = 0; check < maximumChecks; check++)
        {
            token.ThrowIfCancellationRequested();
            await delay(CheckInterval, token);
            if (isForeground())
            {
                return true;
            }
        }
        return false;
    }
}

internal sealed class TargetInputRouter
{
    private readonly ForegroundInputBackend foreground = new();
    private readonly InputDeliveryMode mode;

    internal TargetInputRouter(InputDeliveryMode mode) => this.mode = mode;

    internal InputCapability Probe(WindowTargetSettings target)
    {
        return foreground.Probe(target);
    }

    internal async Task<InputCapability> PrepareAsync(WindowTargetSettings target, CancellationToken token)
    {
        var ready = await ForegroundInputReadiness.WaitAsync(
            mode,
            () => WindowTargetService.IsTargetForeground(target),
            token);
        if (!ready)
        {
            var detail = mode == InputDeliveryMode.Automatic
                ? "CuePilot did not take focus from your game. Return to FiveM within ten seconds, then start again."
                : "Physical input requires FiveM to already be foreground.";
            return new InputCapability(false, foreground.Name, detail, false);
        }

        return foreground.Probe(target);
    }

    internal void SendKey(WindowTargetSettings target, InputKey key, bool up) => foreground.SendKey(target, key, up);
    internal void SendLeftButton(WindowTargetSettings target, bool up) => foreground.SendLeftButton(target, up);
}
