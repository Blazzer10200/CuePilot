namespace CuePilot;

internal static class PickpocketProgress
{
    internal static string Describe(PickpocketObserveStatus status, bool preparationSeen, bool surveyComplete)
    {
        if (status.Detail.StartsWith("Evidence saving failed:", StringComparison.Ordinal)) return status.Detail;
        if (status.State == "Cooldown") return "Waiting for the three-minute cooldown. Input is off.";
        if (status.InputDelivery?.State == "Sent") return "Space sent. Waiting for the game result.";
        if (status.InputDelivery is { } delivery) return delivery.Detail;
        if (status.InputMode != "Observe" && !preparationSeen) return "Waiting for you to start a new pickpocket.";
        if (status.Observation.State == PickpocketVisualState.Preparing) return "Pickpocket starting. Watching for the moving bar.";
        if (status.Observation.State == PickpocketVisualState.Hidden) return "Waiting for the pickpocket bar.";
        if (status.SelectedBandIndex is not int index || index < 0 || index >= status.Observation.Bands.Count)
            return status.Prediction?.Reason ?? status.Detail;
        var band = status.Observation.Bands[index];
        var color = band.Color == PickpocketBandColor.PaleGreen ? "green" : band.Color.ToString().ToLowerInvariant();
        if (band.ItemName is not null) color = $"{band.ItemName} ({color})";
        if (status.InputMode == "Observe") return $"Tracking {color}. Space stays manual.";
        if (status.InputMode == "PrecisionAttempt" && PickpocketSweepTracker.IsTiny(status.Observation, index) && !surveyComplete)
            return $"Measuring the first sweep for {color}.";
        if (status.Prediction?.CanSchedule == true) return $"Timing {color}. Preparing one Space press.";
        if (status.Prediction is { SpeedPixelsPerSecond: not 0 } prediction
            && (band.Center - status.Observation.MarkerX) * prediction.SpeedPixelsPerSecond < 0)
            return $"Waiting for {color} on the return.";
        return status.Prediction?.Reason ?? status.Detail;
    }
}
