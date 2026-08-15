using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CuePilot;

internal sealed class HotkeyBinding
{
    public string Key { get; set; } = "Pause";
    public bool Control { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }

    [JsonIgnore]
    internal string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Control) parts.Add("CTRL");
            if (Shift) parts.Add("SHIFT");
            if (Alt) parts.Add("ALT");
            parts.Add(Key.Equals("Pause", StringComparison.OrdinalIgnoreCase) ? "PAUSE / BREAK" : Key.ToUpperInvariant());
            return string.Join(" + ", parts);
        }
    }

    internal HotkeyBinding Copy() => new() { Key = Key, Control = Control, Shift = Shift, Alt = Alt };
}

internal sealed class AppSettings
{
    public int FormatVersion { get; set; } = 9;
    public string SelectedProfile { get; set; } = "fishing";
    public HotkeyBinding StartStop { get; set; } = DefaultStartStop();
    public HotkeyBinding LockpickingStartStop { get; set; } = DefaultLockpickingStartStop();
    public HotkeyBinding EmergencyStop { get; set; } = DefaultEmergencyStop();
    public FishingRoutineSettings Routine { get; set; } = new();

    internal static AppSettings Defaults() => new();

    internal AppSettings Copy() => new()
    {
        FormatVersion = FormatVersion,
        SelectedProfile = SelectedProfile,
        StartStop = StartStop.Copy(),
        LockpickingStartStop = LockpickingStartStop.Copy(),
        EmergencyStop = EmergencyStop.Copy(),
        Routine = Routine.Copy(),
    };

    internal static HotkeyBinding DefaultStartStop() => new() { Key = "F10" };

    internal static HotkeyBinding DefaultLockpickingStartStop() => new() { Key = "F9" };

    internal static HotkeyBinding DefaultEmergencyStop() => new() { Key = "Pause" };
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static string SettingsPath => AppPaths.SettingsPath;

    internal static AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return AppSettings.Defaults();
        try
        {
            return DeserializeAndMigrate(File.ReadAllText(SettingsPath));
        }
        catch (JsonException)
        {
            return AppSettings.Defaults();
        }
    }

    internal static void Save(AppSettings settings)
    {
        settings.FormatVersion = 9;
        settings.Routine.Clamp();
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
        File.Move(temporaryPath, SettingsPath, true);
    }

    internal static AppSettings RoundTripForTest(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Options), Options)
        ?? throw new InvalidOperationException("Settings serialization returned null.");

    internal static AppSettings DeserializeAndMigrateForTest(string json) => DeserializeAndMigrate(json);

    internal static AppSettings DeserializeAndMigrateForBridge(string json) => DeserializeAndMigrate(json);

    private static AppSettings DeserializeAndMigrate(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("formatVersion", out var versionProperty)
            || versionProperty.GetInt32() is < 1 or > 9)
        {
            return AppSettings.Defaults();
        }

        var formatVersion = versionProperty.GetInt32();
        var root = JsonNode.Parse(json)?.AsObject();
        if (root?["routine"] is JsonObject routine
            && routine["inputMode"]?.GetValue<string>() == "Application")
        {
            routine["inputMode"] = nameof(InputDeliveryMode.Automatic);
            json = root.ToJsonString();
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.Defaults();
        settings.Routine ??= new FishingRoutineSettings();
        settings.Routine.TargetWindow ??= new WindowTargetSettings();
        settings.Routine.TargetWindow.WindowTitle = NormalizeLegacyWindowTitle(settings.Routine.TargetWindow.WindowTitle);
        settings.StartStop ??= AppSettings.DefaultStartStop();
        settings.LockpickingStartStop ??= AppSettings.DefaultLockpickingStartStop();
        settings.EmergencyStop ??= AppSettings.DefaultEmergencyStop();
        if (string.IsNullOrWhiteSpace(settings.SelectedProfile)) settings.SelectedProfile = "fishing";

        if (formatVersion < 3)
        {
            settings.Routine.FishingLowerTensionPercent = 55;
            settings.Routine.FishingUpperTensionPercent = 68;
            settings.Routine.FishingMinimumPulseMilliseconds = 35;
            settings.Routine.FishingMaximumPulseMilliseconds = 90;
            settings.Routine.FishingMinimumRestMilliseconds = 70;
            settings.Routine.CollectOnTimeout = false;
        }

        settings.FormatVersion = 9;
        settings.Routine.Clamp();
        return IsValid(settings) ? settings : AppSettings.Defaults();
    }

    internal static bool IsValid(AppSettings settings) =>
        IsValid(settings.StartStop)
        && IsValid(settings.LockpickingStartStop)
        && IsValid(settings.EmergencyStop)
        && !SameBinding(settings.StartStop, settings.LockpickingStartStop)
        && !SameBinding(settings.StartStop, settings.EmergencyStop)
        && !SameBinding(settings.LockpickingStartStop, settings.EmergencyStop);

    internal static bool IsValid(HotkeyBinding binding) =>
        !string.IsNullOrWhiteSpace(binding.Key)
        && binding.Key is not ("None" or "ControlKey" or "LControlKey" or "RControlKey"
            or "ShiftKey" or "LShiftKey" or "RShiftKey"
            or "Menu" or "LMenu" or "RMenu" or "LWin" or "RWin");

    private static bool SameBinding(HotkeyBinding left, HotkeyBinding right) =>
        left.Key.Equals(right.Key, StringComparison.OrdinalIgnoreCase)
        && left.Control == right.Control
        && left.Shift == right.Shift
        && left.Alt == right.Alt;

    private static string NormalizeLegacyWindowTitle(string title) => title
        .Replace("Ã‚Â®", "®", StringComparison.Ordinal)
        .Replace("Â®", "®", StringComparison.Ordinal);
}
