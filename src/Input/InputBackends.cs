using System.ComponentModel;

namespace WorkflowLooper;

internal sealed record InputCapability(bool Ready, string Backend, string Detail, bool SupportsCoveredWindow);

internal interface IInputBackend
{
    string Name { get; }
    bool SupportsCoveredWindow { get; }
    InputCapability Probe(WindowTargetSettings target);
    void SendKey(WindowTargetSettings target, Keys key, bool up);
    void SendLeftButton(WindowTargetSettings target, bool up);
}

internal sealed class ForegroundInputBackend : IInputBackend
{
    public string Name => "Physical scan-code input";
    public bool SupportsCoveredWindow => false;

    public InputCapability Probe(WindowTargetSettings target) => WindowTargetService.IsTargetForeground(target)
        ? new(true, Name, "FiveM is foreground and physical input is ready.", false)
        : new(false, Name, "Physical input requires FiveM to be foreground.", false);

    public void SendKey(WindowTargetSettings target, Keys key, bool up)
    {
        EnsureForeground(target);
        InputSender.SendVirtualKey(key, up);
    }

    public void SendLeftButton(WindowTargetSettings target, bool up)
    {
        EnsureForeground(target);
        InputSender.SendLeftButton(up);
    }

    private static void EnsureForeground(WindowTargetSettings target)
    {
        if (!WindowTargetService.IsTargetForeground(target))
        {
            throw new InvalidOperationException("Physical input requires FiveM to be foreground.");
        }
    }
}

internal sealed class ApplicationMessageInputBackend : IInputBackend
{
    public string Name => "Application-addressed input";
    public bool SupportsCoveredWindow => true;

    public InputCapability Probe(WindowTargetSettings target) => WindowTargetService.TryGetHandle(target, out _, out var detail)
        ? new(true, Name, "Target window accepts application-addressed messages; live game acceptance will be monitored.", true)
        : new(false, Name, detail, true);

    public void SendKey(WindowTargetSettings target, Keys key, bool up)
    {
        var window = Resolve(target);
        var scanCode = NativeMethods.MapVirtualKey((uint)key, NativeMethods.MapvkVkToVsc);
        var lParam = 1L | ((long)scanCode << 16);
        if (up)
        {
            lParam |= 1L << 30;
            lParam |= 1L << 31;
        }

        Post(window, (uint)(up ? NativeMethods.WmKeyup : NativeMethods.WmKeydown), (IntPtr)(int)key, (IntPtr)lParam);
    }

    public void SendLeftButton(WindowTargetSettings target, bool up)
    {
        var window = Resolve(target);
        Post(window, (uint)(up ? NativeMethods.WmLbuttonup : NativeMethods.WmLbuttondown),
            up ? IntPtr.Zero : (IntPtr)NativeMethods.MkLbutton, IntPtr.Zero);
    }

    private static IntPtr Resolve(WindowTargetSettings target)
    {
        if (!WindowTargetService.TryGetHandle(target, out var window, out var detail))
        {
            throw new InvalidOperationException(detail);
        }

        return window;
    }

    private static void Post(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!NativeMethods.PostMessage(window, message, wParam, lParam))
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"Windows rejected application-addressed input 0x{message:X}.");
        }
    }
}

internal sealed class TargetInputRouter
{
    private readonly ForegroundInputBackend foreground = new();
    private readonly ApplicationMessageInputBackend application = new();
    private InputDeliveryMode mode;

    internal TargetInputRouter(InputDeliveryMode mode) => this.mode = mode;

    internal InputCapability Probe(WindowTargetSettings target)
    {
        var selected = Select(target);
        return selected.Probe(target);
    }

    internal async Task<InputCapability> PrepareAsync(WindowTargetSettings target, CancellationToken token)
    {
        if (mode == InputDeliveryMode.Application)
        {
            return application.Probe(target);
        }

        if (!WindowTargetService.IsTargetForeground(target) && !await WindowTargetService.TryActivateAsync(target, token))
        {
            return new InputCapability(false, foreground.Name,
                "Windows could not activate FiveM for physical scan-code input. Click FiveM once, then arm again.", false);
        }

        return foreground.Probe(target);
    }

    internal void SendKey(WindowTargetSettings target, Keys key, bool up) => Select(target).SendKey(target, key, up);
    internal void SendLeftButton(WindowTargetSettings target, bool up) => Select(target).SendLeftButton(target, up);

    private IInputBackend Select(WindowTargetSettings target) => mode switch
    {
        InputDeliveryMode.Foreground => foreground,
        InputDeliveryMode.Application => application,
        _ => foreground,
    };
}
