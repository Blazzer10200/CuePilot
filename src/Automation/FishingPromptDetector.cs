using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CuePilot;

internal enum FishingPromptKind
{
    None,
    Cast,
    Collect,
}

// HUD state is deliberately separate from the actionable prompt kind.  The
// routine may only press E for Cast/Collect; the remaining states make the
// detector's reading explainable without adding a second input controller.
internal enum FishingHudState
{
    None,
    Ready,
    Casting,
    Waiting,
    Result,
    Decision,
}

internal enum FishingHudSignal
{
    None,
    Cast,
    Stop,
    Release,
    Keep,
    CatchKeep,
}

internal readonly record struct FishingPromptObservation(
    FishingPromptKind Kind,
    double Confidence,
    double CastConfidence = 0,
    double CollectConfidence = 0,
    FishingHudState State = FishingHudState.None,
    double StateConfidence = 0);

internal readonly record struct FishingHudObservation(
    FishingHudState State,
    double Confidence,
    FishingPromptObservation Prompt);

internal readonly record struct FishingPromptMatchEvidence(
    FishingPromptKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    double Score,
    double KeyScore,
    double GlyphScore,
    double TextScore,
    double ContrastScore,
    double BackgroundScore,
    FishingHudSignal Signal = FishingHudSignal.None);

internal readonly record struct FishingPromptEvidence(
    FishingPromptMatchEvidence Cast,
    FishingPromptMatchEvidence Collect,
    string DecisionReason,
    FishingHudState State = FishingHudState.None,
    double StateConfidence = 0,
    double StopScore = 0,
    double ReleaseScore = 0,
    double KeepScore = 0,
    double CatchKeepScore = 0);

internal sealed class FishingPromptStabilityGate(
    FishingPromptKind expected,
    int requiredMatches = 2,
    int toleratedMisses = 1)
{
    private int matches;
    private int misses;

    internal bool Observe(FishingPromptObservation observation)
    {
        if (observation.Kind == expected)
        {
            matches++;
            misses = 0;
            return matches >= requiredMatches;
        }

        if (observation.Kind == FishingPromptKind.None && matches > 0 && misses < toleratedMisses)
        {
            misses++;
            return false;
        }

        matches = 0;
        misses = 0;
        return false;
    }

    internal void Reset()
    {
        matches = 0;
        misses = 0;
    }
}

internal static class FishingPromptArbitration
{
    private const double ReadyStateOverrideGate = 0.90;

    internal static bool ShouldSuppress(
        FishingPromptObservation prompt,
        FishingMeterObservation meter) =>
        // An ambiguous Cast match must never interrupt a live tension meter. A strongly
        // classified Ready HUD is different: it is direct evidence that the minigame has
        // ended and it must outrank stale meter geometry retained from the previous fish.
        prompt.Kind == FishingPromptKind.Cast &&
        meter.IsVisible &&
        (prompt.State != FishingHudState.Ready || prompt.StateConfidence < ReadyStateOverrideGate);
}

internal sealed class FishingPromptClearGate(FishingPromptKind pressed, int requiredMissingSamples = 3)
{
    private int missingSamples;

    internal bool Observe(FishingPromptObservation observation)
    {
        if (observation.Kind == pressed)
        {
            missingSamples = 0;
            return false;
        }

        if (observation.Kind != FishingPromptKind.None)
        {
            return true;
        }

        missingSamples++;
        return missingSamples >= requiredMissingSamples;
    }
}

internal static class FishingPromptDetector
{
    private const double MinimumGlyphScore = 0.95;
    private const double ActionGate = 0.65;
    private const double StateGate = 0.70;
    private static readonly Lazy<PromptTemplate[]> Templates = new(LoadTemplates);

    internal static FishingPromptObservation Analyze(Bitmap bitmap) => Analyze(bitmap, out _);

