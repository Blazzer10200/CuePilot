using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace CuePilot;

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

    internal sealed record FiveMWindowTarget(
        int ProcessId,
        string ProcessName,
        string WindowTitle,
        Rectangle Bounds,
        bool IsForeground,
        bool IsMinimized)
    {
        internal WindowTargetSettings ToSettings() => new()
        {
            ProcessId = ProcessId,
            ProcessName = ProcessName,
            WindowTitle = WindowTitle,
        };
    }

    internal static IReadOnlyList<FiveMWindowTarget> FindFiveMTargets()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var byProcess = new Dictionary<int, FiveMWindowTarget>();

        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) || !TryGetCaptureBounds(window, out var bounds))
            {
                return true;
            }

            if (bounds.Width < 320 || bounds.Height < 240)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var nativeProcessId);
            if (nativeProcessId == 0)
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById((int)nativeProcessId);
                if (!process.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var title = GetTitle(window);
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                var candidate = new FiveMWindowTarget(
                    (int)nativeProcessId,
                    process.ProcessName,
                    title,
                    bounds,
                    window == foreground,
                    NativeMethods.IsIconic(window));
                if (!byProcess.TryGetValue(candidate.ProcessId, out var existing) || Prefer(candidate, existing))
                {
                    byProcess[candidate.ProcessId] = candidate;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // The process can exit between window enumeration and inspection.
            }

            return true;
        }, IntPtr.Zero);

        return byProcess.Values
            .OrderByDescending(candidate => candidate.IsForeground)
            .ThenBy(candidate => candidate.IsMinimized)
            .ThenBy(candidate => candidate.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.ProcessId)
            .ToArray();
    }

    internal static bool IsFiveMTarget(WindowTargetSettings? target) =>
        target is not null
        && !string.IsNullOrWhiteSpace(target.ProcessName)
        && target.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase);

    internal static bool TryResolve(WindowTargetSettings target, out ResolvedWindowTarget resolved, out string detail)
    {
        if (!target.IsConfigured)
        {
            resolved = default!;
            detail = "No target application is configured.";
            return false;
        }

        IntPtr processMatch = IntPtr.Zero;
        IntPtr titleMatch = IntPtr.Zero;
        IntPtr fallbackMatch = IntPtr.Zero;
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) || !TryGetCaptureBounds(window, out var candidateBounds))
            {
                return true;
            }

            if (candidateBounds.Width < 320 || candidateBounds.Height < 240)
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
                if (fallbackMatch == IntPtr.Zero)
                {
                    fallbackMatch = window;
                }

                if (!string.IsNullOrWhiteSpace(target.WindowTitle)
                    && title.Equals(target.WindowTitle, StringComparison.OrdinalIgnoreCase))
                {
                    titleMatch = window;
                }

                if (target.ProcessId > 0 && candidateProcessId == target.ProcessId)
                {
                    processMatch = window;
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                return true;
            }
        }, IntPtr.Zero);

        var match = processMatch != IntPtr.Zero
            ? processMatch
            : titleMatch != IntPtr.Zero
                ? titleMatch
                : fallbackMatch;

        if (match == IntPtr.Zero || !NativeMethods.IsWindow(match) || !TryGetCaptureBounds(match, out var bounds))
        {
            resolved = default!;
            detail = $"{target.ProcessName} is not running or has no captureable window.";
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(match, out var processId);
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

    private static bool Prefer(FiveMWindowTarget candidate, FiveMWindowTarget existing)
    {
        if (candidate.IsForeground != existing.IsForeground)
        {
            return candidate.IsForeground;
        }

        if (candidate.IsMinimized != existing.IsMinimized)
        {
            return !candidate.IsMinimized;
        }

        return candidate.Bounds.Width * candidate.Bounds.Height > existing.Bounds.Width * existing.Bounds.Height;
    }

    private static bool TryGetCaptureBounds(IntPtr window, out Rectangle bounds)
    {
        if (NativeMethods.GetClientRect(window, out var client)
            && client.Right > client.Left
            && client.Bottom > client.Top)
        {
            var origin = new NativeMethods.CursorPoint { X = client.Left, Y = client.Top };
            if (NativeMethods.ClientToScreen(window, ref origin))
            {
                bounds = new Rectangle(
                    origin.X,
                    origin.Y,
                    client.Right - client.Left,
                    client.Bottom - client.Top);
                return true;
            }
        }

        if (NativeMethods.GetWindowRect(window, out var outer))
        {
            bounds = Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        bounds = Rectangle.Empty;
        return false;
    }

    private static string GetTitle(IntPtr window)
    {
        var builder = new StringBuilder(512);
        NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }
}
