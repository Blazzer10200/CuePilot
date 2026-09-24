namespace CuePilot;

internal sealed record PickpocketInputDelivery(string State, string Detail, double? PlannedMs = null,
    double? KeyDownMs = null, double? KeyUpMs = null, double? TimerWakeMs = null, double? ValidationMilliseconds = null);

/// <summary>One explicitly armed tap. All native input is behind the shared stop/ownership gate.</summary>
internal sealed class PickpocketInputController
{
    internal const double HoldMilliseconds = 35;
    private readonly AutomationInputGate gate;
    private readonly Action<bool> send;
    private readonly Func<double> now;
    internal bool Consumed { get; private set; }
    internal int PressCount { get; private set; }
    internal PickpocketInputDelivery? Delivery { get; private set; }
    /// <summary>Monotonic time at which an owned Space hold should end; null when nothing is held.</summary>
    internal double? ReleaseDueMs { get; private set; }

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
        ReleaseDueMs = null;
    }

    internal void Disarm(string reason)
    {
        Consumed = true;
        Delivery ??= new("Skipped", reason);
    }

    internal bool Stop()
    {
        ReleaseDueMs = null;
        var released = gate.StopAndReleaseOwnedInput();
        if (released) StampKeyUp();
        return released;
    }

    /// <summary>
    /// Releases an owned Space once its hold has elapsed. The observer loop calls
    /// this around its frame wait so capture continues through the press instead
    /// of blocking on the hold. Stop and run cleanup release regardless.
    /// </summary>
    internal bool ReleaseIfDue(bool immediately = false)
    {
        if (ReleaseDueMs is not double due || (!immediately && now() < due)) return false;
        ReleaseDueMs = null;
        if (!gate.ReleaseKey(InputKey.Space))
            throw new InvalidOperationException("Space release failed; input is disarmed. Stop retries owned-key cleanup.");
        StampKeyUp();
        return true;
    }

    private void StampKeyUp()
    {
        if (Delivery is { KeyDownMs: not null, KeyUpMs: null }) Delivery = Delivery with { KeyUpMs = now() };
    }

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
            // Hold at least 35 ms without blocking the capture loop; ReleaseIfDue ends it.
            ReleaseDueMs = Delivery.KeyDownMs + HoldMilliseconds;
        }
        catch
        {
            // Nothing is owned unless the down event was sent; the gate keeps that truth.
            if (!gate.ReleaseKey(InputKey.Space))
                throw new InvalidOperationException("Space release failed; input is disarmed. Stop retries owned-key cleanup.");
            throw;
        }
    }
}
