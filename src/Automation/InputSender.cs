using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CuePilot;

internal sealed class InputReleaseSafety
{
    private readonly Action<InputKey, bool> sendKey;
    private readonly Action<bool> sendLeftButton;

    internal InputReleaseSafety()
        : this(InputSender.SendVirtualKey, InputSender.SendLeftButton)
    {
    }

    internal InputReleaseSafety(Action<InputKey, bool> sendKey, Action<bool> sendLeftButton)
    {
        this.sendKey = sendKey;
        this.sendLeftButton = sendLeftButton;
    }

    internal bool ReleaseKey(InputKey key) => TryRelease(() => sendKey(key, true));

    internal bool ReleaseLeftButton() => TryRelease(() => sendLeftButton(true));

    internal bool ReleaseAll()
    {
        var leftReleased = ReleaseLeftButton();
        var keyReleased = ReleaseKey(InputKey.E);
        return leftReleased && keyReleased;
    }

    private static bool TryRelease(Action release)
    {
        try
        {
            release();
            return true;
        }
        catch
        {
            // Cleanup must continue through every held-input release even when
            // Windows rejects one individual SendInput call.
            return false;
        }
    }
}

/// <summary>
/// Serializes run stop with every input-down transition and remembers only the
/// input CuePilot actually owns. Once stop closes the gate, a late detector
/// result cannot begin another key press or mouse hold, and an idle stop emits
/// no synthetic events into an unrelated FiveM NUI.
/// </summary>
internal sealed class AutomationInputGate
{
    private readonly object sync = new();
    private readonly InputReleaseSafety releaseSafety;
    private readonly HashSet<InputKey> heldKeys = [];
    private bool acceptingInput;
    private bool leftButtonHeld;

    internal AutomationInputGate(InputReleaseSafety? releaseSafety = null)
    {
        this.releaseSafety = releaseSafety ?? new InputReleaseSafety();
    }

    internal void BeginRun()
    {
        lock (sync)
        {
            if (leftButtonHeld || heldKeys.Count > 0)
            {
                throw new InvalidOperationException("CuePilot still owns input from the previous run.");
            }

            acceptingInput = true;
        }
    }

    internal void SendKeyDown(InputKey key, CancellationToken token, Action send)
    {
        ArgumentNullException.ThrowIfNull(send);
        lock (sync)
        {
            ThrowIfClosed(token);
            send();
            heldKeys.Add(key);
        }
    }

    internal void SendLeftButtonDown(CancellationToken token, Action send)
    {
        ArgumentNullException.ThrowIfNull(send);
        lock (sync)
        {
            ThrowIfClosed(token);
            send();
            leftButtonHeld = true;
        }
    }

    internal bool ReleaseKey(InputKey key)
    {
        lock (sync)
        {
            if (!heldKeys.Contains(key))
            {
                return true;
            }

            var released = releaseSafety.ReleaseKey(key);
            if (released)
            {
                heldKeys.Remove(key);
            }
            return released;
        }
    }

    internal bool ReleaseLeftButton()
    {
        lock (sync)
        {
            if (!leftButtonHeld)
            {
                return true;
            }

            var released = releaseSafety.ReleaseLeftButton();
            if (released)
            {
                leftButtonHeld = false;
            }
            return released;
        }
    }

    internal bool StopAndReleaseOwnedInput()
    {
        lock (sync)
        {
            acceptingInput = false;
            var released = true;
            if (leftButtonHeld)
            {
                var leftReleased = releaseSafety.ReleaseLeftButton();
                if (leftReleased)
                {
                    leftButtonHeld = false;
                }
                released &= leftReleased;
            }

            foreach (var key in heldKeys.ToArray())
            {
                var keyReleased = releaseSafety.ReleaseKey(key);
                if (keyReleased)
                {
                    heldKeys.Remove(key);
                }
                released &= keyReleased;
            }
            return released;
        }
    }

    private void ThrowIfClosed(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!acceptingInput)
        {
            throw new OperationCanceledException("CuePilot input is stopping; no new input was sent.", token);
        }
    }
}

internal static class InputSender
{
    internal static void ReleaseAll()
    {
        new InputReleaseSafety().ReleaseAll();
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
