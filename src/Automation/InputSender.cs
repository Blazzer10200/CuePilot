using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CuePilot;

internal static class InputSender
{
    internal static void ReleaseAll()
    {
        try { SendLeftButton(true); } catch { }
        try { SendVirtualKey(InputKey.E, true); } catch { }
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

    internal static void MoveCursorAbsolute(int screenX, int screenY)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SmXvirtualscreen);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SmYvirtualscreen);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.SmCxvirtualscreen);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.SmCyvirtualscreen);
        var normalized = NormalizeAbsolute(screenX, screenY, virtualLeft, virtualTop, virtualWidth, virtualHeight);
        Send(new NativeMethods.Input
        {
            Type = NativeMethods.InputMouse,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput
                {
                    X = normalized.X,
                    Y = normalized.Y,
                    Flags = NativeMethods.MouseeventfMove
                        | NativeMethods.MouseeventfAbsolute
                        | NativeMethods.MouseeventfVirtualdesk,
                },
            },
        });
    }

    internal static Point NormalizeAbsolute(
        int screenX,
        int screenY,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight)
    {
        if (virtualWidth <= 1 || virtualHeight <= 1)
        {
            throw new InvalidOperationException("Windows did not report a usable virtual desktop for cursor input.");
        }

        var x = Math.Clamp(screenX, virtualLeft, virtualLeft + virtualWidth - 1);
        var y = Math.Clamp(screenY, virtualTop, virtualTop + virtualHeight - 1);
        return new Point(
            (int)Math.Round((x - virtualLeft) * 65535d / (virtualWidth - 1)),
            (int)Math.Round((y - virtualTop) * 65535d / (virtualHeight - 1)));
    }

    internal static void SendVirtualKey(InputKey key, bool up)
    {
        Send(CreateScanCodeInput(key, up));
    }

    internal static NativeMethods.Input CreateScanCodeInput(InputKey key, bool up)
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
