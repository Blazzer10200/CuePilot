using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Numerics;

namespace CuePilot;

internal enum PickpocketVisualState { Hidden, Uncertain, Preparing, Active, Grabbed, Missed }
internal enum PickpocketBandColor { White, Purple, Red, PaleGreen, Blue, Yellow }
internal sealed record PickpocketBand(PickpocketBandColor Color, double Left, double Right, string? ItemName = null)
{
    internal double Center => (Left + Right) / 2;
    internal double Width => Right - Left;
}
internal sealed record PickpocketObservation(
    PickpocketVisualState State, Rectangle Bar, double MarkerX,
    IReadOnlyList<PickpocketBand> Bands, double Confidence, string Reason)
{
    /// <summary>Consecutive frames kept Active by tracked continuation while the status header was unreadable.</summary>
    internal int HeaderlessFrames { get; init; }
    internal static PickpocketObservation Missing => new(PickpocketVisualState.Hidden, Rectangle.Empty, 0, [], 0, "No verified pickpocket panel.");
}

/// <summary>Bounded pixel reader; no capture or input. Coordinates are relative to the supplied bitmap.</summary>
internal static class PickpocketDetector
{
    private static readonly Lazy<HeaderMask[]> Headers = new(LoadHeaders);
    private const int HeaderGridWidth = 114;
    private const int HeaderWordCount = (HeaderGridWidth * 22 + 63) / 64;

