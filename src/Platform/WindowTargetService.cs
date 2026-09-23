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

            if (NativeMethods.GetWindowThreadProcessId(window, out var nativeProcessId) == 0
                || nativeProcessId == 0)
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

    // Every capture sample and every input edge resolves the target. A full enumeration
    // opens the process behind every visible window (FiveM alone runs dozens of browser
    // helpers), so a confirmed match is remembered and revalidated with cheap window
    // calls plus one process lookup, and the full scan runs only when that fails.
    private sealed record CachedMatch(string ProcessName, int ConfiguredProcessId, string ConfiguredTitle, IntPtr Handle, uint ProcessId);

    private static volatile CachedMatch? cachedMatch;

    internal static bool TryResolve(WindowTargetSettings target, out ResolvedWindowTarget resolved, out string detail)
    {
        if (!target.IsConfigured)
        {
            resolved = default!;
            detail = "No target application is configured.";
            return false;
        }

        var match = TryCachedMatch(target);
        if (match == IntPtr.Zero)
        {
            match = FindMatch(target, out var strongMatch);
            cachedMatch = strongMatch && NativeMethods.GetWindowThreadProcessId(match, out var matchedProcessId) != 0
                ? new CachedMatch(target.ProcessName, target.ProcessId, target.WindowTitle ?? string.Empty, match, matchedProcessId)
                : null;
        }

        if (match == IntPtr.Zero || !NativeMethods.IsWindow(match) || !TryGetCaptureBounds(match, out var bounds))
        {
            cachedMatch = null;
            resolved = default!;
            detail = $"{target.ProcessName} is not running or has no captureable window.";
            return false;
        }

        if (NativeMethods.GetWindowThreadProcessId(match, out var processId) == 0 || processId == 0)
        {
            cachedMatch = null;
            resolved = default!;
            detail = $"{target.ProcessName} window no longer has a valid process.";
            return false;
        }

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

    private static IntPtr TryCachedMatch(WindowTargetSettings target)
    {
        var cached = cachedMatch;
        if (cached is null
            || !cached.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase)
            || cached.ConfiguredProcessId != target.ProcessId
            || !cached.ConfiguredTitle.Equals(target.WindowTitle ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || !NativeMethods.IsWindow(cached.Handle)
            || !NativeMethods.IsWindowVisible(cached.Handle)
            || NativeMethods.GetWindowThreadProcessId(cached.Handle, out var processId) == 0
            || processId != cached.ProcessId)
        {
            return IntPtr.Zero;
        }

        try
        {
            // Guards against the window's process exiting and its id being reused.
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase)
                ? cached.Handle
                : IntPtr.Zero;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return IntPtr.Zero;
        }
    }

    // Preference order: the configured process id, then the configured title, then the
    // first window of the named process. Only the first two are stable enough to cache.
    private static IntPtr FindMatch(WindowTargetSettings target, out bool strongMatch)
    {
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

            if (NativeMethods.GetWindowThreadProcessId(window, out var candidateProcessId) == 0
                || candidateProcessId == 0)
            {
                return true;
            }

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

        strongMatch = processMatch != IntPtr.Zero || titleMatch != IntPtr.Zero;
        return processMatch != IntPtr.Zero
            ? processMatch
            : titleMatch != IntPtr.Zero
                ? titleMatch
                : fallbackMatch;
    }

    internal static bool IsTargetForeground(WindowTargetSettings target) =>
        TryResolve(target, out var resolved, out _) && resolved.IsForeground;

    // Final timing-critical revalidation of a window already resolved for this
    // capture. Do not enumerate unrelated windows or reopen every process here.
    internal static bool IsUnchangedForeground(ResolvedWindowTarget captured) =>
        captured.Handle != IntPtr.Zero && captured.ProcessId > 0 && captured.IsForeground && !captured.IsMinimized
        && NativeMethods.IsWindow(captured.Handle) && NativeMethods.IsWindowVisible(captured.Handle)
        && !NativeMethods.IsIconic(captured.Handle) && NativeMethods.GetForegroundWindow() == captured.Handle
        && NativeMethods.GetWindowThreadProcessId(captured.Handle, out var processId) != 0
        && processId == captured.ProcessId && TryGetCaptureBounds(captured.Handle, out var bounds) && bounds == captured.Bounds;

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
        var length = NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return length > 0 ? builder.ToString(0, length) : string.Empty;
    }
}
