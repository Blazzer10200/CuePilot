namespace CuePilot;

internal sealed class WindowTargetSettings
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(ProcessName);

    internal WindowTargetSettings Copy() => new()
    {
        ProcessId = ProcessId,
        ProcessName = ProcessName,
        WindowTitle = WindowTitle,
    };
}

internal enum InputDeliveryMode
{
    Automatic,
    Foreground,
}

internal sealed class FishingRoutineSettings
{
    public int FishingLowerTensionPercent { get; set; } = 55;
    public int FishingUpperTensionPercent { get; set; } = 68;
    public int FishingSampleMilliseconds { get; set; } = 40;
    public int FishingMinimumPulseMilliseconds { get; set; } = 35;
    public int FishingMaximumPulseMilliseconds { get; set; } = 90;
    public int FishingMinimumRestMilliseconds { get; set; } = 70;
    public int MaximumDurationSeconds { get; set; } = 210;
    public int CollectDelayMilliseconds { get; set; } = 250;
    public bool CollectOnTimeout { get; set; }
    public InputDeliveryMode InputMode { get; set; } = InputDeliveryMode.Automatic;
    public WindowTargetSettings TargetWindow { get; set; } = new();

    internal FishingRoutineSettings Copy() => new()
    {
        FishingLowerTensionPercent = FishingLowerTensionPercent,
        FishingUpperTensionPercent = FishingUpperTensionPercent,
        FishingSampleMilliseconds = FishingSampleMilliseconds,
        FishingMinimumPulseMilliseconds = FishingMinimumPulseMilliseconds,
        FishingMaximumPulseMilliseconds = FishingMaximumPulseMilliseconds,
        FishingMinimumRestMilliseconds = FishingMinimumRestMilliseconds,
        MaximumDurationSeconds = MaximumDurationSeconds,
        CollectDelayMilliseconds = CollectDelayMilliseconds,
        CollectOnTimeout = CollectOnTimeout,
        InputMode = InputMode,
        TargetWindow = TargetWindow.Copy(),
    };

    internal void Clamp()
    {
        FishingLowerTensionPercent = Math.Clamp(FishingLowerTensionPercent, 25, 80);
        FishingUpperTensionPercent = Math.Clamp(FishingUpperTensionPercent, FishingLowerTensionPercent + 5, 85);
        FishingSampleMilliseconds = Math.Clamp(FishingSampleMilliseconds, 20, 200);
        FishingMinimumPulseMilliseconds = Math.Clamp(FishingMinimumPulseMilliseconds, 20, 80);
        FishingMaximumPulseMilliseconds = Math.Clamp(FishingMaximumPulseMilliseconds, FishingMinimumPulseMilliseconds, 120);
        FishingMinimumRestMilliseconds = Math.Clamp(FishingMinimumRestMilliseconds, 20, 250);
        MaximumDurationSeconds = Math.Clamp(MaximumDurationSeconds, 5, 3_600);
        CollectDelayMilliseconds = Math.Clamp(CollectDelayMilliseconds, 0, 10_000);
    }
}

internal enum RoutineState
{
    Stopped,
    Armed,
    Regulating,
    Collecting,
    Stowing,
    Casting,
    Faulted,
}

internal sealed record RoutineStatus(RoutineState State, string Detail, int SampleCount = 0, double Confidence = 0);