    internal static PickpocketObservation Analyze(Bitmap frame, Rectangle? search = null, PickpocketObservation? previous = null)
    {
        using var pixels = new Pixels(frame);
        var bounds = Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height),
            search ?? new Rectangle(0, frame.Height / 2, frame.Width, frame.Height - frame.Height / 2));
        // While the marker is already tracked, scan the columns it can reach
        // instead of the whole region. Shorter analysis leaves more of the frame
        // age budget for the press, and any failure falls back to the full scan
        // below, so this narrows work without narrowing what can be detected.
        // Every pass gets its own header budget. On grass or pale clothing the wide
        // pass finds hundreds of marker-like stems and one of them can spend the
        // whole budget, which starved the solid-stem pass that holds the real marker.
        if (TrackedBounds(bounds, previous) is Rectangle near)
        {
            var tracked = ScanStems(pixels, near, previous, 36, new HeaderSearch()) ?? ScanStems(pixels, near, previous, 3, new HeaderSearch());
            if (IsTrackedContinuation(tracked, previous))
                return StabilizeMarkerOcclusion(tracked!, previous);
        }
        var best = ScanStems(pixels, bounds, previous, 36, new HeaderSearch()) ?? ScanStems(pixels, bounds, previous, 3, new HeaderSearch());
        return StabilizeMarkerOcclusion(best ?? PickpocketObservation.Missing, previous);
    }

    /// <summary>
    /// Offline diagnostics only (the session analyzer sets it, live capture never does).
    /// When non-null, each rejection point below records why, so "Hidden" stops being
    /// an opaque answer. The live path pays one null check per rejection.
    /// </summary>
    [ThreadStatic] internal static List<string>? Ledger;

    internal sealed record PanelProbe(PickpocketVisualState HeaderState, double HeaderScore, double HeaderText, double HeaderBackground,
        bool MarkerFound, double MarkerX, IReadOnlyList<PickpocketBand> Bands);

    /// <summary>
    /// Offline: what a frame shows at a KNOWN bar, without trusting the status header.
    /// <paramref name="bar"/> is in this bitmap's coordinates. Separates "the panel is not
    /// there" (no marker, no target intervals) from "the panel is there but its header is
    /// unreadable" (marker and intervals present, low header score).
    /// </summary>
    internal static PanelProbe ProbePanel(Bitmap frame, Rectangle bar)
    {
        using var pixels = new Pixels(frame);
        var scale = bar.Width / 576d;
        var y = bar.Top + bar.Height / 2;
        var header = MatchHeaderDetailed(pixels, bar.Left, y, scale);
        var area = Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height),
            Rectangle.FromLTRB(bar.Left - 3, bar.Top - (int)Math.Ceiling(30 * scale), bar.Right + 3, bar.Bottom + (int)Math.Ceiling(30 * scale)));
        var stems = area.IsEmpty ? [] : FindStems(pixels, area, 3);
        var found = stems.Count > 0;
        var anchor = found ? stems.OrderByDescending(s => s.Density * s.Length).First() : default;
        var bands = ReadBands(pixels, bar.Left, Math.Min(bar.Right, frame.Width) - 1, found ? anchor.X : -10000, y, scale);
        return new PanelProbe(header.State, header.Score, header.Foreground, header.Background, found, found ? StemCentroid(stems, anchor) : double.NaN, bands);
    }

    /// <summary>
    /// Columns the marker can occupy on the next frame, or null when there is no
    /// usable previous observation. Padded well past one frame of travel and past
    /// the adjacent columns <see cref="StemCentroid"/> needs on either side.
    /// </summary>
    private static Rectangle? TrackedBounds(Rectangle bounds, PickpocketObservation? previous)
    {
        if (previous is not { State: PickpocketVisualState.Active } prior || prior.Bar.Width <= 0) return null;
        if (!double.IsFinite(prior.MarkerX) || prior.MarkerX < bounds.Left || prior.MarkerX > bounds.Right) return null;
        // 12% of the bar covers several frames of the fastest travel observed in
        // the recorded evidence, plus the centroid's own +/-3 column reach.
        var reach = Math.Max(24, prior.Bar.Width * 0.12) + 4;
        var left = (int)Math.Floor(prior.MarkerX - reach);
        var right = (int)Math.Ceiling(prior.MarkerX + reach);
        var window = Rectangle.Intersect(bounds, Rectangle.FromLTRB(left, bounds.Top, right, bounds.Bottom));
        // A window that is not actually narrower would only add a second scan.
        return window.Width > 0 && window.Width < bounds.Width * 0.75 ? window : null;
    }

    /// <summary>
    /// True when a localized scan found the same panel it was tracking. Anything
    /// else (lost marker, moved or resized bar, a different state) must be
    /// confirmed by the full scan so scenery cannot capture the tracker.
    /// </summary>
    private static bool IsTrackedContinuation(PickpocketObservation? candidate, PickpocketObservation? previous)
    {
        if (candidate is not { State: PickpocketVisualState.Active } found || previous is not { } prior) return false;
        if (found.Bar.Width <= 0) return false;
        var tolerance = Math.Max(2.0, found.Bar.Width / 576d * 3);
        return Math.Abs(found.Bar.Left - prior.Bar.Left) <= tolerance
            && Math.Abs(found.Bar.Top - prior.Bar.Top) <= tolerance
            && Math.Abs(found.Bar.Width - prior.Bar.Width) <= tolerance;
    }

    private static PickpocketObservation? ScanStems(Pixels pixels, Rectangle bounds, PickpocketObservation? previous, int maximumGap, HeaderSearch searchWork)
    {
        PickpocketObservation? best = null;
        var stems = FindStems(pixels, bounds, maximumGap);
        Ledger?.Add($"scan gap={maximumGap}: {stems.Count} marker-like stem(s)"
            + (stems.Count == 0 ? " (no green marker column found in the search area)"
                : ": " + string.Join(", ", stems.Take(4).Select(s => $"x={s.X} len={s.Length} density={s.Density:F2}"))));
        // Keep the occluded-marker path first. If scenery joins it into a long
        // green column, recover with short gaps and rank solid stems before
        // spending a separate bounded header budget. Neither path skips identity checks.
        var candidates = maximumGap == 36 ? stems.AsEnumerable()
            : stems.OrderByDescending(stem => stem.Density).ThenByDescending(stem => stem.Length);
        (int X, int Top, int Length, double Density)? bestStem = null;
        var inspected = 0;
        foreach (var stem in candidates.Take(16))
        {
            inspected++;
            var candidate = InspectStem(pixels, stem.X, stem.Top, stem.Length, stem.Density, previous, searchWork);
            if (candidate is not null && (best is null || candidate.Confidence > best.Confidence)) { best = candidate; bestStem = stem; }
            if (searchWork.Exhausted) break;
        }
        Ledger?.Add($"scan gap={maximumGap}: inspected {inspected} of {stems.Count} stems" + (searchWork.Exhausted ? "; HEADER SEARCH BUDGET EXHAUSTED" : "") + (best is null ? "; none accepted" : ""));
        // Identity checks ran on one integer column. Report the marker at the
        // hit-weighted centre of its adjacent stem columns so the motion fit and
        // the result offset are not quantised to whole pixels.
        return best is not null && bestStem is { } anchor ? best with { MarkerX = StemCentroid(stems, anchor) } : best;
    }

    private static List<(int X, int Top, int Length, double Density)> FindStems(Pixels pixels, Rectangle bounds, int maximumGap)
    {
        var stems = new List<(int X, int Top, int Length, double Density)>();
        // The protruding green stem is longer than the colored bands. Scan columns
        // without copying a full-screen buffer or performing template-pyramid searches.
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var start = -1;
            var last = -1;
            var hits = 0;
            for (var y = bounds.Top; y <= bounds.Bottom; y++)
            {
                if (y < bounds.Bottom && pixels.MarkerAt(x, y))
                {
                    if (start < 0) start = y;
                    last = y;
                    hits++;
                    continue;
                }
                if (start < 0) continue;
                if (y < bounds.Bottom && y - last <= maximumGap) continue;
                var length = last - start + 1;
                if (hits >= 14 && length >= 26 && length <= 100)
                {
                    var middle = start + length / 2;
                    if (!IsMarker(pixels.At(x - 6, middle)) && !IsMarker(pixels.At(x + 6, middle)))
                        stems.Add((x, start, length, hits / (double)length));
                }
                start = -1;
                hits = 0;
            }
        }
        return stems;
    }

    private static double StemCentroid(List<(int X, int Top, int Length, double Density)> stems, (int X, int Top, int Length, double Density) anchor)
    {
        double weight = anchor.Length * anchor.Density, sum = anchor.X * weight;
        for (var side = -1; side <= 1; side += 2)
        for (var x = anchor.X + side; Math.Abs(x - anchor.X) <= 3; x += side)
        {
            var column = stems.FirstOrDefault(s => s.X == x && Math.Abs(s.Top - anchor.Top) <= 4 && Math.Abs(s.Length - anchor.Length) <= 8);
            if (column.Length == 0) break; // Contiguous stem columns only.
            var hits = column.Length * column.Density;
            weight += hits;
            sum += x * hits;
        }
        return weight > 0 ? sum / weight : anchor.X;
    }

    internal static PickpocketObservation StabilizeMarkerOcclusion(PickpocketObservation observation, PickpocketObservation? previous)
    {
        if (observation.State != PickpocketVisualState.Active || previous?.State != PickpocketVisualState.Active) return observation;
        var scale = observation.Bar.Width / 576d;
        var tolerance = 2 * scale;
        var markerRadius = 6 * scale;
        if (scale <= 0 || Math.Abs(previous.Bar.Left - observation.Bar.Left) > tolerance
            || Math.Abs(previous.Bar.Top - observation.Bar.Top) > tolerance
            || Math.Abs(previous.Bar.Width - observation.Bar.Width) > tolerance) return observation;
        var bands = observation.Bands.Select(band =>
        {
            // The marker exclusion mask can eat an interval's entry or exit edge.
            // Recover only a partially visible, uniquely matched previous interval:
            // the opposite edge must still agree, and both hidden/visible edges
            // must lie beside the current marker. Never restore an absent region.
            var matches = previous.Bands.Where(old => old.Color == band.Color && old.Width > 2 * markerRadius &&
                ((band.Left > old.Left && Math.Abs(old.Right - band.Right) <= tolerance
                    && Math.Abs(old.Left - observation.MarkerX) <= markerRadius && Math.Abs(band.Left - observation.MarkerX) <= markerRadius)
                || (band.Right < old.Right && Math.Abs(old.Left - band.Left) <= tolerance
                    && Math.Abs(old.Right - observation.MarkerX) <= markerRadius && Math.Abs(band.Right - observation.MarkerX) <= markerRadius))).ToArray();
            if (matches.Length != 1) return band;
            var prior = matches[0];
            return band.Left > prior.Left && Math.Abs(prior.Right - band.Right) <= tolerance
                ? band with { Left = prior.Left } : band with { Right = prior.Right };
        }).ToArray();
        return observation with { Bands = bands };
    }

    private static PickpocketObservation? InspectStem(Pixels pixels, int x, int top, int length, double density, PickpocketObservation? previous, HeaderSearch searchWork)
    {
        var center = top + length / 2;
        // Scenery touching one end of the stem moves its middle several pixels off
        // the bar, which misaligns the header template. While a settled panel is
        // tracked, try its known bar row first when the stem still spans it.
        if (previous is { State: PickpocketVisualState.Active, Bar.Width: > 0 } tracked
            && x >= tracked.Bar.Left - 3 && x <= tracked.Bar.Right + 3)
        {
            var barY = tracked.Bar.Top + tracked.Bar.Height / 2;
            if (barY != center && Math.Abs(barY - center) <= 12 && barY >= top && barY < top + length
                && InspectStemAt(pixels, x, top, length, density, barY, previous, searchWork) is { } snapped) return snapped;
        }
        return InspectStemAt(pixels, x, top, length, density, center, previous, searchWork);
    }

    private static PickpocketObservation? InspectStemAt(Pixels pixels, int x, int top, int length, double density, int y, PickpocketObservation? previous, HeaderSearch searchWork)
    {
        if (IsMarker(pixels.At(x - 6, y)) || IsMarker(pixels.At(x + 6, y)))
        {
            Ledger?.Add($"stem x={x} len={length}: rejected, marker-colored pixels 6 px to its side (scenery joined to the stem)");
            return null;
        }
        if (ContinueTrackedPanel(pixels, x, y, previous, searchWork) is { } continued)
        {
            Ledger?.Add($"stem x={x} len={length}: kept Active by header-less continuation (frame {continued.HeaderlessFrames} without a readable header)");
            return continued;
        }
        // Anchor the panel to the status header and marker geometry, independently
        // of item count, ordering, spacing, and duplicate colors.
        var panel = LocatePanel(pixels, x, y, length, density, previous, searchWork);
        if (panel is null)
        {
            Ledger?.Add($"stem x={x} len={length}: no status header matched, so the panel was not verified");
            return null;
        }
        var (left, scale, state, confidence, panelY) = panel.Value;
        y = panelY;
        var width = (int)Math.Round(576 * scale);
        var right = left + width - 1;
        if (left < 0 || right >= pixels.Width) return null;
        var bar = new Rectangle(left, y - (int)Math.Round(11 * scale), width, Math.Max(1, (int)Math.Round(23 * scale)));
        // A miss tints the whole bar red. It is a result, never a giant target.
        if (state == PickpocketVisualState.Missed)
            return new PickpocketObservation(state, bar, x, [], confidence, "Missed result verified; target intervals are no longer actionable.");
        var bands = ReadBands(pixels, left, right, x, y, scale);
        if (bands.Count is < 1 or > 16)
        {
            Ledger?.Add($"stem x={x}: header read as {state} ({confidence:F2}) but {bands.Count} target interval(s) found (need 1-16)");
            return null;
        }
        Ledger?.Add($"stem x={x}: accepted as {state} (header score {confidence:F2}, {bands.Count} intervals)");
        return new PickpocketObservation(state, bar, x, bands, confidence, "Status header, marker, and current target intervals verified independently.");
    }

    // ~0.5 s of play at the 16 ms Active cadence; the live header glitch lasts up to ~250 ms.
    private const int MaximumHeaderlessFrames = 30;

    /// <summary>
    /// The game animates the status header, so a tracked panel loses its header
    /// match for several frames mid-sweep. Keep it Active only when the marker
    /// sits on the same bar and every wide target interval away from the marker
    /// is still at the same pixels, with nothing new inside the bar; scenery
    /// cannot reproduce that layout. A readable header of any state takes the
    /// verified path, and the run of header-less frames is bounded.
    /// </summary>
    private static PickpocketObservation? ContinueTrackedPanel(Pixels pixels, int x, int y, PickpocketObservation? previous, HeaderSearch searchWork)
    {
        if (previous is not { State: PickpocketVisualState.Active } prior || prior.Bar.Width <= 0)
        {
            Ledger?.Add("header-less continuation unavailable: the previous frame was not Active, so tracking had been reset");
            return null;
        }
        if (prior.HeaderlessFrames >= MaximumHeaderlessFrames)
        {
            Ledger?.Add($"header-less continuation exhausted: {prior.HeaderlessFrames} frames in a row without a readable header");
            return null;
        }
        if (Math.Abs(prior.Bar.Top + prior.Bar.Height / 2 - y) > 3 || x < prior.Bar.Left - 3 || x > prior.Bar.Right + 3)
        {
            Ledger?.Add("header-less continuation: this stem is not on the tracked bar");
            return null;
        }
        var scale = prior.Bar.Width / 576d;
        // Same key LocatePanel tries first, so this costs no extra header work.
        if (searchWork.Match(pixels, prior.Bar.Left, y, scale).Score >= .85) return null;
        var bands = ReadBands(pixels, prior.Bar.Left, prior.Bar.Right - 1, x, y, scale);
        if (!SameTargets(bands, prior.Bands, x, scale)) return null;
        return new PickpocketObservation(PickpocketVisualState.Active, prior.Bar, x, bands, .8,
            "Status header unreadable; bar and target intervals match the tracked panel.") { HeaderlessFrames = prior.HeaderlessFrames + 1 };
    }

    private static bool SameTargets(IReadOnlyList<PickpocketBand> current, IReadOnlyList<PickpocketBand> prior, double markerX, double scale)
    {
        var tolerance = Math.Max(2, 2 * scale);
        var occlusion = 6 * scale + tolerance;
        bool Wide(PickpocketBand band) => band.Width > 2 * scale;
        var matched = 0;
        foreach (var old in prior.Where(Wide))
        {
            // The marker can hide or split an interval it overlaps.
            if (markerX >= old.Left - occlusion && markerX <= old.Right + occlusion) continue;
            if (!current.Any(band => band.Color == old.Color && Math.Abs(band.Left - old.Left) <= tolerance && Math.Abs(band.Right - old.Right) <= tolerance))
            {
                Ledger?.Add($"header-less continuation failed: the {old.Color} interval at x={old.Left:F0}-{old.Right:F0} is not at the same pixels now"
                    + $" (frame shows: {(current.Count == 0 ? "no intervals" : string.Join(", ", current.Select(b => $"{b.Color} {b.Left:F0}-{b.Right:F0}")))})");
                return false;
            }
            matched++;
        }
        // Overlap, not containment: the previous frame's marker may have hidden an edge.
        var nothingNew = current.Where(Wide).All(band => prior.Any(old => old.Color == band.Color
            && band.Left <= old.Right + tolerance && band.Right >= old.Left - tolerance));
        if (matched < 1) Ledger?.Add("header-less continuation failed: no wide interval away from the marker to compare");
        else if (!nothingNew) Ledger?.Add("header-less continuation failed: a new wide interval appeared inside the bar");
        return matched >= 1 && nothingNew;
    }

    private static List<PickpocketBand> ReadBands(Pixels pixels, int left, int right, int x, int y, double scale)
    {
        var bands = new List<PickpocketBand>();
        PickpocketBandColor? current = null;
        var start = left;
        var sampleOffset = Math.Max(2, (int)Math.Round(5 * scale));
        for (var column = left; column <= right + 1; column++)
        {
            PickpocketBandColor? color = column <= right ? BandColor(pixels.At(column, y)) : null;
            // The compressed marker has a pale-green fringe one pixel beyond
            // its bright stem. Exclude it before reconnecting an occluded band.
            if (Math.Abs(column - x) <= Math.Ceiling(3 * scale)) color = null;
            if (color is not null && (BandColor(pixels.At(column, y - sampleOffset)) != color
                || BandColor(pixels.At(column, y + sampleOffset)) != color)) color = null;
            if (color == current) continue;
            if (current is not null && column - start >= Math.Max(1, scale))
                bands.Add(new PickpocketBand(current.Value, start, column));
            current = color;
            start = column;
        }
        // A marker can split one target interval; reconnect only the small gap it occupies.
        for (var i = bands.Count - 1; i > 0; i--)
        {
            var before = bands[i - 1];
            var after = bands[i];
            if (before.Color == after.Color && after.Left - before.Right <= 8 * scale + 1
                && x >= before.Right - 3 * scale && x <= after.Left + 3 * scale)
            {
                bands[i - 1] = before with { Right = after.Right };
                bands.RemoveAt(i);
            }
        }
        return bands;
    }

    private static (int Left, double Scale, PickpocketVisualState State, double Confidence, int Y)? LocatePanel(
        Pixels pixels, int markerX, int y, int length, double density, PickpocketObservation? previous, HeaderSearch searchWork)
    {
        if (previous is { State: not PickpocketVisualState.Hidden } && previous.Bar.Width > 0
            && Math.Abs(previous.Bar.Top + previous.Bar.Height / 2 - y) <= 3)
        {
            var priorScale = previous.Bar.Width / 576d;
            var match = searchWork.Match(pixels, previous.Bar.Left, y, priorScale);
            if (match.Score >= .85 && markerX >= previous.Bar.Left - 3 && markerX <= previous.Bar.Right + 3)
                return (previous.Bar.Left, priorScale, match.State, match.Score, y);
        }
        var estimate = Math.Round(length / 45d * 40) / 40;
        var scales = new[] { estimate, estimate - .025, estimate + .025, estimate - .05, estimate + .05 }.Where(s => s >= .5).ToArray();
        foreach (var scale in scales)
        {
            var left = (int)Math.Round((pixels.Width - 576 * scale) / 2);
            if (markerX < left - 3 || markerX > left + 576 * scale + 3) continue;
            var match = searchWork.Match(pixels, left, y, scale);
            if (match.Score >= .85) return (left, scale, match.State, match.Score, y);
        }
        // A solid stem is very likely the real marker, but scenery touching its ends makes it
        // longer than the panel implies, so the length-based scale estimate can be off by more
        // than the usual tolerance. Try wider scales at the centered position before the
        // expensive sparse scan.
        if (density >= .6)
            foreach (var offset in new[] { -.075, .075, -.1, .1, -.125, .125, -.15 })
            {
                var scale = estimate + offset;
                if (scale < .5) continue;
                var left = (int)Math.Round((pixels.Width - 576 * scale) / 2);
                if (markerX < left - 3 || markerX > left + 576 * scale + 3) continue;
                var match = searchWork.Match(pixels, left, y, scale);
                if (match.Score >= .85) return (left, scale, match.State, match.Score, y);
            }
        if (density >= .6)
        {
            // Scenery also shifts the stem's middle off the bar row. The panel scale follows the capture
            // width (the UI scales with a 16:9 window), so probe that scale over a few row offsets.
            var natural = Math.Round(pixels.Width * .000744 * 40) / 40;
            foreach (var scale in new[] { natural, natural - .025, natural + .025 })
            {
                var left = (int)Math.Round((pixels.Width - 576 * scale) / 2);
                if (scale < .5 || markerX < left - 3 || markerX > left + 576 * scale + 3) continue;
                foreach (var dy in new[] { 0, -3, 3, -6, 6, -9, 9, -12, 12 })
                {
                    var match = searchWork.Match(pixels, left, y + dy, scale);
                    if (match.Score >= .85) return (left, scale, match.State, match.Score, y + dy);
                }
            }
        }
        // Non-centered/cropped layouts: sparsely prefilter glyph cores before the
        // full header check. This path is acquisition only when tracking is valid.
        foreach (var scale in scales)
        foreach (var header in Headers.Value)
        {
            var maxLeft = Math.Min(markerX + 3, pixels.Width - (int)Math.Round(576 * scale));
            var minLeft = Math.Max(0, markerX - (int)Math.Round(576 * scale));
            for (var left = minLeft; left <= maxLeft; left += 2)
            {
                if (searchWork.Exhausted) return null;
                var hits = 0;
                for (var n = 0; n < 10; n++)
                {
                    var point = header.Foreground[n * header.Foreground.Length / 10];
                    if (IsText(pixels.At(left + (int)Math.Round(point.X * scale), y + (int)Math.Round((point.Y - 211) * scale)))) hits++;
                }
                // Sparse cores can lose pixels under fractional scaling; the full
                // positive/negative mask remains the acceptance gate.
                if (hits < 4) continue;
                var match = searchWork.Match(pixels, left, y, scale);
                if (match.Score >= .80) return (left, scale, match.State, match.Score, y);
            }
        }
        if (Ledger is not null) LedgerHeaderProbes(pixels, y, length, previous, searchWork.Exhausted);
        return null;
    }

    private static void LedgerHeaderProbes(Pixels pixels, int y, int length, PickpocketObservation? previous, bool exhausted)
    {
        var probes = new List<(int Left, double Scale, string Where)>();
        if (previous is { Bar.Width: > 0 }) probes.Add((previous.Bar.Left, previous.Bar.Width / 576d, "at the last known bar"));
        var estimate = Math.Round(length / 45d * 40) / 40;
        if (estimate >= .5) probes.Add(((int)Math.Round((pixels.Width - 576 * estimate) / 2), estimate, "centered"));
        foreach (var (left, scale, where) in probes)
        {
            var m = MatchHeaderDetailed(pixels, left, y, scale);
            Ledger!.Add($"  header probe {where}: best {m.State} score {m.Score:F2} (needs 0.85): text found {m.Foreground:P0}, clean background {m.Background:P0}");
        }
        if (exhausted) Ledger!.Add("  header search budget (96 positions) was used up before a match");
    }

    private static (PickpocketVisualState State, double Score) MatchHeader(Pixels pixels, int left, int y, double scale)
    {
        var detailed = MatchHeaderDetailed(pixels, left, y, scale);
        return (detailed.State, detailed.Score);
    }

    /// <summary>Score is min(text found, clean background); the two parts say which one failed.</summary>
    private static (PickpocketVisualState State, double Score, double Foreground, double Background) MatchHeaderDetailed(Pixels pixels, int left, int y, double scale)
    {
        var best = (State: PickpocketVisualState.Uncertain, Score: 0d, Foreground: 0d, Background: 0d);
        Span<ulong> text = stackalloc ulong[HeaderWordCount];
        Span<ulong> brightText = stackalloc ulong[HeaderWordCount];
        text.Clear();
        brightText.Clear();
        for (var gy = 0; gy < 22; gy++)
        for (var gx = 0; gx < HeaderGridWidth; gx++)
        {
            var color = pixels.At(left + (int)Math.Round((gx - 2) * scale), y + (int)Math.Round((gy - 213) * scale));
            var bit = gy * HeaderGridWidth + gx;
            if (IsText(color)) text[bit / 64] |= 1UL << (bit % 64);
            if (IsTextCore(color)) brightText[bit / 64] |= 1UL << (bit % 64);
        }
        // Evaluate each threshold against BOTH the positive and negative mask.
        // The brighter threshold separates lettering from pale live scenery;
        // the original threshold still recognizes the Preparing fade-in.
        for (var threshold = 0; threshold < 2; threshold++)
        {
            var thresholdText = threshold == 0 ? text : brightText;
            foreach (var header in Headers.Value)
            {
                foreach (var mask in header.Shifts)
                {
                    var positives = 0;
                    var negatives = 0;
                    for (var word = 0; word < HeaderWordCount; word++)
                    {
                        positives += BitOperations.PopCount(thresholdText[word] & mask.Foreground[word]);
                        negatives += BitOperations.PopCount(~thresholdText[word] & mask.Background[word]);
                    }
                    var foreground = positives / (double)header.Foreground.Length;
                    var background = negatives / (double)header.Background.Length;
                    var score = Math.Min(foreground, background);
                    if (score > best.Score) best = (header.State, score, foreground, background);
                }
            }
        }
        return best;
    }

    private static HeaderMask[] LoadHeaders()
    {
        var states = new[] { PickpocketVisualState.Preparing, PickpocketVisualState.Active, PickpocketVisualState.Grabbed, PickpocketVisualState.Missed };
        return states.Select((state, index) =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CuePilot.Vision.PickpocketHeader{index + 1}.png")
                ?? throw new InvalidOperationException("Missing pickpocket header reference.");
            using var bitmap = new Bitmap(stream);
            using var pixels = new Pixels(bitmap);
            var foreground = new List<Point>();
            var background = new List<Point>();
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = pixels.At(x, y);
                if (IsText(color) && Math.Min(color.R, Math.Min(color.G, color.B)) > 200) foreground.Add(new Point(x, y));
                else if (!IsText(color) && (x + y) % 3 == 0) background.Add(new Point(x, y));
            }
            var shifts = new List<HeaderBits>();
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                ulong[] Pack(List<Point> points)
                {
                    var words = new ulong[HeaderWordCount];
                    foreach (var point in points)
                    {
                        var bit = (point.Y + dy + 2) * HeaderGridWidth + point.X + dx + 2;
                        words[bit / 64] |= 1UL << (bit % 64);
                    }
                    return words;
                }
                shifts.Add(new(Pack(foreground), Pack(background)));
            }
            return new HeaderMask(state, foreground.ToArray(), background.ToArray(), shifts.ToArray());
        }).ToArray();
    }

    private static bool IsMarker(Color c) => c.G > 150 && c.G > c.R * 1.22 && c.G > c.B * 1.45;
    private static bool IsText(Color c) => Math.Min(c.R, Math.Min(c.G, c.B)) > 145 && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < 55;
    private static bool IsTextCore(Color c) => IsText(c) && Math.Min(c.R, Math.Min(c.G, c.B)) > 200;
    private static PickpocketBandColor? BandColor(Color c)
    {
        if (IsMarker(c)) return null;
        if (c.R > 130 && c.G > 120 && c.B < Math.Min(c.R, c.G) * .55) return PickpocketBandColor.Yellow;
        if (c.B > 110 && c.G > 100 && c.B > c.R * 1.35 && c.G > c.R * 1.2) return PickpocketBandColor.Blue;
        if (c.R > 90 && c.B > 90 && c.B > c.G * 1.3) return PickpocketBandColor.Purple;
        if (c.R > 100 && c.R > c.G * 1.6 && c.R > c.B * 1.6) return PickpocketBandColor.Red;
        if (c.G > 100 && c.R > 70 && c.G > c.B * 1.15 && c.G > c.R * 1.04) return PickpocketBandColor.PaleGreen;
        if (Math.Min(c.R, Math.Min(c.G, c.B)) > 100 && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < 35) return PickpocketBandColor.White;
        return null;
    }

    private sealed record HeaderBits(ulong[] Foreground, ulong[] Background);
    private sealed record HeaderMask(PickpocketVisualState State, Point[] Foreground, Point[] Background, HeaderBits[] Shifts);
    private sealed class HeaderSearch
    {
        // Per-frame only: never reuse a header result from an older image.
        private readonly Dictionary<(int Left, int Y, double Scale), (PickpocketVisualState State, double Score)> matches = new();
        internal bool Exhausted => matches.Count >= 96;
        internal (PickpocketVisualState State, double Score) Match(Pixels pixels, int left, int y, double scale)
        {
            var key = (left, y, scale);
            if (matches.TryGetValue(key, out var cached)) return cached;
            if (Exhausted) return (PickpocketVisualState.Uncertain, 0);
            var result = MatchHeader(pixels, left, y, scale);
            matches.Add(key, result);
            return result;
        }
    }
    private sealed unsafe class Pixels : IDisposable
    {
        private readonly Bitmap bitmap;
        private readonly BitmapData data;
        internal int Width { get; }
        private int Height { get; }
        internal Pixels(Bitmap bitmap)
        {
            this.bitmap = bitmap;
            Width = bitmap.Width;
            Height = bitmap.Height;
            data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        }
        internal Color At(int x, int y)
        {
            if ((uint)x >= Width || (uint)y >= Height) return Color.Empty;
            var p = (byte*)data.Scan0 + y * data.Stride + x * 4;
            return Color.FromArgb(p[2], p[1], p[0]);
        }
        internal bool MarkerAt(int x, int y)
        {
            if ((uint)x >= Width || (uint)y >= Height) return false;
            var p = (byte*)data.Scan0 + y * data.Stride + x * 4;
            return p[1] > 150 && p[1] * 100 > p[2] * 122 && p[1] * 100 > p[0] * 145;
        }
        public void Dispose() => bitmap.UnlockBits(data);
    }
}
