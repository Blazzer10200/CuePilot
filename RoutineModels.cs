namespace WorkflowLooper;

internal sealed class WindowTargetSettings
{
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public bool RequireForeground { get; set; } = true;

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(ProcessName);

    internal WindowTargetSettings Copy() => new()
    {
        ProcessName = ProcessName,
        WindowTitle = WindowTitle,
        RequireForeground = RequireForeground,
    };
}

internal sealed class VisualCueSettings
{
    public bool Enabled { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Width { get; set; } = 160;
    public int Height { get; set; } = 90;
    public string Fingerprint { get; set; } = string.Empty;
    public int SimilarityPercent { get; set; } = 86;

    internal bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Fingerprint);

    internal VisualCueSettings Copy() => new()
    {
        Enabled = Enabled,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Width = Width,
        Height = Height,
        Fingerprint = Fingerprint,
        SimilarityPercent = SimilarityPercent,
    };
}

internal sealed class TriggeredRoutineSettings
{
    public int TapIntervalMilliseconds { get; set; } = 150;
    public int HoldMilliseconds { get; set; } = 25;
    public int TriggerHoldMilliseconds { get; set; } = 80;
    public int MaximumDurationSeconds { get; set; } = 210;
    public int CollectDelayMilliseconds { get; set; } = 250;
    public int CooldownSeconds { get; set; } = 5;
    public bool PhysicalClickFinishes { get; set; } = true;
    public bool CollectOnTimeout { get; set; } = true;
    public WindowTargetSettings TargetWindow { get; set; } = new();
    public VisualCueSettings VisualCue { get; set; } = new();

    internal TriggeredRoutineSettings Copy() => new()
    {
        TapIntervalMilliseconds = TapIntervalMilliseconds,
        HoldMilliseconds = HoldMilliseconds,
        TriggerHoldMilliseconds = TriggerHoldMilliseconds,
        MaximumDurationSeconds = MaximumDurationSeconds,
        CollectDelayMilliseconds = CollectDelayMilliseconds,
        CooldownSeconds = CooldownSeconds,
        PhysicalClickFinishes = PhysicalClickFinishes,
        CollectOnTimeout = CollectOnTimeout,
        TargetWindow = TargetWindow.Copy(),
        VisualCue = VisualCue.Copy(),
    };

    internal void Clamp()
    {
        TapIntervalMilliseconds = Math.Clamp(TapIntervalMilliseconds, 20, 5_000);
        HoldMilliseconds = Math.Clamp(HoldMilliseconds, 1, TapIntervalMilliseconds - 1);
        TriggerHoldMilliseconds = Math.Clamp(TriggerHoldMilliseconds, 0, 2_000);
        MaximumDurationSeconds = Math.Clamp(MaximumDurationSeconds, 5, 3_600);
        CollectDelayMilliseconds = Math.Clamp(CollectDelayMilliseconds, 0, 10_000);
        CooldownSeconds = Math.Clamp(CooldownSeconds, 0, 300);
        VisualCue.SimilarityPercent = Math.Clamp(VisualCue.SimilarityPercent, 20, 95);
    }
}

internal enum RoutineState
{
    Stopped,
    Armed,
    Tapping,
    Collecting,
    Cooldown,
    Faulted,
}

internal sealed record RoutineStatus(RoutineState State, string Detail, int ClickCount = 0, double CueSimilarity = 0);

internal sealed record RhythmCalibration(int ClickCount, int IntervalMilliseconds, int HoldMilliseconds);
