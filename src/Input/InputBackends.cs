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
        if (mode == InputDeliveryMode.Foreground && !WindowTargetService.IsTargetForeground(target))
        {
            return foreground.Probe(target);
        }

        if (!WindowTargetService.IsTargetForeground(target) && !await WindowTargetService.TryActivateAsync(target, token))
        {
            return new InputCapability(false, foreground.Name,
                "Windows could not activate FiveM for physical scan-code input. Click FiveM once, then arm again.", false);
        }

        return foreground.Probe(target);
    }

    internal void SendKey(WindowTargetSettings target, InputKey key, bool up) => foreground.SendKey(target, key, up);
    internal void SendLeftButton(WindowTargetSettings target, bool up) => foreground.SendLeftButton(target, up);
}
