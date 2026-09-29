using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CuePilot;

/// <summary>
/// Turns one Pickpocket evidence session into a plain verdict: what the detector saw over time,
/// which frames it misread and why, and what stopped the shot. Read-only and safe on a session
/// that is still recording (the trace is opened with shared access, and Windows reports a stale
/// size for a file being appended, so trust the contents, not the listing).
/// </summary>
internal static partial class PickpocketSessionAnalyzer
{
    internal sealed record Sample(int Index, double Ms, string Engine, string Visual, string VisualReason, double Confidence, IReadOnlyList<PickpocketBand> BandList,
        double MarkerX, Rectangle Bar, double IntervalMs, string Detail, string PredictionReason, int Presses, int ManualSpaces, string InputMode = "")
    {
        internal int Bands => BandList.Count;
    }

    /// <param name="Warm">Read with the live tracking history (the previous Active observation) handed in, as the engine does.</param>
    /// <param name="SearchMissed">Cold read said Hidden although the header is readable at the panel's real position.</param>
    /// <param name="HeaderFailed">Cold read said Hidden and the header really is unreadable there.</param>
    internal sealed record FrameAudit(string File, int Sample, string Live, string Cold, string Warm, PickpocketDetector.PanelProbe? Probe,
        IReadOnlyList<string> Ledger, IReadOnlyList<string> WarmLedger, bool BudgetExhausted, bool? MarkerExamined, bool SearchMissed, bool HeaderFailed, double ColdMs = 0);

    internal sealed record Finding(bool Problem, string Title, string Detail);

    [GeneratedRegex(@"stem x=(\d+)")]
    private static partial Regex StemLine();

    internal static int Run(string target, bool frames)
    {
        var directory = Resolve(target);
        if (directory is null) { Console.Error.WriteLine($"PICKPOCKET_ANALYZE_FAILED no session at '{target}'. Pass a session folder or 'latest'."); return 2; }
        var samples = ReadTrace(Path.Combine(directory, "trace.jsonl"));
        if (samples.Count == 0) { Console.Error.WriteLine($"PICKPOCKET_ANALYZE_FAILED {directory} has no readable trace records."); return 2; }
        Console.WriteLine($"Analyzing session folder: {directory}");
        var audits = frames ? AuditFrames(directory, samples) : [];
        var summaryPath = Path.Combine(directory, "summary.json");
        var report = Build(Path.GetFileName(directory), samples, audits, File.Exists(summaryPath), ReadRecorderState(summaryPath));
        try { File.WriteAllText(Path.Combine(directory, "ANALYSIS.md"), report); } catch (IOException) { }
        Console.WriteLine(report);
        return 0;
    }

