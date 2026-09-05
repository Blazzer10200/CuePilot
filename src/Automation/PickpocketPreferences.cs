namespace CuePilot;

internal sealed record PickpocketPreferences
{
    public string TargetPolicy { get; set; } = "RarestFirst";
    public string InputMode { get; set; } = "Observe";
    public int RedAdvanceMs { get; set; } = 8;
    public int YellowAdvanceMs { get; set; } = 20;
    public PickpocketBandColor[] CustomPriority { get; set; } = [PickpocketBandColor.Yellow, PickpocketBandColor.Red, PickpocketBandColor.Purple, PickpocketBandColor.Blue, PickpocketBandColor.White];
    public string[] ItemPriority { get; set; } = PickpocketItemReader.Items.Keys.ToArray();
    internal PickpocketPreferences Copy() => this with { CustomPriority = CustomPriority.ToArray(), ItemPriority = ItemPriority.ToArray() };
    private static bool ValidPolicy(string policy) => policy is "Widest" or "PurpleBlueWhite" or "RarestFirst" or "Custom"
        || (Enum.TryParse<PickpocketBandColor>(policy, out var color) && Enum.IsDefined(color) && color.ToString() == policy);
    internal void Validate()
    {
        if (!ValidPolicy(TargetPolicy)) throw new InvalidOperationException("Choose a supported pickpocket target.");
        if (InputMode is not ("Observe" or "SingleAttempt" or "PrecisionAttempt")) throw new InvalidOperationException("Choose Observe, SingleAttempt or PrecisionAttempt input mode.");
        if (RedAdvanceMs is < 0 or > 20 || YellowAdvanceMs is < 0 or > 20)
            throw new InvalidOperationException("Early timing adjustments must be between 0 and 20 ms.");
        if (!ValidOrder(CustomPriority)) throw new InvalidOperationException("Choose one to six different colors for the custom priority order.");
        if (!ValidItems(ItemPriority)) throw new InvalidOperationException("Rank each known item once, without duplicates.");
    }
    private static bool ValidOrder(PickpocketBandColor[]? order) => order is { Length: > 0 and <= 6 }
        && order.All(color => Enum.IsDefined(color)) && order.Distinct().Count() == order.Length;
    private static bool ValidItems(string[]? order) => order?.Length == PickpocketItemReader.Items.Count
        && order.All(name => name is not null && PickpocketItemReader.Items.ContainsKey(name)) && order.Distinct().Count() == order.Length;
    internal void Normalize()
    {
        if (!ValidPolicy(TargetPolicy)) TargetPolicy = "RarestFirst";
        if (InputMode is not ("Observe" or "SingleAttempt" or "PrecisionAttempt")) InputMode = "Observe";
        if (RedAdvanceMs is < 0 or > 20) RedAdvanceMs = 8;
        if (YellowAdvanceMs is < 0 or > 20) YellowAdvanceMs = 20;
        if (!ValidOrder(CustomPriority)) CustomPriority = new PickpocketPreferences().CustomPriority;
        if (!ValidItems(ItemPriority)) ItemPriority = PickpocketItemReader.Items.Keys.ToArray();
    }
}
