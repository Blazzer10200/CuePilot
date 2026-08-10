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
            if (Control)
            {
                parts.Add("CTRL");
            }

            if (Shift)
            {
                parts.Add("SHIFT");
            }

            if (Alt)
            {
                parts.Add("ALT");
            }

            parts.Add(Key == Keys.Pause ? "PAUSE / BREAK" : Key.ToString().ToUpperInvariant());
            return string.Join(" + ", parts);
        }
    }

    internal HotkeyBinding Copy() => new()
    {
        Key = Key,
        Control = Control,
        Shift = Shift,
        Alt = Alt,
    };

    internal bool Matches(HotkeyBinding other) =>
        Key == other.Key && Control == other.Control && Shift == other.Shift && Alt == other.Alt;
}

internal sealed class AppSettings
{
    public int FormatVersion { get; set; } = 2;
    public HotkeyBinding Record { get; set; } = DefaultRecord();
    public HotkeyBinding Playback { get; set; } = DefaultPlayback();
    public HotkeyBinding EmergencyStop { get; set; } = DefaultEmergencyStop();
    public TriggeredRoutineSettings Routine { get; set; } = new();

    internal static AppSettings Defaults() => new();

    internal AppSettings Copy() => new()
    {
        FormatVersion = FormatVersion,
        Record = Record.Copy(),
        Playback = Playback.Copy(),
        EmergencyStop = EmergencyStop.Copy(),
        Routine = Routine.Copy(),
    };

    internal bool HasDuplicates() =>
        Record.Matches(Playback) || Record.Matches(EmergencyStop) || Playback.Matches(EmergencyStop);

    internal static HotkeyBinding DefaultRecord() => new() { Key = Keys.F6, Control = true, Shift = true };
    internal static HotkeyBinding DefaultPlayback() => new() { Key = Keys.F7, Control = true, Shift = true };
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
        if (!File.Exists(SettingsPath))
        {
            return AppSettings.Defaults();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options);
            if (settings is null || settings.FormatVersion is not (1 or 2) || !IsValid(settings))
            {
                return AppSettings.Defaults();
            }

            settings.FormatVersion = 2;
            settings.Routine ??= new TriggeredRoutineSettings();
            settings.Routine.TargetWindow ??= new WindowTargetSettings();
            settings.Routine.VisualCue ??= new VisualCueSettings();
            settings.Routine.Clamp();
            return settings;
        }
        catch (JsonException)
        {
            return AppSettings.Defaults();
        }
    }

    internal static void Save(AppSettings settings)
    {
        settings.FormatVersion = 2;
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

    internal static bool IsValid(AppSettings settings) =>
        IsValid(settings.Record) && IsValid(settings.Playback) && IsValid(settings.EmergencyStop) && !settings.HasDuplicates();

    internal static bool IsValid(HotkeyBinding binding) => binding.Key is not (
        Keys.None or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
        or Keys.Menu or Keys.LMenu or Keys.RMenu
        or Keys.LWin or Keys.RWin);
}
