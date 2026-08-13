using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkflowLooper;

internal sealed class HotkeyBinding
{
    public Keys Key { get; set; }
    public bool Control { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }

    [JsonIgnore]
    internal uint NativeModifiers => NativeMethods.ModNoRepeat
        | (Control ? NativeMethods.ModControl : 0)
        | (Shift ? NativeMethods.ModShift : 0)
        | (Alt ? NativeMethods.ModAlt : 0);

    [JsonIgnore]
    internal string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Control) parts.Add("CTRL");
            if (Shift) parts.Add("SHIFT");
            if (Alt) parts.Add("ALT");
            parts.Add(Key == Keys.Pause ? "PAUSE / BREAK" : Key.ToString().ToUpperInvariant());
            return string.Join(" + ", parts);
        }
    }

    internal HotkeyBinding Copy() => new() { Key = Key, Control = Control, Shift = Shift, Alt = Alt };
}

internal sealed class AppSettings
{
    public int FormatVersion { get; set; } = 7;
    public string SelectedProfile { get; set; } = "fishing";
    public HotkeyBinding EmergencyStop { get; set; } = DefaultEmergencyStop();
    public FishingRoutineSettings Routine { get; set; } = new();

    internal static AppSettings Defaults() => new();

    internal AppSettings Copy() => new()
    {
        FormatVersion = FormatVersion,
        SelectedProfile = SelectedProfile,
        EmergencyStop = EmergencyStop.Copy(),
        Routine = Routine.Copy(),
    };

    internal static HotkeyBinding DefaultEmergencyStop() => new() { Key = Keys.Pause };
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkflowLooper",
        "settings.json");

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
        settings.FormatVersion = 7;
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
            || versionProperty.GetInt32() is < 1 or > 7)
        {
            return AppSettings.Defaults();
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.Defaults();
        settings.Routine ??= new FishingRoutineSettings();
        settings.Routine.TargetWindow ??= new WindowTargetSettings();
        settings.EmergencyStop ??= AppSettings.DefaultEmergencyStop();
        if (string.IsNullOrWhiteSpace(settings.SelectedProfile)) settings.SelectedProfile = "fishing";

        if (versionProperty.GetInt32() < 3)
        {
            settings.Routine.FishingLowerTensionPercent = 55;
            settings.Routine.FishingUpperTensionPercent = 68;
            settings.Routine.FishingMinimumPulseMilliseconds = 35;
            settings.Routine.FishingMaximumPulseMilliseconds = 90;
            settings.Routine.FishingMinimumRestMilliseconds = 70;
            settings.Routine.CollectOnTimeout = false;
        }

        settings.FormatVersion = 7;
        settings.Routine.Clamp();
        return IsValid(settings) ? settings : AppSettings.Defaults();
    }

    internal static bool IsValid(AppSettings settings) => IsValid(settings.EmergencyStop);

    internal static bool IsValid(HotkeyBinding binding) => binding.Key is not (
        Keys.None or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
        or Keys.Menu or Keys.LMenu or Keys.RMenu
        or Keys.LWin or Keys.RWin);
}
