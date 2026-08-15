namespace CuePilot;

internal static class AppPaths
{
    internal const string ProductDirectoryName = "CuePilot";

    internal static string LocalDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductDirectoryName);

    internal static string SettingsPath { get; } = Path.Combine(LocalDataDirectory, "settings.json");
    internal static string DiagnosticsDirectory { get; } = Path.Combine(LocalDataDirectory, "diagnostics");
    internal static string DebugSessionsDirectory { get; } = Path.Combine(DiagnosticsDirectory, "sessions");
}
