namespace CuePilot;

internal sealed record PickpocketInputDelivery(string State, string Detail, double? PlannedMs = null,
    double? KeyDownMs = null, double? KeyUpMs = null, double? TimerWakeMs = null, double? ValidationMilliseconds = null);

/// <summary>One explicitly armed tap. All native input is behind the shared stop/ownership gate.</summary>
internal sealed class PickpocketInputController
{
    private readonly AutomationInputGate gate;
    private readonly Action<bool> send;
    private readonly Func<double> now;
    internal bool Consumed { get; private set; }
    internal int PressCount { get; private set; }
    internal PickpocketInputDelivery? Delivery { get; private set; }

    internal PickpocketInputController(Action<bool>? send = null, Func<double>? now = null)
    {
        this.send = send ?? (up => InputSender.SendVirtualKey(InputKey.Space, up));
        this.now = now ?? (() => PickpocketSampleClock.NowMilliseconds);
        gate = new(new InputReleaseSafety((_, up) => this.send(up), _ => { }));
    }

    internal void BeginRun()
    {
        gate.BeginRun();
        Consumed = false;
        PressCount = 0;
        Delivery = null;
    }

    internal void Disarm(string reason)
    {
        Consumed = true;
        Delivery ??= new("Skipped", reason);
    }

    internal bool Stop() => gate.StopAndReleaseOwnedInput();

    internal void TryTap(PickpocketTimingPrediction prediction, double presentationMs,
        Func<bool> validate, Action<double, CancellationToken> waitUntil, CancellationToken token, bool precision = false)
    {
        if (Consumed || !prediction.CanSchedule || prediction.PressAtMs is not double planned
            || prediction.LatestPressAtMs is not double latest || !double.IsFinite(planned)
            || !double.IsFinite(latest) || !double.IsFinite(presentationMs)) return;
        var current = now();
        // Key hold duration is independent of the key-down deadline. Precision
        // accepts narrow delivery windows and can schedule just before the marker
        // occludes a tiny region. It retains the same 40 ms image-age limit.
        if (latest < planned || (!precision && latest - planned < 35) || planned - current > (precision ? 28 : 16) || current > latest
            || current < presentationMs || current - presentationMs > 40 || (precision && planned - presentationMs > 36)) return;
        if (planned > current) waitUntil(planned, token);
        token.ThrowIfCancellationRequested();
        Consumed = true; // A blocked or failed delivery never retries this run.
        Delivery = new("Blocked", "Final input checks did not pass; no retry this run.", planned, TimerWakeMs: now());
        try
        {
            gate.SendKeyDown(InputKey.Space, token, () =>
            {
                var validationStarted = now();
                var valid = validate();
                Delivery = Delivery with { ValidationMilliseconds = now() - validationStarted };
                if (!valid) throw new InvalidOperationException("Pickpocket input cancelled: target focus, geometry, cooldown or physical Space changed.");
                var deliveryTime = now();
                if (deliveryTime < planned || deliveryTime > latest || deliveryTime - presentationMs > 40)
                    throw new InvalidOperationException("Pickpocket input cancelled: frame or delivery deadline expired.");
                send(false);
                PressCount++;
                Delivery = Delivery with { State = "Sent", Detail = "One Space tap sent. Waiting for the game result.", KeyDownMs = now() };
            });
            waitUntil(now() + 35, token);
        }
        finally
        {
            if (!gate.ReleaseKey(InputKey.Space))
                throw new InvalidOperationException("Space release failed; input is disarmed. Stop retries owned-key cleanup.");
            if (Delivery.KeyDownMs is not null) Delivery = Delivery with { KeyUpMs = now() };
        }
    }
}