    internal static FishingPromptObservation Analyze(Bitmap bitmap, out FishingPromptEvidence evidence)
    {
        evidence = default;
        if (bitmap.Width < 150 || bitmap.Height < 44)
            return new FishingPromptObservation(FishingPromptKind.None, 0);

        using var pixels = new PixelBuffer(bitmap);
        var usePromptRegion = pixels.Width >= 640 && pixels.Height >= 360;
        var frameBounds = new Rectangle(0, 0, pixels.Width, pixels.Height);
        var brightSearchRegion = usePromptRegion
            ? GameViewportGeometry.AdaptiveHudSearchRegion(frameBounds, 0.25, 0.55, 0.88, 1)
            : new Rectangle(0, 0, pixels.Width, pixels.Height);
        var brightPoints = pixels.FindNeutralPoints(200, 35, brightSearchRegion);
        var templates = Templates.Value;
        var matches = new PromptMatch[templates.Length];
        Parallel.For(0, templates.Length, index =>
        {
            matches[index] = FindBestMatch(pixels, templates[index], brightPoints);
        });
        // This runs for every captured frame. Keep the selection allocation-free:
        // the templates are fixed and ordered by kind, so a direct pass preserves
        // MaxBy's first-match tie behavior without constructing LINQ iterators.
        var castIndex = -1;
        var keepIndex = -1;
        var catchKeepIndex = -1;
        var stopIndex = -1;
        var releaseIndex = -1;
        for (var index = 0; index < templates.Length; index++)
        {
            switch (templates[index].Signal)
            {
                case FishingHudSignal.Cast when castIndex < 0 || matches[index].Score.Total > matches[castIndex].Score.Total:
                    castIndex = index;
                    break;
                case FishingHudSignal.Keep when keepIndex < 0 || matches[index].Score.Total > matches[keepIndex].Score.Total:
                    keepIndex = index;
                    break;
                case FishingHudSignal.CatchKeep when catchKeepIndex < 0 || matches[index].Score.Total > matches[catchKeepIndex].Score.Total:
                    catchKeepIndex = index;
                    break;
                case FishingHudSignal.Stop when stopIndex < 0 || matches[index].Score.Total > matches[stopIndex].Score.Total:
                    stopIndex = index;
                    break;
                case FishingHudSignal.Release when releaseIndex < 0 || matches[index].Score.Total > matches[releaseIndex].Score.Total:
                    releaseIndex = index;
                    break;
            }
        }

        if (castIndex < 0 || keepIndex < 0 || catchKeepIndex < 0 || stopIndex < 0 || releaseIndex < 0)
        {
            throw new InvalidOperationException("Fishing prompt templates must include all expected HUD signals.");
        }

        var castScore = matches[castIndex].Score.Total;
        var keepScore = matches[keepIndex].Score.Total;
        var catchKeepScore = matches[catchKeepIndex].Score.Total;
        var collectIndex = keepScore >= catchKeepScore ? keepIndex : catchKeepIndex;
        var collectScore = Math.Max(keepScore, catchKeepScore);
        var stopScore = matches[stopIndex].Score.Total;
        var releaseScore = matches[releaseIndex].Score.Total;
        var kind = castScore >= collectScore ? FishingPromptKind.Cast : FishingPromptKind.Collect;
        var best = Math.Max(castScore, collectScore);
        var other = Math.Min(castScore, collectScore);
        var (hudState, hudConfidence) = ClassifyHudState(
            pixels,
            matches[castIndex], matches[stopIndex], matches[releaseIndex], matches[keepIndex], matches[catchKeepIndex]);
        var catchDividerScore = MeasureCatchCardDivider(pixels, matches[catchKeepIndex]);
        var decisionReason = best < ActionGate
            ? $"Best prompt score {best:P0} is below the {ActionGate:P0} gate"
            : best - other < 0.012
                ? $"Cast and collect scores are separated by only {best - other:P1}"
                : $"Accepted {kind} at {best:P0}; HUD {hudState} {hudConfidence:P0}; catch-divider {catchDividerScore:P0}";
        evidence = new FishingPromptEvidence(
            ToEvidence(templates[castIndex], matches[castIndex]),
            ToEvidence(templates[collectIndex], matches[collectIndex]),
            decisionReason,
            hudState,
            hudConfidence,
            stopScore,
            releaseScore,
            keepScore,
            catchKeepScore);
        if (best < ActionGate || best - other < 0.012)
            return new FishingPromptObservation(FishingPromptKind.None, best, castScore, collectScore, hudState, hudConfidence);
        return new FishingPromptObservation(kind, best, castScore, collectScore, hudState, hudConfidence);
    }

