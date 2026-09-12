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
        var best = ScanStems(pixels, bounds, previous, 36) ?? ScanStems(pixels, bounds, previous, 3);
        return StabilizeMarkerOcclusion(best ?? PickpocketObservation.Missing, previous);
    }

    private static PickpocketObservation? ScanStems(Pixels pixels, Rectangle bounds, PickpocketObservation? previous, int maximumGap)
    {
        PickpocketObservation? best = null;
        var searchWork = new HeaderSearch();
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
        // Keep the occluded-marker path first. If scenery joins it into a long
        // green column, recover with short gaps and rank solid stems before
        // spending a separate bounded header budget. Neither path skips identity checks.
        var candidates = maximumGap == 36 ? stems.AsEnumerable()
            : stems.OrderByDescending(stem => stem.Density).ThenByDescending(stem => stem.Length);
        foreach (var stem in candidates.Take(16))
        {
            var candidate = InspectStem(pixels, stem.X, stem.Top, stem.Length, previous, searchWork);
            if (candidate is not null && (best is null || candidate.Confidence > best.Confidence)) best = candidate;
            if (searchWork.Exhausted) break;
        }
        return best;
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

    private static PickpocketObservation? InspectStem(Pixels pixels, int x, int top, int length, PickpocketObservation? previous, HeaderSearch searchWork)
    {
        var y = top + length / 2;
        if (IsMarker(pixels.At(x - 6, y)) || IsMarker(pixels.At(x + 6, y))) return null;
        // Anchor the panel to the status header and marker geometry, independently
        // of item count, ordering, spacing, and duplicate colors.
        var panel = LocatePanel(pixels, x, y, length, previous, searchWork);
        if (panel is null) return null;
        var (left, scale, state, confidence) = panel.Value;
        var width = (int)Math.Round(576 * scale);
        var right = left + width - 1;
        if (left < 0 || right >= pixels.Width) return null;
        var bar = new Rectangle(left, y - (int)Math.Round(11 * scale), width, Math.Max(1, (int)Math.Round(23 * scale)));
        // A miss tints the whole bar red. It is a result, never a giant target.
        if (state == PickpocketVisualState.Missed)
            return new PickpocketObservation(state, bar, x, [], confidence, "Missed result verified; target intervals are no longer actionable.");
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
        if (bands.Count is < 1 or > 16) return null;
        return new PickpocketObservation(state, bar, x, bands, confidence, "Status header, marker, and current target intervals verified independently.");
    }

    private static (int Left, double Scale, PickpocketVisualState State, double Confidence)? LocatePanel(
        Pixels pixels, int markerX, int y, int length, PickpocketObservation? previous, HeaderSearch searchWork)
    {
        if (previous is { State: not PickpocketVisualState.Hidden } && previous.Bar.Width > 0
            && Math.Abs(previous.Bar.Top + previous.Bar.Height / 2 - y) <= 3)
        {
            var priorScale = previous.Bar.Width / 576d;
            var match = searchWork.Match(pixels, previous.Bar.Left, y, priorScale);
            if (match.Score >= .85 && markerX >= previous.Bar.Left - 3 && markerX <= previous.Bar.Right + 3)
                return (previous.Bar.Left, priorScale, match.State, match.Score);
        }
        var estimate = Math.Round(length / 45d * 40) / 40;
        var scales = new[] { estimate, estimate - .025, estimate + .025, estimate - .05, estimate + .05 }.Where(s => s >= .5).ToArray();
        foreach (var scale in scales)
        {
            var left = (int)Math.Round((pixels.Width - 576 * scale) / 2);
            if (markerX < left - 3 || markerX > left + 576 * scale + 3) continue;
            var match = searchWork.Match(pixels, left, y, scale);
            if (match.Score >= .85) return (left, scale, match.State, match.Score);
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
                if (match.Score >= .80) return (left, scale, match.State, match.Score);
            }
        }
        return null;
    }

    private static (PickpocketVisualState State, double Score) MatchHeader(Pixels pixels, int left, int y, double scale)
    {
        var best = (State: PickpocketVisualState.Uncertain, Score: 0d);
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
                    if (score > best.Score) best = (header.State, score);
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
            var p = (byte*)data.Scan0 + y * data.Stride + x * 4;
            return p[1] > 150 && p[1] * 100 > p[2] * 122 && p[1] * 100 > p[0] * 145;
        }
        public void Dispose() => bitmap.UnlockBits(data);
    }
}
