using System.Diagnostics;
using System.Text;

namespace WorkflowLooper;

internal static class WindowTargetService
{
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
            RequireForeground = true,
        };
    }

    internal static bool IsForegroundMatch(WindowTargetSettings target, out string detail)
    {
        if (!target.IsConfigured || !target.RequireForeground)
        {
            detail = "No foreground lock configured.";
            return true;
        }

        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            detail = "No foreground window is active.";
            return false;
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            using var process = Process.GetProcessById((int)processId);
            if (!process.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                detail = $"Foreground app is {process.ProcessName}, expected {target.ProcessName}.";
                return false;
            }

            detail = $"Locked to {target.ProcessName}.";
            return true;
        }
        catch (ArgumentException)
        {
            detail = "The target application closed.";
            return false;
        }
    }

    internal static bool TryGetTargetBounds(WindowTargetSettings target, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var window = FindForegroundTarget(target);
        if (window == IntPtr.Zero || !NativeMethods.GetWindowRect(window, out var rectangle))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static IntPtr FindForegroundTarget(WindowTargetSettings target)
    {
        var window = NativeMethods.GetForegroundWindow();
        if (!target.IsConfigured)
        {
            return window;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase) ? window : IntPtr.Zero;
        }
        catch (ArgumentException)
        {
            return IntPtr.Zero;
        }
    }

    private static string GetTitle(IntPtr window)
    {
        var builder = new StringBuilder(512);
        NativeMethods.GetWindowText(window, builder, builder.Capacity);
        return builder.ToString();
    }
}