    // This is a diagnostic/state surface only.  It deliberately reuses the
    // production meter detector for the LMB-backed casting state instead of
    // introducing a second, less strict LMB matcher here.
    internal static FishingHudObservation AnalyzeHudState(Bitmap bitmap, out FishingPromptEvidence evidence)
    {
        var prompt = Analyze(bitmap, out evidence);
        if (prompt.State != FishingHudState.None)
            return new FishingHudObservation(prompt.State, prompt.StateConfidence, prompt);

        var meter = FishingMeterService.AnalyzeFrameDetailed(bitmap);
        if (meter.Observation.IsVisible)
            return new FishingHudObservation(FishingHudState.Casting, meter.Observation.Confidence, prompt);

        return new FishingHudObservation(FishingHudState.None, Math.Max(prompt.Confidence, meter.Observation.Confidence), prompt);
    }

    internal static FishingPromptFrameSample? CaptureAndAnalyze(
        IFrameSource frameSource,
        WindowTargetSettings target,
        out FrameSourceStatus status)
    {
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            status = new FrameSourceStatus(FrameSourceState.TargetUnavailable, frameSource.Name, detail, TimeSpan.MaxValue, 0);
            return null;
        }

        var region = new Rectangle(Point.Empty, resolved.Bounds.Size);
        if (!frameSource.TryCapture(target, region, out var frame, out status) || frame is null)
            return null;
        try
        {
            var observation = Analyze(frame.Bitmap, out var evidence);
            return new FishingPromptFrameSample(frame, observation, evidence);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    private static PromptMatch FindBestMatch(PixelBuffer source, PromptTemplate template, IReadOnlyList<Point> brightPoints)
    {
        if (template.Width > source.Width || template.Height > source.Height) return default;
        var usePromptRegion = source.Width >= 640 && source.Height >= 360;
        var sourceBounds = new Rectangle(0, 0, source.Width, source.Height);
        var promptRegion = usePromptRegion
            ? GameViewportGeometry.AdaptiveHudSearchRegion(sourceBounds, 0.25, 0.55, 0.88, 1)
            : sourceBounds;
        var minimumX = promptRegion.Left;
        var maximumX = promptRegion.Right;
        var minimumY = promptRegion.Top;
        var best = default(PromptMatch);
        foreach (var brightPoint in brightPoints)
        {
            var x = brightPoint.X - template.SearchAnchor.X;
            var y = brightPoint.Y - template.SearchAnchor.Y;
            if (x < minimumX || x > maximumX || y < minimumY || x + template.Width > source.Width || y + template.Height > source.Height) continue;
            var misses = 0;
            foreach (var point in template.KeyAnchors)
            {
                if (source.IsNeutralAt(x + point.X, y + point.Y, 70, 60)) continue;
                misses++;
                if (misses > 5) break;
            }

            // The key outline is anti-aliased differently at fractional UI
            // scales. Six of eleven anchors still establishes the keycap while
            // allowing the full glyph/text mask to make the final decision.
            if (misses > 5) continue;
            var score = ScoreAt(source, template, x, y);
            if (score.Total > best.Score.Total) best = new PromptMatch(x, y, score, template.Width, template.Height);
            if (best.Score.Total >= 0.985) return best;
        }

        return best;
    }

    private static PromptScore ScoreAt(PixelBuffer source, PromptTemplate template, int x, int y)
    {
        var glyphForegroundMatches = 0;
        foreach (var point in template.GlyphForeground)
        {
            if (source.IsNeutralAt(x + point.X, y + point.Y, 125, 60)) glyphForegroundMatches++;
        }

        var glyphBackgroundMatches = 0;
        foreach (var point in template.GlyphBackground)
        {
            if (source.IsDarkAt(x + point.X, y + point.Y, 110)) glyphBackgroundMatches++;
        }

        var glyphForegroundScore = template.GlyphForeground.Length == 0
            ? 0
            : glyphForegroundMatches / (double)template.GlyphForeground.Length;
        var glyphBackgroundScore = template.GlyphBackground.Length == 0
            ? 0
            : glyphBackgroundMatches / (double)template.GlyphBackground.Length;
        var glyphScore = glyphForegroundScore * 0.70 + glyphBackgroundScore * 0.30;
        if (glyphScore < MinimumGlyphScore)
        {
            return new PromptScore(0, 0, glyphScore, 0, 0, 0);
        }

        var keyMatches = 0;
        foreach (var point in template.KeyForeground)
        {
            if (source.IsNeutralAt(x + point.X, y + point.Y, 70, 60)) keyMatches++;
        }

        var textMatches = 0;
        foreach (var point in template.TextForeground)
        {
            if (source.IsNeutralAt(x + point.X, y + point.Y, 125, 60)) textMatches++;
        }

        var contrastMatches = 0;
        foreach (var pair in template.TextContrastPairs)
        {
            var foreground = source.LuminanceAt(x + pair.Foreground.X, y + pair.Foreground.Y);
            var background = source.LuminanceAt(x + pair.Background.X, y + pair.Background.Y);
            if (foreground - background >= 12) contrastMatches++;
        }

        var backgroundMatches = 0;
        foreach (var point in template.TextBackground)
        {
            if (!source.IsNeutralAt(x + point.X, y + point.Y, 105, 70)) backgroundMatches++;
        }

        var keyScore = keyMatches / (double)template.KeyForeground.Length;
        var textScore = textMatches / (double)template.TextForeground.Length;
        var contrastScore = template.TextContrastPairs.Length == 0
            ? 0
            : contrastMatches / (double)template.TextContrastPairs.Length;
        var backgroundScore = template.TextBackground.Length == 0
            ? 1
            : backgroundMatches / (double)template.TextBackground.Length;
        // Text foreground alone is ambiguous over sand, foam, or sky: a bright
        // scene can satisfy every white-text sample for both prompt templates.
        // The GTA prompt font has a stable dark outline/shadow immediately
        // beside each white stroke. Scoring those signed local-contrast pairs
        // preserves the letter geometry without requiring a dark world behind it.
        var total = keyScore * 0.15
            + glyphScore * 0.10
            + textScore * 0.20
            + contrastScore * 0.40
            + backgroundScore * 0.15;
        return new PromptScore(total, keyScore, glyphScore, textScore, contrastScore, backgroundScore);
    }

    private static FishingPromptMatchEvidence ToEvidence(PromptTemplate template, PromptMatch match) => new(
        template.Kind,
        match.X,
        match.Y,
        template.Width,
        template.Height,
        match.Score.Total,
        match.Score.Key,
        match.Score.Glyph,
        match.Score.Text,
        match.Score.Contrast,
        match.Score.Background,
        template.Signal);

    private static (FishingHudState State, double Confidence) ClassifyHudState(
        PixelBuffer pixels,
        PromptMatch cast,
        PromptMatch stop,
        PromptMatch release,
        PromptMatch keep,
        PromptMatch catchKeep)
    {
        var ready = CombineSignals(cast, stop, expectedRightSignal: true);
        var decision = CombineSignals(release, keep, expectedRightSignal: true);
        var catchDividerScore = MeasureCatchCardDivider(pixels, catchKeep);
        var result = CombineSignals(release, catchKeep, expectedRightSignal: true);

        // The catch-card Keep prompt is an independent, lower-left ROI.  When
        // it lines up with Release Fish, that is stronger evidence of a result
        // card than a bare decision row and does not depend on fish species,
        // weight, or the world pixels behind the translucent card.
        if (result >= StateGate && catchDividerScore >= 0.27)
            return (FishingHudState.Result, result);
        if (decision >= StateGate)
            return (FishingHudState.Decision, decision);
        if (ready >= StateGate)
            return (FishingHudState.Ready, ready);

        // Waiting is only meaningful when the stop prompt is independently
        // strong and Cast Fishing Line is absent; otherwise Ready wins.
        if (stop.Score.Total >= ActionGate && cast.Score.Total < 0.45)
            return (FishingHudState.Waiting, stop.Score.Total);
        return (FishingHudState.None, Math.Max(Math.Max(ready, decision), result));
    }

    private static double MeasureCatchCardDivider(PixelBuffer pixels, PromptMatch catchKeep)
    {
        // The fish name/weight varies, but a thin neutral divider directly
        // above the Release/Keep row is fixed across catch cards.  Sampling
        // that edge avoids a brittle fish-card screenshot template and keeps
        // a bare decision row from being reported as a result panel.
        if (catchKeep.Width <= 0 || catchKeep.Height <= 0
            || catchKeep.Y < catchKeep.Height * 0.55)
            return 0;

        var dividerY = catchKeep.Y - Math.Max(3, (int)Math.Round(catchKeep.Height * 0.18));
        var left = Math.Max(0, catchKeep.X - (int)Math.Round(catchKeep.Width * 1.45));
        var right = Math.Min(pixels.Width - 1, catchKeep.X + (int)Math.Round(catchKeep.Width * 1.45));
        if (dividerY < 0 || left >= right) return 0;

        var matches = 0;
        var samples = 0;
        for (var x = left; x <= right; x += Math.Max(1, catchKeep.Width / 60))
        {
            samples++;
            var divider = pixels.LuminanceAt(x, dividerY);
            var above = pixels.LuminanceAt(x, Math.Max(0, dividerY - 3));
            var below = pixels.LuminanceAt(x, Math.Min(pixels.Height - 1, dividerY + 3));
            if (pixels.IsNeutralAt(x, dividerY, 90, 75)
                && divider - (above + below) / 2 >= 30)
            {
                matches++;
            }
        }

        return samples == 0 ? 0 : matches / (double)samples;
    }

    private static double CombineSignals(PromptMatch left, PromptMatch right, bool expectedRightSignal)
    {
        if (left.Score.Total <= 0 || right.Score.Total <= 0)
            return 0;

        var sameRow = Math.Abs(left.Y - right.Y) <= Math.Max(left.Height, right.Height) * 0.55;
        var expectedOrder = !expectedRightSignal || right.X > left.X + left.Width * 0.20;
        var layoutScore = sameRow && expectedOrder ? 1d : 0d;
        return left.Score.Total * 0.45 + right.Score.Total * 0.45 + layoutScore * 0.10;
    }

    private static PromptTemplate[] LoadTemplates()
    {
        var references = new[]
        {
            LoadTemplate("CuePilot.Vision.CastReady.png", FishingPromptKind.Cast, FishingHudSignal.Cast, new Rectangle(37, 39, 210, 44)),
            LoadTemplate("CuePilot.Vision.CastReady.png", FishingPromptKind.None, FishingHudSignal.Stop, new Rectangle(270, 39, 170, 44)),
            LoadTemplate("CuePilot.Vision.CollectReady.png", FishingPromptKind.None, FishingHudSignal.Release, new Rectangle(25, 12, 175, 44)),
            LoadTemplate("CuePilot.Vision.CollectReady.png", FishingPromptKind.Collect, FishingHudSignal.Keep, new Rectangle(240, 12, 146, 44)),
            LoadTemplate("CuePilot.Vision.CatchCard.png", FishingPromptKind.Collect, FishingHudSignal.CatchKeep, new Rectangle(354, 126, 150, 44)),
        };

        // FiveM's NUI can render at very different effective sizes after a
        // resolution, DPI, or UI-scale change. Keep the pyramid bounded, but
        // cover the large prompt layout supplied from the live game as well as
        // the original compact captures.
        // Point-sampled prompt glyphs are sensitive to even a two-pixel size
        // change. Probe a dense small/normal UI pyramid, then retain a few
        // bounded large-layout levels for the supplied catch-card variants.
        var scales = Enumerable.Range(0, 18)
            .Select(index => 0.65 + index * 0.05)
            .Concat(new[] { 1.75, 2.0, 2.5, 3.0, 3.5, 4.0 })
            .ToArray();
        return references.SelectMany(reference => scales.Select(scale => Scale(reference, scale))).ToArray();
    }

    private static PromptTemplate Scale(PromptTemplate source, double scale)
    {
        if (scale == 1) return source;

        Point ScalePoint(Point point) => new(
            (int)Math.Round(point.X * scale),
            (int)Math.Round(point.Y * scale));
        Point[] ScalePoints(IEnumerable<Point> points) => points.Select(ScalePoint).Distinct().ToArray();
        TextContrastPair[] ScalePairs(IEnumerable<TextContrastPair> pairs) => pairs
            .Select(pair => new TextContrastPair(ScalePoint(pair.Foreground), ScalePoint(pair.Background)))
            .Distinct()
            .ToArray();

        return new PromptTemplate(
            source.Kind,
            source.Signal,
            (int)Math.Round(source.Width * scale),
            (int)Math.Round(source.Height * scale),
            ScalePoints(source.KeyForeground),
            ScalePoints(source.GlyphForeground),
            ScalePoints(source.GlyphBackground),
            ScalePoints(source.TextForeground),
            ScalePairs(source.TextContrastPairs),
            ScalePoints(source.TextBackground),
            ScalePoints(source.KeyAnchors),
            ScalePoint(source.SearchAnchor));
    }

    private static PromptTemplate LoadTemplate(
        string resourceName,
        FishingPromptKind kind,
        FishingHudSignal signal,
        Rectangle crop)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing fishing prompt reference {resourceName}.");
        using var reference = new Bitmap(stream);
        crop = Rectangle.Intersect(new Rectangle(Point.Empty, reference.Size), crop);
        var keyForeground = new List<Point>();
        var strongGlyph = new List<Point>();
        var glyphBackground = new List<Point>();
        var textForeground = new List<Point>();
        var textBackground = new List<Point>();
        for (var y = 0; y < crop.Height; y += 2)
        {
            for (var x = 0; x < crop.Width; x += 2)
            {
                var color = reference.GetPixel(crop.Left + x, crop.Top + y);
                if (color.A < 32) continue;
                if (x < 48 && IsNeutral(color, 70, 60)) keyForeground.Add(new Point(x, y));
                if (x is >= 14 and <= 32 && y is >= 8 and <= 35)
                {
                    if (IsNeutral(color, 180, 45)) strongGlyph.Add(new Point(x, y));
                    else if ((color.R + color.G + color.B) / 3 <= 95) glyphBackground.Add(new Point(x, y));
                }
                if (x < 50) continue;
                if (IsNeutral(color, 125, 60)) textForeground.Add(new Point(x, y));
                else textBackground.Add(new Point(x, y));
            }
        }

        if (keyForeground.Count < 20 || textForeground.Count < 20)
            throw new InvalidOperationException($"Fishing prompt reference {resourceName} is incomplete.");
        var textContrastPairs = CreateTextContrastPairs(reference, crop, textForeground);
        if (textContrastPairs.Length < 20)
            throw new InvalidOperationException($"Fishing prompt reference {resourceName} has insufficient text contrast.");
        const int anchorCount = 11;
        var anchors = Enumerable.Range(0, anchorCount)
            .Select(index => keyForeground[index * (keyForeground.Count - 1) / (anchorCount - 1)])
            .ToArray();
        if (strongGlyph.Count == 0) throw new InvalidOperationException($"Fishing prompt reference {resourceName} has no key glyph.");
        var searchAnchor = strongGlyph[strongGlyph.Count / 2];
        return new PromptTemplate(
            kind,
            signal,
            crop.Width,
            crop.Height,
            keyForeground.ToArray(),
            strongGlyph.ToArray(),
            glyphBackground.ToArray(),
            textForeground.ToArray(),
            textContrastPairs,
            textBackground.ToArray(),
            anchors,
            searchAnchor);
    }