    /// <summary>Scores today's detector on every saved frame of every session: regressions on frames the engine once read Active, and gains on frames it lost.</summary>
    internal static int RunCorpus(string? root)
    {
        root ??= Path.Combine(AppPaths.DiagnosticsDirectory, "pickpocket");
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"PICKPOCKET_CORPUS_FAILED no folder at '{root}'."); return 2; }
        int frames = 0, liveActive = 0, liveActiveNowActive = 0, liveActiveNowWarmActive = 0, liveLost = 0, liveLostRecovered = 0, panelLost = 0, panelLostCold = 0;
        var regressions = new List<string>();
        var stillMissed = new List<string>();
        var coldMisses = new List<string>();
        var falseAlarms = new List<string>();
        var times = new List<double>();
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(d => d))
        {
            if (!File.Exists(Path.Combine(directory, "trace.jsonl")) || !File.Exists(Path.Combine(directory, "frames.jsonl"))) continue;
            if (Directory.EnumerateFiles(directory, "*.png").Skip(14).Any() is false) continue; // test-run stubs
            var samples = ReadTrace(Path.Combine(directory, "trace.jsonl"));
            if (samples.Count == 0) continue;
            foreach (var a in AuditFrames(directory, samples))
            {
                frames++;
                times.Add(a.ColdMs);
                var name = Path.GetFileName(directory)[..15] + "/" + a.File;
                var nowActive = a.Cold is "Active" or "Grabbed" or "Missed" or "Preparing";
                if (a.Live == "Active")
                {
                    liveActive++;
                    if (a.Cold == "Active") liveActiveNowActive++;
                    if (a.Cold == "Active" || a.Warm == "Active") liveActiveNowWarmActive++;
                    else regressions.Add($"{name} live=Active cold={a.Cold} warm={a.Warm}");
                }
                else if (a.Live == "Hidden" && a.Probe is { MarkerFound: true, Bands.Count: >= 1 and <= 16, HeaderScore: >= .85 })
                {
                    liveLost++;
                    if (nowActive || a.Warm == "Active") liveLostRecovered++;
                    else stillMissed.Add($"{name} header={a.Probe.HeaderScore:F2} cold={a.Cold} warm={a.Warm}");
                }
                if (a.Live == "Hidden" && a.Cold != "Hidden" && !(a.Probe is { MarkerFound: true, HeaderScore: >= .85 })) falseAlarms.Add($"{name} live=Hidden cold={a.Cold} header={(a.Probe?.HeaderScore ?? 0):F2}");
                if (a.SearchMissed) { panelLost++; coldMisses.Add($"{name} header={a.Probe!.HeaderScore:F2} examined={a.MarkerExamined} exhausted={a.BudgetExhausted} warm={a.Warm}"); }
                if (a.SearchMissed && a.Warm != "Active") panelLostCold++;
            }
        }
        if (frames == 0) { Console.Error.WriteLine($"PICKPOCKET_CORPUS_FAILED no session under '{root}' had audited frames (needs trace.jsonl, frames.jsonl and more than 14 PNGs)."); return 2; }
        times.Sort();
        Console.WriteLine($"frames {frames}; cold analyze ms (with ledger): p50 {times[times.Count / 2]:F2} p99 {times[(int)(times.Count * .99)]:F2} max {times[^1]:F2}");
        Console.WriteLine($"live Active: {liveActive}; cold Active now {liveActiveNowActive}; cold-or-warm Active now {liveActiveNowWarmActive}; NOT Active now {liveActive - liveActiveNowWarmActive}");
        Console.WriteLine($"live Hidden but panel readable: {liveLost}; recovered now {liveLostRecovered}; still missed {liveLost - liveLostRecovered}");
        Console.WriteLine($"cold search misses {panelLost} (of which warm also misses {panelLostCold})");
        foreach (var r in regressions.Take(25)) Console.WriteLine("REGRESSION " + r);
        Console.WriteLine($"FALSE-POSITIVE candidates (live Hidden, no readable panel, now not Hidden): {falseAlarms.Count}");
        foreach (var r in falseAlarms.Take(20)) Console.WriteLine("FP " + r);
        foreach (var r in coldMisses.Take(40)) Console.WriteLine("COLD " + r);
        foreach (var r in stillMissed.Take(25)) Console.WriteLine("MISSED " + r);
        return 0;
    }

    private static string? Resolve(string target)
    {
        if (Directory.Exists(target)) return Path.GetFullPath(target);
        if (!target.Equals("latest", StringComparison.OrdinalIgnoreCase)) return null;
        var root = Path.Combine(AppPaths.DiagnosticsDirectory, "pickpocket");
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Where(d => File.Exists(Path.Combine(d, "trace.jsonl"))).OrderByDescending(Path.GetFileName).FirstOrDefault()
            : null;
    }

    /// <summary>The recorder's own verdict from summary.json ("Saved", "Limited", "Error"), or null when there is no readable summary.</summary>
    internal static string? ReadRecorderState(string summaryPath)
    {
        try
        {
            if (!File.Exists(summaryPath)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("debug", out var debug)
                && debug.ValueKind == JsonValueKind.Object && debug.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String ? state.GetString() : null;
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    internal static List<Sample> ReadTrace(string path)
    {
        var samples = new List<Sample>();
        if (!File.Exists(path)) return samples;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            try { if (Parse(line) is { } sample) samples.Add(sample); }
            catch (JsonException) { /* the last line of a live file can be half-written */ }
        }
        return samples;
    }

    internal static Sample? Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object) return null;
        var observation = status.TryGetProperty("observation", out var o) ? o : default;
        string Text(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        double Number(JsonElement e, string name, double fallback = 0) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
        var bar = observation.ValueKind == JsonValueKind.Object && observation.TryGetProperty("bar", out var b)
            ? new Rectangle((int)Number(b, "x"), (int)Number(b, "y"), (int)Number(b, "width"), (int)Number(b, "height")) : Rectangle.Empty;
        var bands = new List<PickpocketBand>();
        if (observation.ValueKind == JsonValueKind.Object && observation.TryGetProperty("bands", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
                if (Enum.TryParse<PickpocketBandColor>(Text(item, "color"), true, out var color))
                    bands.Add(new PickpocketBand(color, Number(item, "left"), Number(item, "right")));
        var prediction = status.TryGetProperty("prediction", out var p) ? Text(p, "reason") : "";
        return new Sample((int)Number(status, "sampleCount"), Number(status, "monotonicMilliseconds"), Text(status, "state"), Text(observation, "state"),
            Text(observation, "reason"), Number(observation, "confidence"), bands, Number(observation, "markerX", double.NaN), bar,
            Number(status, "sampleIntervalMilliseconds"), Text(status, "detail"), prediction, (int)Number(status, "automatedPressCount"), (int)Number(status, "manualSpacePressCount"), Text(status, "inputMode"));
    }

    private static List<FrameAudit> AuditFrames(string directory, List<Sample> samples)
    {
        var audits = new List<FrameAudit>();
        var path = Path.Combine(directory, "frames.jsonl");
        if (!File.Exists(path)) return audits;
        var byIndex = samples.GroupBy(s => s.Index).ToDictionary(g => g.Key, g => g.First());
        var active = samples.Where(s => s.Visual == "Active" && s.Bar.Width > 0).OrderBy(s => s.Index).ToList();
        string[] lines;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Saved PNGs are crops of the capture. The detector centres the panel on the frame and needs its
        // full width, so put each crop back at its original offset on a canvas the size of the capture.
        var full = Size.Empty;
        foreach (var l in lines)
            try
            {
                using var d = JsonDocument.Parse(l);
                var r = d.RootElement.GetProperty("imageRegion");
                // The panel is centred in the capture and the crop is taken around it, so a crop's mirror gives the capture width.
                var mirrored = (int)Math.Round(2d * r.GetProperty("x").GetInt32() + r.GetProperty("width").GetInt32());
                full = new Size(Math.Max(full.Width, Math.Max(r.GetProperty("right").GetInt32(), mirrored)), Math.Max(full.Height, r.GetProperty("bottom").GetInt32()));
            }
            catch (Exception failure) when (failure is JsonException or KeyNotFoundException or InvalidOperationException) { }
        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var file = root.GetProperty("file").GetString()!;
                var index = root.GetProperty("sampleCount").GetInt32();
                var region = root.GetProperty("imageRegion");
                var offset = new Point(region.GetProperty("x").GetInt32(), region.GetProperty("y").GetInt32());
                var imagePath = Path.Combine(directory, file);
                if (!File.Exists(imagePath)) continue;
                using var crop = new Bitmap(imagePath);
                using var bitmap = new Bitmap(Math.Max(full.Width, crop.Width + offset.X), Math.Max(full.Height, crop.Height + offset.Y));
                using (var canvas = Graphics.FromImage(bitmap)) { canvas.Clear(Color.Black); canvas.DrawImageUnscaled(crop, offset); }
                var live = byIndex.TryGetValue(index, out var liveSample) ? liveSample.Visual : "?";

                // Cold: no history, the worst case for acquisition.
                PickpocketDetector.Ledger = [];
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var cold = PickpocketDetector.Analyze(bitmap);
                var coldMs = clock.Elapsed.TotalMilliseconds;
                var ledger = PickpocketDetector.Ledger;
                PickpocketDetector.Ledger = null;

                // Warm: hand in the last Active observation from the live trace, shifted into this crop.
                var warmState = "-";
                IReadOnlyList<string> warmLedger = [];
                var before = active.LastOrDefault(s => s.Index < index && index - s.Index <= 40);
                if (before is not null)
                {
                    var shift = 0d;
                    var previous = new PickpocketObservation(PickpocketVisualState.Active,
                        before.Bar, before.MarkerX - shift,
                        before.BandList.Select(b => b with { Left = b.Left - shift, Right = b.Right - shift }).ToArray(), before.Confidence, before.VisualReason);
                    PickpocketDetector.Ledger = [];
                    warmState = PickpocketDetector.Analyze(bitmap, null, previous).State.ToString();
                    warmLedger = PickpocketDetector.Ledger;
                    PickpocketDetector.Ledger = null;
                }

                PickpocketDetector.PanelProbe? probe = null;
                var reference = liveSample is { Visual: "Active" } && liveSample.Bar.Width > 0
                    ? liveSample : active.OrderBy(s => Math.Abs(s.Index - index)).FirstOrDefault();
                if (reference is not null)
                {
                    var bar = reference.Bar;
                    if (new Rectangle(Point.Empty, bitmap.Size).Contains(bar)) probe = PickpocketDetector.ProbePanel(bitmap, bar);
                }
                var present = probe is { MarkerFound: true, Bands.Count: >= 1 and <= 16 };
                var isHidden = cold.State == PickpocketVisualState.Hidden;
                bool? examined = present
                    ? ledger.SelectMany(l => StemLine().Matches(l).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))).Any(x => Math.Abs(x - probe!.MarkerX) <= 4)
                    : null;
                audits.Add(new FrameAudit(file, index, live, cold.State.ToString(), warmState, probe, ledger, warmLedger,
                    ledger.Any(l => l.Contains("EXHAUSTED") || l.Contains("budget")), examined,
                    isHidden && present && probe!.HeaderScore >= .85, isHidden && present && probe!.HeaderScore < .85, coldMs));
            }
            catch (Exception failure) when (failure is JsonException or KeyNotFoundException or IOException or ArgumentException) { /* skip half-written or unreadable frame */ }
        }
        return audits;
    }

    private static readonly bool Verbose = Environment.GetEnvironmentVariable("CUEPILOT_ANALYZE_VERBOSE") == "1";

    private static IEnumerable<string> Compact(IEnumerable<string> ledger) =>
        ledger.GroupBy(l => l.Trim()).Select(g => g.Count() > 1 ? $"{g.Key}  (x{g.Count()})" : g.Key);

    internal static string Build(string session, IReadOnlyList<Sample> samples, IReadOnlyList<FrameAudit> audits, bool finished, string? recorderState = null)
    {
        var t0 = samples.FirstOrDefault(s => s.Ms > 0)?.Ms ?? samples[0].Ms;
        string At(double ms) => ((ms - t0) / 1000).ToString("F2", CultureInfo.InvariantCulture) + "s";
        var report = new StringBuilder();
        report.AppendLine($"# Pickpocket session analysis: {session}");
        report.AppendLine(!finished ? "Session has no summary.json: still recording, or it did not finish cleanly. Analysis reads what is on disk."
            : recorderState is "Limited" or "Error" ? $"Session finished, but the recorder reported {recorderState}: some records or images were dropped."
            : "Session finished cleanly.");
        report.AppendLine($"{samples.Count} recorded samples over {(samples[^1].Ms - t0) / 1000:F1}s.");
        report.AppendLine();

        report.AppendLine("## Verdict");
        foreach (var finding in Diagnose(samples, audits, At, recorderState)) report.AppendLine($"- {(finding.Problem ? "[!!]" : "[ok]")} **{finding.Title}** {finding.Detail}");
        report.AppendLine();

        report.AppendLine("## What the detector saw (changes only)");
        report.AppendLine("```");
        var previousKey = "";
        var rows = 0;
        foreach (var s in samples.Where(s => s.Ms > 0))
        {
            var key = $"{s.Engine}|{s.Visual}|{s.VisualReason}|{s.Detail}";
            if (key == previousKey) continue;
            previousKey = key;
            if (++rows > 60) continue;
            report.AppendLine($"{At(s.Ms),8} #{s.Index,-5} {s.Engine,-9} sees {s.Visual,-9} conf {s.Confidence:F2} bands {s.Bands} gap {s.IntervalMs,4:F0}ms | {s.VisualReason} | {s.Detail}");
        }
        if (rows > 60) report.AppendLine($"... {rows - 60} more changes not shown");
        report.AppendLine("```");

        if (audits.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Saved frames re-read with today's detector");
            report.AppendLine("`live` = what the engine decided then. `cold` = fresh read, no history. `warm` = read with the live tracking history handed in. `header` = status-header match at the panel's real position (0.85 needed): `text` = lettering found, `clean bg` = expected-dark header cells that really are dark.");
            report.AppendLine("```");
            foreach (var a in audits.Where(a => a.Live != a.Cold || a.Cold == "Hidden").Take(50))
            {
                var probe = a.Probe is { } p
                    ? $"header {p.HeaderScore:F2} (text {p.HeaderText:P0}, clean bg {p.HeaderBackground:P0}) marker {(p.MarkerFound ? p.MarkerX.ToString("F0", CultureInfo.InvariantCulture) : "none")} intervals {p.Bands.Count}"
                    : "no reference bar";
                var flag = a.SearchMissed ? "  <== PANEL READABLE, SEARCH MISSED IT" + (a.MarkerExamined == false ? " (real marker never inspected)" : "")
                    : a.HeaderFailed ? "  <== PANEL PRESENT, HEADER UNREADABLE" : "";
                report.AppendLine($"{a.File} live={a.Live,-9} cold={a.Cold,-9} warm={a.Warm,-9} {probe}{flag}");
                if (a.Warm == "Hidden") foreach (var reason in Compact(a.WarmLedger).Take(Verbose ? 200 : 6)) report.AppendLine($"      warm: {reason}");
                if (a.Cold == "Hidden") foreach (var reason in Compact(a.Ledger).Where(l => !l.StartsWith("header-less continuation unavailable")).Take(Verbose ? 200 : 6)) report.AppendLine($"      {reason}");
            }
            report.AppendLine("```");
        }
        return report.ToString();
    }

    internal static List<Finding> Diagnose(IReadOnlyList<Sample> samples, IReadOnlyList<FrameAudit> audits, Func<double, string> at, string? recorderState = null)
    {
        var findings = new List<Finding>();
        if (recorderState is "Limited" or "Error")
            findings.Add(new(true, "Evidence incomplete.", $"The recorder ended {recorderState}: records or images were dropped or a write failed, so gaps below may be missing evidence rather than detector faults."));
        // Runs of the same visual state, closed by the next run's first sample.
        var runs = new List<(string Visual, double Start, double End, int First, int Count)>();
        foreach (var s in samples.Where(s => s.Ms > 0))
        {
            if (runs.Count > 0 && runs[^1].Visual == s.Visual) runs[^1] = runs[^1] with { End = s.Ms, Count = runs[^1].Count + 1 };
            else runs.Add((s.Visual, s.Ms, s.Ms, s.Index, 1));
        }
        for (var i = 0; i + 1 < runs.Count; i++) runs[i] = runs[i] with { End = runs[i + 1].Start };

        var active = samples.Where(s => s.Visual == "Active").ToList();
        var flickers = new List<(double Start, double Length, int First)>();
        for (var i = 1; i + 1 < runs.Count; i++)
            if (runs[i].Visual == "Hidden" && runs[i - 1].Visual == "Active" && runs[i + 1].Visual == "Active" && runs[i].End - runs[i].Start <= 1500)
                flickers.Add((runs[i].Start, runs[i].End - runs[i].Start, runs[i].First));
        if (flickers.Count > 0)
            findings.Add(new(true, "Panel flicker.", $"The detector lost the panel and got it straight back {flickers.Count} time(s), longest {flickers.Max(f => f.Length):F0} ms, first at {at(flickers[0].Start)}. A real panel cannot vanish and return that fast, so these are misreads, and each one resets tracking."));

        var headerless = active.Count(s => s.VisualReason.Contains("header unreadable", StringComparison.OrdinalIgnoreCase));
        if (active.Count > 0 && headerless * 5 >= active.Count)
            findings.Add(new(true, "Tracking leans on the fallback.", $"{headerless} of {active.Count} Active samples ({100.0 * headerless / active.Count:F0}%) came from the header-less fallback, which lasts at most 30 frames and needs an unbroken Active chain. Every full re-acquisition after a Hidden frame has to find the panel from scratch."));

        // A Hidden stretch that ends in Cooldown, with no game result before it, is a false end.
        var cooldown = samples.Select((s, i) => (s, i)).FirstOrDefault(x => x.s.Engine == "Cooldown" && (x.i == 0 || samples[x.i - 1].Engine != "Cooldown"));
        if (cooldown.s is not null && active.Count > 0)
        {
            var before = samples.Take(cooldown.i + 1).ToList();
            var lastActive = before.LastOrDefault(s => s.Visual == "Active");
            var resultSeen = lastActive is not null && before.Any(s => s.Index > lastActive.Index && s.Visual is "Grabbed" or "Missed");
            if (lastActive is not null && !resultSeen && (cooldown.s.Ms - lastActive.Ms) >= 2000)
                findings.Add(new(true, "False attempt end.", $"The panel was last read Active at {at(lastActive.Ms)}, then read Hidden until {at(cooldown.s.Ms)} ({(cooldown.s.Ms - lastActive.Ms) / 1000:F1}s) with no Grabbed/Missed result. The attempt tracker treats 3s of Hidden as the attempt ending and starts the 3-minute cooldown, so the next real attempt was blocked."));
        }

        var presses = samples.Select(s => s.Presses).DefaultIfEmpty().Max();
        var prepared = samples.Any(s => s.Visual == "Preparing");
        var waiting = samples.Count(s => s.PredictionReason.Contains("new preparation state", StringComparison.OrdinalIgnoreCase) || s.Detail.Contains("new preparation state", StringComparison.OrdinalIgnoreCase));
        if (active.Count > 0 && presses == 0 && !prepared && waiting > 0 && samples.All(s => s.InputMode != "Observe"))
            findings.Add(new(true, "Never armed.", $"No Preparing state was ever read, so automatic input could not arm ('waiting for a new preparation state' on {waiting} samples). Either the fade-in was missed or it was misread like the frames below."));
        else if (presses > 0)
            findings.Add(new(false, "Shot fired.", $"{presses} automatic tap(s) sent. Check the run's REPORT.md for the visual offset."));

        var slow = 0;
        for (var i = 1; i < samples.Count; i++)
            if (samples[i].Visual == "Active" && samples[i - 1].Visual == "Active" && samples[i].IntervalMs > 24) slow++;
        if (slow > 0) findings.Add(new(true, "Slow Active sampling.", $"{slow} back-to-back Active samples were more than 24 ms apart (target 16 ms)."));
        var hiddenGaps = samples.Where(s => s.Visual == "Hidden" && s.IntervalMs > 0).Select(s => s.IntervalMs).OrderBy(x => x).ToList();
        if (flickers.Count > 0 && hiddenGaps.Count > 0 && hiddenGaps[hiddenGaps.Count / 2] > 50)
            findings.Add(new(true, "Loop slows down when the panel is lost.", $"Hidden samples arrive every ~{hiddenGaps[hiddenGaps.Count / 2]:F0} ms instead of 16 ms, so every false Hidden costs several frames of tracking right when a timed press needs them."));

        var missed = audits.Where(a => a.SearchMissed).ToList();
        if (missed.Count > 0)
        {
            var neverInspected = missed.Count(a => a.MarkerExamined == false);
            var exhausted = missed.Count(a => a.BudgetExhausted);
            var warmSaved = missed.Count(a => a.Warm == "Active");
            findings.Add(new(true, "Panel readable, but the search missed it.",
                $"{missed.Count} of {audits.Count} saved frames read Hidden from a cold start although the status header scores {missed.Average(a => a.Probe!.HeaderScore):F2} at the panel's real position (0.85 needed). "
                + $"In {neverInspected} of them the real marker was never inspected, and the header-search budget ran out in {exhausted}: scenery (grass, clothing) produced many marker-like stems that were tried first. "
                + $"With live tracking history {warmSaved} of them stayed Active, so this bites right after any Hidden frame, when history is gone."));
        }
        var failed = audits.Where(a => a.HeaderFailed).ToList();
        if (failed.Count > 0)
        {
            var text = failed.Average(a => a.Probe!.HeaderText);
            var background = failed.Average(a => a.Probe!.HeaderBackground);
            var why = background + .05 < text
                ? "The lettering is found but the area that should be dark around it has bright pixels (pale clothing, light grass or sky behind the header)."
                : text + .05 < background ? "The dark surroundings are fine but the lettering itself is not found (fading, blocked or a different header)."
                : "Both the lettering and its surroundings score low.";
            findings.Add(new(true, "Panel present, header unreadable.", $"{failed.Count} of {audits.Count} saved frames show the marker and target intervals but the header scores {failed.Average(a => a.Probe!.HeaderScore):F2} (0.85 needed; text {text:P0}, clean background {background:P0}). {why}"));
        }
        var differ = audits.Count(a => a.Live != a.Cold && !a.SearchMissed && !a.HeaderFailed);
        if (differ > 0 && missed.Count == 0 && failed.Count == 0)
            findings.Add(new(false, "Live and offline reads differ.", $"{differ} frame(s) were read differently offline. Usually tracking history; see the frame list."));

        if (findings.All(f => !f.Problem))
        {
            if (active.Count > 0) findings.Add(new(false, "No detector faults found.", "The panel was recognized and tracked without flicker or false ends."));
            else findings.Add(new(true, audits.Count == 0 ? "No frames audited." : "No Active panel seen.", "No Active panel was seen in this session, so there was nothing to judge; check the FiveM window and capture bounds."));
        }
        return findings;
    }
}
