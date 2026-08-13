using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WorkflowLooper;

internal enum FishingPromptKind
{
    None,
    Cast,
    Collect,
}

internal readonly record struct FishingPromptObservation(
    FishingPromptKind Kind,
    double Confidence,
    double CastConfidence = 0,
    double CollectConfidence = 0);

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
    private static readonly Lazy<PromptTemplate[]> Templates = new(LoadTemplates);

    internal static FishingPromptObservation Analyze(Bitmap bitmap)
    {
        if (bitmap.Width < 150 || bitmap.Height < 44)
            return new FishingPromptObservation(FishingPromptKind.None, 0);

        using var pixels = new PixelBuffer(bitmap);
        var usePromptRegion = pixels.Width >= 640 && pixels.Height >= 360;
        var brightSearchRegion = usePromptRegion
            ? Rectangle.FromLTRB(
                pixels.Width * 25 / 100,
                pixels.Height * 55 / 100,
                Math.Min(pixels.Width, pixels.Width * 70 / 100 + 48),
                pixels.Height)
            : new Rectangle(0, 0, pixels.Width, pixels.Height);
        var brightPoints = pixels.FindNeutralPoints(200, 35, brightSearchRegion);
        var templates = Templates.Value;
        var scores = new double[templates.Length];
        Parallel.For(0, templates.Length, index =>
        {
            scores[index] = FindBestScore(pixels, templates[index], brightPoints);
        });
        var castScore = templates.Select((template, index) => (template, index))
            .Where(item => item.template.Kind == FishingPromptKind.Cast)
            .Max(item => scores[item.index]);
        var collectScore = templates.Select((template, index) => (template, index))
            .Where(item => item.template.Kind == FishingPromptKind.Collect)
            .Max(item => scores[item.index]);

        var kind = castScore >= collectScore ? FishingPromptKind.Cast : FishingPromptKind.Collect;
        var best = Math.Max(castScore, collectScore);
        var other = Math.Min(castScore, collectScore);
        if (best < 0.65 || best - other < 0.012)
            return new FishingPromptObservation(FishingPromptKind.None, best, castScore, collectScore);
        return new FishingPromptObservation(kind, best, castScore, collectScore);
    }

    internal static FishingPromptObservation Observe(
        IFrameSource frameSource,
        WindowTargetSettings target,
        out FrameSourceStatus status)
    {
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            status = new FrameSourceStatus(FrameSourceState.TargetUnavailable, frameSource.Name, detail, TimeSpan.MaxValue, 0);
            return new FishingPromptObservation(FishingPromptKind.None, 0);
        }

        var region = new Rectangle(Point.Empty, resolved.Bounds.Size);
        if (!frameSource.TryCapture(target, region, out var frame, out status) || frame is null)
            return new FishingPromptObservation(FishingPromptKind.None, 0);
        using (frame)
        {
            return Analyze(frame.Bitmap);
        }
    }

    private static double FindBestScore(PixelBuffer source, PromptTemplate template, IReadOnlyList<Point> brightPoints)
    {
        if (template.Width > source.Width || template.Height > source.Height) return 0;
        var usePromptRegion = source.Width >= 640 && source.Height >= 360;
        var minimumX = usePromptRegion ? source.Width * 25 / 100 : 0;
        var maximumX = usePromptRegion ? source.Width * 70 / 100 : source.Width;
        var minimumY = usePromptRegion ? source.Height * 55 / 100 : 0;
        var best = 0d;
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
                if (misses > 2) break;
            }

            if (misses > 2) continue;
            var score = ScoreAt(source, template, x, y);
            if (score > best) best = score;
            if (best >= 0.985) return best;
        }

        return best;
    }

    private static double ScoreAt(PixelBuffer source, PromptTemplate template, int x, int y)
    {
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

        var backgroundMatches = 0;
        foreach (var point in template.TextBackground)
        {
            if (!source.IsNeutralAt(x + point.X, y + point.Y, 105, 70)) backgroundMatches++;
        }

        var keyScore = keyMatches / (double)template.KeyForeground.Length;
        var textScore = textMatches / (double)template.TextForeground.Length;
        var backgroundScore = template.TextBackground.Length == 0
            ? 1
            : backgroundMatches / (double)template.TextBackground.Length;
        return keyScore * 0.20 + textScore * 0.40 + backgroundScore * 0.40;
    }

    private static PromptTemplate[] LoadTemplates()
    {
        var references = new[]
        {
            LoadTemplate("WorkflowLooper.Vision.CastReady.png", FishingPromptKind.Cast, new Rectangle(37, 39, 210, 44)),
            LoadTemplate("WorkflowLooper.Vision.CollectReady.png", FishingPromptKind.Collect, new Rectangle(240, 12, 146, 44)),
            LoadTemplate("WorkflowLooper.Vision.CatchCard.png", FishingPromptKind.Collect, new Rectangle(354, 126, 150, 44)),
        };

        // FiveM's NUI can render at very different effective sizes after a
        // resolution, DPI, or UI-scale change. Keep the pyramid bounded, but
        // cover the large prompt layout supplied from the live game as well as
        // the original compact captures.
        var scales = new[] { 0.85, 1.0, 1.15, 1.5, 2.0, 3.0, 4.0 };
        return references.SelectMany(reference => scales.Select(scale => Scale(reference, scale))).ToArray();
    }

    private static PromptTemplate Scale(PromptTemplate source, double scale)
    {
        if (scale == 1) return source;

        Point ScalePoint(Point point) => new(
            (int)Math.Round(point.X * scale),
            (int)Math.Round(point.Y * scale));
        Point[] ScalePoints(IEnumerable<Point> points) => points.Select(ScalePoint).Distinct().ToArray();

        return new PromptTemplate(
            source.Kind,
            (int)Math.Round(source.Width * scale),
            (int)Math.Round(source.Height * scale),
            ScalePoints(source.KeyForeground),
            ScalePoints(source.TextForeground),
            ScalePoints(source.TextBackground),
            ScalePoints(source.KeyAnchors),
            ScalePoint(source.SearchAnchor));
    }

    private static PromptTemplate LoadTemplate(string resourceName, FishingPromptKind kind, Rectangle crop)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing fishing prompt reference {resourceName}.");
        using var reference = new Bitmap(stream);
        crop = Rectangle.Intersect(new Rectangle(Point.Empty, reference.Size), crop);
        var keyForeground = new List<Point>();
        var strongGlyph = new List<Point>();
        var textForeground = new List<Point>();
        var textBackground = new List<Point>();
        for (var y = 0; y < crop.Height; y += 2)
        {
            for (var x = 0; x < crop.Width; x += 2)
            {
                var color = reference.GetPixel(crop.Left + x, crop.Top + y);
                if (color.A < 32) continue;
                if (x < 48 && IsNeutral(color, 70, 60)) keyForeground.Add(new Point(x, y));
                if (x is >= 14 and <= 32 && y is >= 8 and <= 35 && IsNeutral(color, 200, 35)) strongGlyph.Add(new Point(x, y));
                if (x < 50) continue;
                if (IsNeutral(color, 125, 60)) textForeground.Add(new Point(x, y));
                else textBackground.Add(new Point(x, y));
            }
        }

        if (keyForeground.Count < 20 || textForeground.Count < 20)
            throw new InvalidOperationException($"Fishing prompt reference {resourceName} is incomplete.");
        const int anchorCount = 11;
        var anchors = Enumerable.Range(0, anchorCount)
            .Select(index => keyForeground[index * (keyForeground.Count - 1) / (anchorCount - 1)])
            .ToArray();
        if (strongGlyph.Count == 0) throw new InvalidOperationException($"Fishing prompt reference {resourceName} has no key glyph.");
        var searchAnchor = strongGlyph[strongGlyph.Count / 2];
        return new PromptTemplate(kind, crop.Width, crop.Height, keyForeground.ToArray(), textForeground.ToArray(), textBackground.ToArray(), anchors, searchAnchor);
    }

    private static bool IsNeutral(Color color, int minimumBrightness, int maximumSpread)
    {
        var brightest = Math.Max(color.R, Math.Max(color.G, color.B));
        var darkest = Math.Min(color.R, Math.Min(color.G, color.B));
        return (color.R + color.G + color.B) / 3 >= minimumBrightness && brightest - darkest <= maximumSpread;
    }

    private sealed record PromptTemplate(
        FishingPromptKind Kind,
        int Width,
        int Height,
        Point[] KeyForeground,
        Point[] TextForeground,
        Point[] TextBackground,
        Point[] KeyAnchors,
        Point SearchAnchor);

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
