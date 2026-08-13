using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WorkflowLooper;

internal static class InputSender
{
    internal static void ReleaseAll()
    {
        try { SendLeftButton(true); } catch { }
        try { SendVirtualKey(Keys.E, true); } catch { }
    }

    internal static void SendLeftButton(bool up)
    {
        Send(new NativeMethods.Input
        {
            Type = NativeMethods.InputMouse,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput
                {
                    Flags = up ? NativeMethods.MouseeventfLeftup : NativeMethods.MouseeventfLeftdown,
                },
            },
        });
    }

    internal static void SendVirtualKey(Keys key, bool up)
    {
        Send(CreateScanCodeInput(key, up));
    }

    internal static NativeMethods.Input CreateScanCodeInput(Keys key, bool up)
    {
        var scanCode = NativeMethods.MapVirtualKey((uint)key, NativeMethods.MapvkVkToVsc);
        if (scanCode == 0)
        {
            throw new InvalidOperationException($"No keyboard scan code is available for {key}.");
        }

        return new NativeMethods.Input
        {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    ScanCode = (ushort)scanCode,
                    Flags = NativeMethods.KeyeventfScancode | (up ? NativeMethods.KeyeventfKeyup : 0),
                },
            },
        };
    }

    private static void Send(NativeMethods.Input input)
    {
        if (NativeMethods.SendInput(1, ref input, NativeMethods.InputSize) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected a simulated input event. Check target-app permissions.");
        }
    }
}
