namespace CuePilot;

/// <summary>Recognizes a measured sweep and real edge turnaround without sending input.</summary>
internal sealed class PickpocketSweepTracker
{
    private PickpocketObservation? previous;
    private double lastTime = double.NaN, startX, extreme;
    private int direction, samples, reverseSamples;
    internal bool FirstPassComplete { get; private set; }

    internal static bool IsTiny(PickpocketObservation observation, int index) =>
        observation.Bands[index].Width <= 6 * observation.Bar.Width / 576d;

    private void Reset()
    {
        previous = null;
        lastTime = double.NaN;
        direction = samples = reverseSamples = 0;
        FirstPassComplete = false;
    }

    internal void Observe(PickpocketObservation observation, double presentation)
    {
        if (observation.State != PickpocketVisualState.Active || observation.Confidence < .8
            || !double.IsFinite(presentation) || !double.IsFinite(observation.MarkerX) || observation.Bar.Width <= 0)
        { Reset(); return; }
        var prior = previous;
        var scale = observation.Bar.Width / 576d;
        if (prior is not null && (presentation <= lastTime || presentation - lastTime > 60
            || Math.Abs(prior.Bar.Left - observation.Bar.Left) > 3 * scale
            || Math.Abs(prior.Bar.Top - observation.Bar.Top) > 3 * scale
            || Math.Abs(prior.Bar.Width - observation.Bar.Width) > 3 * scale
            || Math.Abs(observation.MarkerX - prior.MarkerX) > observation.Bar.Width * .1))
        { Reset(); prior = null; }
        previous = observation;
        lastTime = presentation;
        if (prior is null) { startX = extreme = observation.MarkerX; return; }
        var step = Math.Sign(observation.MarkerX - prior.MarkerX);
        if (step == 0) return; // A repeated edge position is not a turnaround.
        if (direction == 0) direction = step;
        if (step == direction)
        {
            extreme = direction > 0 ? Math.Max(extreme, observation.MarkerX) : Math.Min(extreme, observation.MarkerX);
            samples++;
            reverseSamples = 0;
            return;
        }
        reverseSamples++;
        if (reverseSamples < 3 || Math.Abs(observation.MarkerX - extreme) < 6 * scale) return;
        var atEdge = direction > 0 ? extreme >= observation.Bar.Right - observation.Bar.Width * .08
            : extreme <= observation.Bar.Left + observation.Bar.Width * .08;
        if (atEdge && samples >= 24 && Math.Abs(extreme - startX) >= observation.Bar.Width * .6)
            FirstPassComplete = true;
        // Fit the return separately; never treat a mid-bar zigzag as a full pass.
        direction = step;
        startX = extreme;
        extreme = observation.MarkerX;
        samples = reverseSamples;
        reverseSamples = 0;
    }
}
