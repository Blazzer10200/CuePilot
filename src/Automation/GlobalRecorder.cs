using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WorkflowLooper;

internal sealed class GlobalRecorder : IDisposable
{
    private readonly NativeMethods.HookProc keyboardCallback;
    private readonly NativeMethods.HookProc mouseCallback;
    private readonly List<MacroEvent> events = [];
    private IntPtr keyboardHook;
    private IntPtr mouseHook;
    private long startTicks;
    private bool recordMouseMoves;
    private int lastMouseX = int.MinValue;
    private int lastMouseY = int.MinValue;
    private long lastMouseMoveMicroseconds;

    internal bool IsRecording { get; private set; }

    internal GlobalRecorder()
    {
        keyboardCallback = KeyboardHook;
        mouseCallback = MouseHook;
    }

    internal void Start(bool includeMouseMoves)
    {
        if (IsRecording)
        {
            return;
        }

        events.Clear();
        recordMouseMoves = includeMouseMoves;
        lastMouseX = int.MinValue;
        lastMouseY = int.MinValue;
        lastMouseMoveMicroseconds = 0;
        startTicks = Stopwatch.GetTimestamp();
        var module = NativeMethods.GetModuleHandle(null);
        keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, keyboardCallback, module, 0);
        mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, mouseCallback, module, 0);
        if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
        {
            DisposeHooks();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the global input recorder hooks.");
        }

        IsRecording = true;
    }

    internal WorkflowPattern Stop(string name, HotkeyBinding? stopHotkey)
    {
        if (!IsRecording)
        {
            throw new InvalidOperationException("Recording is not active.");
        }

        var stopOffset = CurrentOffset();
        IsRecording = false;
        DisposeHooks();

        if (stopHotkey is not null)
        {
            stopOffset = TrimTrailingHotkey(stopOffset, stopHotkey);
        }

        var bounds = SystemInformation.VirtualScreen;
        return new WorkflowPattern
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Workflow {DateTime.Now:yyyy-MM-dd HH-mm-ss}" : name.Trim(),
            RecordedAt = DateTimeOffset.Now,
            DurationMicroseconds = Math.Max(stopOffset, events.Count == 0 ? 0 : events[^1].OffsetMicroseconds),
            RecordedLeft = bounds.Left,
            RecordedTop = bounds.Top,
            RecordedWidth = bounds.Width,
            RecordedHeight = bounds.Height,
            Events = [.. events],
        };
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == NativeMethods.HcAction && IsRecording)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
            if ((data.Flags & NativeMethods.LlkhfInjected) == 0)
            {
                var message = wParam.ToInt32();
                if (message is NativeMethods.WmKeydown or NativeMethods.WmSyskeydown or NativeMethods.WmKeyup or NativeMethods.WmSyskeyup)
                {
                    events.Add(new MacroEvent
                    {
                        OffsetMicroseconds = CurrentOffset(),
                        Type = message is NativeMethods.WmKeydown or NativeMethods.WmSyskeydown ? MacroEventType.KeyDown : MacroEventType.KeyUp,
                        VirtualKey = (int)data.VirtualKey,
                        ScanCode = (int)data.ScanCode,
                        Extended = (data.Flags & NativeMethods.LlkhfExtended) != 0,
                    });
                }
            }
        }

        return NativeMethods.CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == NativeMethods.HcAction && IsRecording)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
            if ((data.Flags & NativeMethods.LlmhfInjected) == 0)
            {
                var offset = CurrentOffset();
                var message = wParam.ToInt32();
                if (message == NativeMethods.WmMousemove)
                {
                    var moved = Math.Abs(data.Point.X - lastMouseX) >= 2 || Math.Abs(data.Point.Y - lastMouseY) >= 2;
                    if (recordMouseMoves && moved && offset - lastMouseMoveMicroseconds >= 12_000)
                    {
                        AddMouseEvent(MacroEventType.MouseMove, data, 0, offset);
                        lastMouseX = data.Point.X;
                        lastMouseY = data.Point.Y;
                        lastMouseMoveMicroseconds = offset;
                    }
                }
                else
                {
                    var mapped = MapMouseMessage(message, data.MouseData);
                    if (mapped is not null)
                    {
                        AddMouseEvent(mapped.Value.Type, data, mapped.Value.Data, offset);
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private void AddMouseEvent(MacroEventType type, NativeMethods.MouseHookData data, int eventData, long offset)
    {
        events.Add(new MacroEvent
        {
            OffsetMicroseconds = offset,
            Type = type,
            X = data.Point.X,
            Y = data.Point.Y,
            Data = eventData,
        });
    }

    private static (MacroEventType Type, int Data)? MapMouseMessage(int message, uint mouseData) => message switch
    {
        NativeMethods.WmLbuttondown => (MacroEventType.MouseDown, 1),
        NativeMethods.WmLbuttonup => (MacroEventType.MouseUp, 1),
        NativeMethods.WmRbuttondown => (MacroEventType.MouseDown, 2),
        NativeMethods.WmRbuttonup => (MacroEventType.MouseUp, 2),
        NativeMethods.WmMbuttondown => (MacroEventType.MouseDown, 3),
        NativeMethods.WmMbuttonup => (MacroEventType.MouseUp, 3),
        NativeMethods.WmXbuttondown => (MacroEventType.MouseDown, 3 + (int)NativeMethods.HighWord(mouseData)),
        NativeMethods.WmXbuttonup => (MacroEventType.MouseUp, 3 + (int)NativeMethods.HighWord(mouseData)),
        NativeMethods.WmMousewheel => (MacroEventType.MouseWheel, NativeMethods.SignedHighWord(mouseData)),
        NativeMethods.WmMousehwheel => (MacroEventType.MouseHorizontalWheel, NativeMethods.SignedHighWord(mouseData)),
        _ => null,
    };

    private long TrimTrailingHotkey(long stopOffset, HotkeyBinding stopHotkey)
    {
        var hotkeyIndex = events.FindLastIndex(item =>
            item.Type == MacroEventType.KeyDown && item.VirtualKey == (int)stopHotkey.Key && stopOffset - item.OffsetMicroseconds < 1_000_000);
        if (hotkeyIndex < 0)
        {
            return stopOffset;
        }

        var cutOffset = events[hotkeyIndex].OffsetMicroseconds;
        for (var index = hotkeyIndex - 1; index >= 0; index--)
        {
            var item = events[index];
            if (cutOffset - item.OffsetMicroseconds > 700_000)
            {
                break;
            }

            if (item.Type == MacroEventType.KeyDown && IsHotkeyModifier(item.VirtualKey, stopHotkey))
            {
                cutOffset = item.OffsetMicroseconds;
            }
        }

        events.RemoveAll(item => item.OffsetMicroseconds >= cutOffset);
        return cutOffset;
    }

    private static bool IsHotkeyModifier(int virtualKey, HotkeyBinding binding) =>
        (binding.Control && virtualKey is (int)Keys.LControlKey or (int)Keys.RControlKey)
        || (binding.Shift && virtualKey is (int)Keys.LShiftKey or (int)Keys.RShiftKey)
        || (binding.Alt && virtualKey is (int)Keys.LMenu or (int)Keys.RMenu);

    private long CurrentOffset() => (long)((Stopwatch.GetTimestamp() - startTicks) * 1_000_000d / Stopwatch.Frequency);

    private void DisposeHooks()
    {
        if (keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }

        if (mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        IsRecording = false;
        DisposeHooks();
    }
}