    private static bool IsNeutral(Color color, int minimumBrightness, int maximumSpread)
    {
        var brightest = Math.Max(color.R, Math.Max(color.G, color.B));
        var darkest = Math.Min(color.R, Math.Min(color.G, color.B));
        return (color.R + color.G + color.B) / 3 >= minimumBrightness && brightest - darkest <= maximumSpread;
    }

    private static TextContrastPair[] CreateTextContrastPairs(
        Bitmap reference,
        Rectangle crop,
        IReadOnlyList<Point> foregroundPoints)
    {
        var pairs = new List<TextContrastPair>();
        foreach (var foreground in foregroundPoints)
        {
            var foregroundLuminance = Luminance(reference.GetPixel(
                crop.Left + foreground.X,
                crop.Top + foreground.Y));
            TextContrastPair? best = null;
            var bestDelta = 0d;
            for (var radius = 1; radius <= 5 && best is null; radius++)
            {
                for (var offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (var offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetY)) != radius) continue;
                        var background = new Point(foreground.X + offsetX, foreground.Y + offsetY);
                        if (background.X < 50 || background.X >= crop.Width
                            || background.Y < 0 || background.Y >= crop.Height) continue;
                        var delta = foregroundLuminance - Luminance(reference.GetPixel(
                            crop.Left + background.X,
                            crop.Top + background.Y));
                        if (delta < 35 || delta <= bestDelta) continue;
                        bestDelta = delta;
                        best = new TextContrastPair(foreground, background);
                    }
                }
            }

            if (best is not null) pairs.Add(best.Value);
        }

        return pairs.Distinct().ToArray();
    }

    private static double Luminance(Color color) =>
        color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;

    private sealed record PromptTemplate(
        FishingPromptKind Kind,
        FishingHudSignal Signal,
        int Width,
        int Height,
        Point[] KeyForeground,
        Point[] GlyphForeground,
        Point[] GlyphBackground,
        Point[] TextForeground,
        TextContrastPair[] TextContrastPairs,
        Point[] TextBackground,
        Point[] KeyAnchors,
        Point SearchAnchor);

    private readonly record struct PromptScore(
        double Total,
        double Key,
        double Glyph,
        double Text,
        double Contrast,
        double Background);

    private readonly record struct TextContrastPair(Point Foreground, Point Background);

    private readonly record struct PromptMatch(int X, int Y, PromptScore Score, int Width = 0, int Height = 0);

    private sealed class PixelBuffer : IDisposable
    {
        private readonly Bitmap bitmap;
        private readonly bool ownsBitmap;
        private readonly BitmapData data;
        private readonly byte[] bytes;

        internal PixelBuffer(Bitmap source)
        {
            Width = source.Width;
            Height = source.Height;
            if (source.PixelFormat == PixelFormat.Format32bppArgb)
            {
                bitmap = source;
            }
            else
            {
                bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                ownsBitmap = true;
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawImageUnscaled(source, 0, 0);
            }

            data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            bytes = new byte[Math.Abs(data.Stride) * Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        }

        internal int Width { get; }
        internal int Height { get; }

        internal bool IsNeutralAt(int x, int y, int minimumBrightness, int maximumSpread)
        {
            var row = data.Stride >= 0 ? y : Height - 1 - y;
            var offset = row * Math.Abs(data.Stride) + x * 4;
            var blue = bytes[offset];
            var green = bytes[offset + 1];
            var red = bytes[offset + 2];
            var brightest = Math.Max(red, Math.Max(green, blue));
            var darkest = Math.Min(red, Math.Min(green, blue));
            return (red + green + blue) / 3 >= minimumBrightness && brightest - darkest <= maximumSpread;
        }

        internal bool IsDarkAt(int x, int y, int maximumBrightness)
        {
            var row = data.Stride >= 0 ? y : Height - 1 - y;
            var offset = row * Math.Abs(data.Stride) + x * 4;
            return (bytes[offset] + bytes[offset + 1] + bytes[offset + 2]) / 3 <= maximumBrightness;
        }

        internal double LuminanceAt(int x, int y)
        {
            var row = data.Stride >= 0 ? y : Height - 1 - y;
            var offset = row * Math.Abs(data.Stride) + x * 4;
            return bytes[offset + 2] * 0.2126 + bytes[offset + 1] * 0.7152 + bytes[offset] * 0.0722;
        }

        internal List<Point> FindNeutralPoints(int minimumBrightness, int maximumSpread, Rectangle region)
        {
            region = Rectangle.Intersect(new Rectangle(0, 0, Width, Height), region);
            var points = new List<Point>();
            for (var y = region.Top; y < region.Bottom; y++)
            {
                for (var x = region.Left; x < region.Right; x++)
                {
                    if (IsNeutralAt(x, y, minimumBrightness, maximumSpread)) points.Add(new Point(x, y));
                }
            }

            return points;
        }

        public void Dispose()
        {
            bitmap.UnlockBits(data);
            if (ownsBitmap) bitmap.Dispose();
        }
    }
}

internal sealed class FishingPromptFrameSample(
    FrameLease frame,
    FishingPromptObservation observation,
    FishingPromptEvidence evidence) : IDisposable
{
    internal FrameLease Frame { get; } = frame;
    internal FishingPromptObservation Observation { get; } = observation;
    internal FishingPromptEvidence Evidence { get; } = evidence;
    public void Dispose() => Frame.Dispose();
}
