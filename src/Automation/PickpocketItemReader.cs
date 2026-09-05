using System.Drawing;
using System.Reflection;
using System.Text.Json;

namespace CuePilot;

/// <summary>Recognizes only recorded card labels, normalized to detected bar scale. Unknown labels stay unknown.</summary>
internal sealed class PickpocketItemReader
{
    internal static readonly IReadOnlyDictionary<string, PickpocketBandColor> Items = new Dictionary<string, PickpocketBandColor>
    {
        ["Lucky Charm"] = PickpocketBandColor.Yellow, ["TNT Recipe"] = PickpocketBandColor.Yellow,
        ["Ruby"] = PickpocketBandColor.Red, ["Luxury Watch"] = PickpocketBandColor.Purple, ["Ring"] = PickpocketBandColor.Purple,
        ["Cuff Medicine"] = PickpocketBandColor.Blue, ["Pocket Watch"] = PickpocketBandColor.Blue,
        ["Loose Change"] = PickpocketBandColor.White, ["Rope"] = PickpocketBandColor.White, ["Wrist Band"] = PickpocketBandColor.White,
        ["Broken Electronic Part"] = PickpocketBandColor.PaleGreen, ["Broken Hard Drive"] = PickpocketBandColor.PaleGreen
    };
    private sealed record Template(string Name, string Mask);
    private static readonly Lazy<(string Name, byte[] Mask)[]> Templates = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CuePilot.Vision.PickpocketItems.json")!;
        return JsonSerializer.Deserialize<Template[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .Select(t => (t.Name, Convert.FromBase64String(t.Mask))).ToArray();
    });
    private List<(PickpocketBand Band, string? Candidate, int Count)> known = [];
    private Rectangle bar;
    private double nextRead;
    private double firstRead = double.NaN;
    internal bool ReadyToChoose { get; private set; }
    internal PickpocketItemReader() { _ = Templates.Value; }

    internal PickpocketObservation Annotate(Bitmap frame, PickpocketObservation observation, double presentation)
    {
        var tolerance = 3 * observation.Bar.Width / 576d;
        var samePanel = bar.Width > 0 && Math.Abs(observation.Bar.Left - bar.Left) <= tolerance
            && Math.Abs(observation.Bar.Top - bar.Top) <= tolerance && Math.Abs(observation.Bar.Width - bar.Width) <= tolerance;
        if (observation.State is PickpocketVisualState.Preparing or PickpocketVisualState.Hidden || !samePanel)
        { known.Clear(); nextRead = 0; firstRead = double.NaN; ReadyToChoose = false; bar = observation.Bar; }
        if (observation.State != PickpocketVisualState.Active) return observation;
        if (double.IsNaN(firstRead)) firstRead = presentation;
        var read = presentation >= nextRead && presentation - firstRead <= 260;
        if (read) nextRead = presentation + 120;
        var updated = new List<(PickpocketBand Band, string? Candidate, int Count)>();
        var bands = observation.Bands.Select(band =>
        {
            var prior = known.FirstOrDefault(k => k.Band.Color == band.Color && Math.Abs(k.Band.Center - band.Center) < 5 * bar.Width / 576d);
            var name = read && prior.Count < 2 ? Recognize(frame, observation.Bar, band) : prior.Candidate;
            var count = name is null ? 0 : name == prior.Candidate ? prior.Count + (read ? 1 : 0) : 1;
            updated.Add((band, name, count));
            return band with { ItemName = count >= 2 ? name : null };
        }).ToArray();
        foreach (var entry in updated)
        {
            known.RemoveAll(k => k.Band.Color == entry.Band.Color && Math.Abs(k.Band.Center - entry.Band.Center) < 5 * bar.Width / 576d);
            known.Add(entry);
        }
        if (known.Count > 32) known = known.TakeLast(32).ToList();
        ReadyToChoose = presentation - firstRead >= 260 || updated.All(k => k.Count >= 2);
        return observation with { Bands = bands };
    }

    internal static string? Recognize(Bitmap frame, Rectangle bar, PickpocketBand band)
    {
        var scale = bar.Width / 576d;
        if (scale < .6 || scale > 3 || band.Center - 54 * scale < 0 || band.Center + 54 * scale >= frame.Width
            || bar.Top - 80 * scale < 0 || bar.Top - 45 * scale >= frame.Height) return null;
        var mask = new byte[104 * 30];
        for (var y = 0; y < 30; y++) for (var x = 0; x < 104; x++)
        {
            var c = frame.GetPixel((int)Math.Round(band.Center + (x - 52) * scale), (int)Math.Round(bar.Top + (y - 78) * scale));
            var min = Math.Min(c.R, Math.Min(c.G, c.B));
            if (min >= 170 && Math.Max(c.R, Math.Max(c.G, c.B)) - min <= 40) mask[y * 104 + x] = 1;
        }
        var count = mask.Count(value => value != 0);
        if (count < 30 || count > 1500) return null;
        var scores = Templates.Value.Where(t => Items[t.Name] == band.Color)
            .Select(t => (t.Name, Score: Score(mask, count, t.Mask))).GroupBy(t => t.Name)
            .Select(g => (Name: g.Key, Score: g.Max(t => t.Score))).OrderByDescending(t => t.Score).ToArray();
        return scores.Length > 0 && scores[0].Score >= .72 && (scores.Length == 1 || scores[0].Score - scores[1].Score >= .10)
            ? scores[0].Name : null;
    }

    private static double Score(byte[] actual, int count, byte[] template)
    {
        var templateCount = template.Count(value => value != 0);
        var best = 0d;
        for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++)
        {
            var overlap = 0;
            for (var y = 2; y < 28; y++) for (var x = 2; x < 102; x++)
                overlap += actual[y * 104 + x] & template[(y + dy) * 104 + x + dx];
            best = Math.Max(best, 2d * overlap / (count + templateCount));
        }
        return best;
    }
    internal static int Rank(string? name, IReadOnlyList<string>? order)
    {
        if (name is null || order is null) return int.MaxValue;
        for (var i = 0; i < order.Count; i++) if (order[i] == name) return i;
        return int.MaxValue;
    }
}
