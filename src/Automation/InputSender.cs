using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WorkflowLooper;

internal static class InputSender
{
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
        Send(new NativeMethods.Input
        {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = (ushort)key,
                    Flags = up ? NativeMethods.KeyeventfKeyup : 0,
                },
            },
        });
    }

    private static void Send(NativeMethods.Input input)
    {
        if (NativeMethods.SendInput(1, ref input, NativeMethods.InputSize) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected a simulated input event. Check target-app permissions.");
        }
    }
}
