using System.Diagnostics;
using System.Text;

namespace WorkflowLooper;

internal static class WindowTargetService
{
    internal sealed record ResolvedWindowTarget(
        IntPtr Handle,
        int ProcessId,
        string ProcessName,
        string WindowTitle,
        Rectangle Bounds,
        bool IsForeground,
        bool IsMinimized);

    internal static WindowTargetSettings CaptureForeground()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows did not report a foreground application.");
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            throw new InvalidOperationException("The foreground application's process could not be identified.");
        }

        using var process = Process.GetProcessById((int)processId);
        return new WindowTargetSettings
        {
            ProcessName = process.ProcessName,
            WindowTitle = GetTitle(window),
        };
    }

    internal static bool TryResolve(WindowTargetSettings target, out ResolvedWindowTarget resolved, out string detail)
    {
        if (!target.IsConfigured)
        {
            resolved = default!;
            detail = "No target application is configured.";
            return false;
        }

        IntPtr match = IntPtr.Zero;
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) || !NativeMethods.GetWindowRect(window, out var candidateRect))
            {
                return true;
            }

            var width = candidateRect.Right - candidateRect.Left;
            var height = candidateRect.Bottom - candidateRect.Top;
            if (width < 320 || height < 240)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var candidateProcessId);
            try
            {
                using var process = Process.GetProcessById((int)candidateProcessId);
                if (!process.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var title = GetTitle(window);
                if (match == IntPtr.Zero || (!string.IsNullOrWhiteSpace(target.WindowTitle)
                    && title.Equals(target.WindowTitle, StringComparison.OrdinalIgnoreCase)))
                {
                    match = window;
                }

                return match == IntPtr.Zero || string.IsNullOrWhiteSpace(target.WindowTitle)
                    || !title.Equals(target.WindowTitle, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return true;
            }
        }, IntPtr.Zero);

        if (match == IntPtr.Zero || !NativeMethods.IsWindow(match) || !NativeMethods.GetWindowRect(match, out var rectangle))
        {
            resolved = default!;
            detail = $"{target.ProcessName} is not running or has no captureable window.";
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(match, out var processId);
        var bounds = Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        resolved = new ResolvedWindowTarget(
            match,
            (int)processId,
            target.ProcessName,
            GetTitle(match),
            bounds,
            match == NativeMethods.GetForegroundWindow(),
            NativeMethods.IsIconic(match));
        detail = resolved.IsMinimized
            ? $"{target.ProcessName} is minimized."
            : $"Resolved {target.ProcessName} window 0x{match.ToInt64():X}.";
        return bounds.Width > 0 && bounds.Height > 0;
    }

    internal static bool IsTargetForeground(WindowTargetSettings target) =>
        TryResolve(target, out var resolved, out _) && resolved.IsForeground;

    internal static bool TryGetHandle(WindowTargetSettings target, out IntPtr handle, out string detail)
    {
        if (TryResolve(target, out var resolved, out detail))
        {
            handle = resolved.Handle;
            return true;
        }

        handle = IntPtr.Zero;
        return false;
    }

    internal static async Task<bool> TryActivateAsync(WindowTargetSettings target, CancellationToken token)
    {
        if (!TryResolve(target, out var resolved, out _))
        {
            return false;
        }

        if (resolved.IsMinimized)
        {
            NativeMethods.ShowWindow(resolved.Handle, NativeMethods.SwRestore);
        }

        NativeMethods.SetForegroundWindow(resolved.Handle);
        var deadline = DateTime.UtcNow.AddMilliseconds(1_500);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (IsTargetForeground(target))
            {
                return true;
            }

            await Task.Delay(50, token);
        }

        return IsTargetForeground(target);
    }

    private static string GetTitle(IntPtr window)
    {
        var builder = new StringBuilder(512);
        NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }
}
