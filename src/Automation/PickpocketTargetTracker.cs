namespace CuePilot;

/// <summary>Confirm targets during Active play; fade-in regions are never latched.</summary>
internal sealed class PickpocketTargetTracker
{
    private PickpocketObservation? previous;
    private PickpocketBand? pending;
    private int confirmations, missing;
    private double lastPresentation = double.NaN;
    internal PickpocketBand? Chosen { get; private set; }
    internal int Revision { get; private set; }
    internal string Reason { get; private set; } = "Waiting for active target confirmation.";

    internal void Reset()
    {
        previous = null;
        pending = null;
        Chosen = null;
        confirmations = missing = 0;
        lastPresentation = double.NaN;
        Revision++;
    }

    internal int? Observe(PickpocketObservation observation, string policy, double presentation, IReadOnlyList<PickpocketBandColor>? customPriority = null, IReadOnlyList<string>? itemPriority = null)
    {
        if (observation.State is PickpocketVisualState.Preparing or PickpocketVisualState.Hidden)
        {
            Reset();
            Reason = "Waiting for active target confirmation; fade-in regions are provisional.";
            return null;
        }
        if (observation.State != PickpocketVisualState.Active)
            return Chosen is null ? null : PickpocketObserverEngine.MatchBand(observation, Chosen);
        var prior = previous;
        previous = observation;
        var fresh = double.IsFinite(presentation) && (double.IsNaN(lastPresentation) || presentation > lastPresentation);
        var contiguous = double.IsNaN(lastPresentation) || presentation - lastPresentation <= 60;
        lastPresentation = presentation;
        var scale = observation.Bar.Width / 576d;
        var samePanel = prior is null || (Math.Abs(prior.Bar.Left - observation.Bar.Left) <= 3 * scale
            && Math.Abs(prior.Bar.Top - observation.Bar.Top) <= 3 * scale && Math.Abs(prior.Bar.Width - observation.Bar.Width) <= 3 * scale);
        if (!fresh || !contiguous || !samePanel || observation.Confidence < .8)
        {
            pending = null;
            confirmations = 0;
            if (!samePanel) { Chosen = null; Revision++; }
            Reason = "Waiting for fresh, stable target geometry.";
            return null;
        }
        if (Chosen is not null)
        {
            var matched = PickpocketObserverEngine.MatchBand(observation, Chosen);
            if (matched.HasValue)
            {
                missing = 0;
                // A higher-priority color may initially be hidden by the marker.
                // Withhold the lower shot while confirming its newly visible rival;
                // never promote on a single frame or change an exact-color policy.
                if ((PickpocketObserverEngine.IsPriorityPolicy(policy) || (policy != "Widest" && itemPriority is { Count: > 0 }))
                    && PickpocketObserverEngine.SelectBand(observation, policy, customPriority: customPriority, itemPriority: itemPriority) is int preferred
                    && (observation.Bands[preferred].Color != Chosen.Color
                        || PickpocketItemReader.Rank(observation.Bands[preferred].ItemName, itemPriority) < PickpocketItemReader.Rank(observation.Bands[matched.Value].ItemName, itemPriority)))
                {
                    confirmations = pending is not null && PickpocketObserverEngine.MatchBand(observation, pending) == preferred ? confirmations + 1 : 1;
                    pending = observation.Bands[preferred];
                    Reason = $"Confirming higher-priority {pending.Color} target ({confirmations}/3 samples).";
                    if (confirmations < 3) return null;
                    Chosen = pending;
                    pending = null;
                    confirmations = 0;
                    Revision++;
                    Reason = "Tracking confirmed higher-priority target.";
                    return preferred;
                }
                pending = null;
                confirmations = 0;
                Reason = "Tracking confirmed target.";
                return matched;
            }
            missing++;
            var nearChosen = observation.MarkerX >= Chosen.Left - 6 * scale && observation.MarkerX <= Chosen.Right + 6 * scale;
            if (missing < 3 || nearChosen)
            {
                Reason = "Selected region is obscured; waiting for confirmation.";
                return null;
            }
            Chosen = null;
            pending = null;
            confirmations = missing = 0;
            Revision++;
        }
        var direction = prior is null ? 0 : Math.Sign(observation.MarkerX - prior.MarkerX);
        var index = direction == 0 ? null : PickpocketObserverEngine.SelectBand(observation, policy, direction, customPriority, itemPriority);
        if (index is not int selected)
        {
            pending = null;
            confirmations = 0;
            Reason = "Waiting for a visible upcoming region.";
            return null;
        }
        var band = observation.Bands[selected];
        confirmations = pending is not null && PickpocketObserverEngine.MatchBand(observation, pending) == selected ? confirmations + 1 : 1;
        pending = band;
        Reason = $"Confirming active target ({confirmations}/3 samples).";
        if (confirmations < 3) return null;
        Chosen = band;
        Revision++;
        Reason = "Tracking confirmed target.";
        return selected;
    }
}
