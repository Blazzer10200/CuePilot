using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace CuePilot;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase)) return SelfTest.Run();
        if (args.Contains("--ui-bridge", StringComparer.OrdinalIgnoreCase)) return UiBridge.Run();

        var targetProbe = ArgumentValue(args, "--target-probe");
        if (targetProbe is not null) return RunTargetProbe(targetProbe);

        var captureProbe = ArgumentValue(args, "--capture-probe");
        if (captureProbe is not null) return RunCaptureProbe(captureProbe);

        var promptCapture = ArgumentValue(args, "--capture-prompt");
        if (promptCapture is not null) return RunPromptCapture(
            promptCapture,
            ArgumentValue(args, "--output"),
            args.Contains("--capture-only", StringComparer.OrdinalIgnoreCase));

        var inputProbe = ArgumentValue(args, "--input-probe");
        if (inputProbe is not null) return RunInputProbe(inputProbe);

        var prompt = ArgumentValue(args, "--analyze-prompt");
        if (prompt is not null)
        {
            using var bitmap = new Bitmap(prompt);
            var observation = FishingPromptDetector.Analyze(bitmap, out var evidence);
            Console.WriteLine($"kind={observation.Kind} confidence={observation.Confidence:P1} cast={observation.CastConfidence:P1} collect={observation.CollectConfidence:P1}");
            Console.WriteLine(evidence);
            return observation.Kind == FishingPromptKind.None ? 2 : 0;
        }

        var promptBenchmark = ArgumentValue(args, "--benchmark-prompt");
        if (promptBenchmark is not null) return RunPromptBenchmark(promptBenchmark);

        var meter = ArgumentValue(args, "--analyze-meter");
        if (meter is not null)
        {
            using var bitmap = new Bitmap(meter);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var analysis = FishingMeterService.AnalyzeFrameDetailed(bitmap);
            clock.Stop();
            var observation = analysis.Observation;
            Console.WriteLine($"visible={observation.IsVisible} tension={observation.TensionRatio:P1} progress={observation.ProgressRatio:P1} caught={observation.IsCaught} failed={observation.IsFailed} confidence={observation.Confidence:P1} detector_ms={clock.Elapsed.TotalMilliseconds:F2} candidates={analysis.CandidateCount}");
            foreach (var candidate in FishingMeterService.InspectFrame(bitmap))
            {
                Console.WriteLine(candidate);
            }
            return observation.IsVisible ? 0 : 2;
        }

        var benchmark = ArgumentValue(args, "--benchmark-meter");
        if (benchmark is not null) return RunMeterBenchmark(benchmark);

        var fishingReplay = ArgumentValue(args, "--replay-fishing");
        if (fishingReplay is not null) return ReplayFishing(fishingReplay);

        var lockpicking = ArgumentValue(args, "--analyze-lockpicking");
        if (lockpicking is not null)
        {
            using var bitmap = new Bitmap(lockpicking);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var observation = LockpickingDetector.Analyze(bitmap);
            clock.Stop();
            var approachRatio = observation.Target?.ApproachRatio ?? 0;
            var labels = observation.Targets is null
                ? "-"
                : string.Join(',', observation.Targets.Select(target => target.Number?.ToString() ?? "?"));
            Console.WriteLine($"state={observation.State} confidence={observation.Confidence:P1} hud=({observation.HudCenterX:P1},{observation.HudCenterY:P1}) radius={observation.HudRadius:P1} targets={observation.VisibleTargetCount} labels=[{labels}] target_phase={observation.Target?.Phase} target=({observation.Target?.CenterX:P1},{observation.Target?.CenterY:P1}) number={observation.Target?.Number?.ToString() ?? "-"} literal={observation.Target?.HasLiteralNumber ?? false} approach={approachRatio:F2} fill={observation.Target?.FillDensity ?? 0:F2} action={observation.PredictedAction} detector_ms={clock.Elapsed.TotalMilliseconds:F2}");
            var evidence = LockpickingDetector.Inspect(bitmap);
            Console.WriteLine($"hud={evidence.HudConfidence:F3} open={evidence.OpenRingCoverage:F3} spin={evidence.SpinRingCoverage:F3} label={evidence.BottomLabelSignal:F3} arcs=[{string.Join(',', evidence.ArcProfile.Select(value => value.ToString("F3")))}]");
            Console.WriteLine(observation.Reason);
            return observation.State == LockpickingVisualState.Hidden ? 2 : 0;
        }

        var lockpickingReplay = ArgumentValue(args, "--replay-lockpicking");
        if (lockpickingReplay is not null)
        {
            return ReplayLockpicking(lockpickingReplay, ArgumentDouble(args, "--fps", 30));
        }

        var replay = ArgumentValue(args, "--replay-session");
        if (replay is not null) return ReplayDebugSession(replay);

        Console.Error.WriteLine("CuePilot Engine is started by the Tauri desktop application. Use --self-test or a documented probe command for direct execution.");
        return 2;
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double ArgumentDouble(string[] args, string name, double fallback) =>
        double.TryParse(
            ArgumentValue(args, name),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
        && value > 0
            ? value
            : fallback;

    private static int ReplayLockpicking(string directory, double framesPerSecond)
    {
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"LOCKPICKING_REPLAY_FAILED missing={directory}");
            return 2;
        }

        var files = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("LOCKPICKING_REPLAY_FAILED no image frames found.");
            return 2;
        }

        var tracker = new LockpickingObservationTracker();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var ticksPerFrame = System.Diagnostics.Stopwatch.Frequency / framesPerSecond;
        var lastKey = string.Empty;
        var detectorTicks = 0L;
        LockpickingObservation? previousRawObservation = null;
        for (var index = 0; index < files.Length; index++)
        {
            using var bitmap = new Bitmap(files[index]);
            var detectorStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var rawObservation = LockpickingDetector.Analyze(bitmap, previousRawObservation);
            detectorTicks += System.Diagnostics.Stopwatch.GetTimestamp() - detectorStarted;
            previousRawObservation = rawObservation;
            var observation = tracker.Track(
                rawObservation,
                start + (long)Math.Round(index * ticksPerFrame),
                TimeSpan.Zero,
                0);
            var key = $"{observation.State}:{observation.PredictedAction}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
            var transitionKey = $"{key}:{observation.Target?.Number}:{observation.Target?.Phase}";
            if (!transitionKey.Equals(lastKey, StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"t={index / framesPerSecond:F3}s state={observation.State} target={observation.Target?.Number?.ToString() ?? "-"} " +
                    $"phase={observation.Target?.Phase.ToString() ?? "None"} approach={observation.Target?.ApproachRatio ?? 0:F2} " +
                    $"eta_ms={observation.Target?.TimeToReadyMilliseconds?.ToString("F0") ?? "-"} action=\"{observation.PredictedAction}\"");
                lastKey = transitionKey;
            }
        }

        var detectorMeanMilliseconds = detectorTicks * 1000d / System.Diagnostics.Stopwatch.Frequency / files.Length;
        Console.WriteLine($"frames={files.Length} fps={framesPerSecond:F2} detector_mean_ms={detectorMeanMilliseconds:F2} summary=[{string.Join(',', counts.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}"))}]");
        return 0;
    }

    private static int ReplayFishing(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"FISHING_REPLAY_FAILED missing={directory}");
            return 2;
        }

        var files = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine("FISHING_REPLAY_FAILED no image frames found.");
            return 2;
        }

        var report = FishingReplayService.Replay(files);
        foreach (var transition in report.Transitions)
        {
            Console.WriteLine(
                $"frame={transition.FrameIndex} state={transition.State} prompt={transition.Prompt} " +
                $"suppressed={transition.PromptSuppressed} meter={transition.MeterVisible} " +
                $"caught={transition.Caught} failed={transition.Failed} confidence={transition.Confidence:P1}");
        }

        Console.WriteLine(
            $"frames={report.FrameCount} meter_frames={report.MeterFrames} prompt_frames={report.PromptFrames} " +
            $"suppressed_prompt_frames={report.SuppressedPromptFrames} caught_frames={report.CaughtFrames} " +
            $"detector_mean_ms={report.MeanDetectorMilliseconds:F2} transitions={report.Transitions.Count}");
        return 0;
    }

    private static int RunTargetProbe(string processName)
    {
        var target = new WindowTargetSettings { ProcessName = processName };
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            Console.Error.WriteLine($"TARGET_PROBE_FAILED {detail}");
            return 2;
        }
        Console.WriteLine($"TARGET_PROBE_OK hwnd=0x{resolved.Handle.ToInt64():X} pid={resolved.ProcessId} size={resolved.Bounds.Width}x{resolved.Bounds.Height} foreground={resolved.IsForeground} minimized={resolved.IsMinimized}");
        return 0;
    }

    private static int RunCaptureProbe(string processName)
    {
        var target = new WindowTargetSettings { ProcessName = processName };
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            Console.Error.WriteLine($"CAPTURE_PROBE_FAILED {detail}");
            return 2;
        }
        using var source = FrameSourceFactory.Create();
        var observation = FishingMeterService.Observe(source, target, out var status);
        if (status.State != FrameSourceState.Ready)
        {
            Console.Error.WriteLine($"CAPTURE_PROBE_FAILED backend={status.Backend} detail={status.Detail}");
            return 3;
        }
        Console.WriteLine($"CAPTURE_PROBE_OK backend={status.Backend} ms={status.CaptureMilliseconds:F2} visible={observation.IsVisible} failed={observation.IsFailed} confidence={observation.Confidence:P0}");
        return 0;
    }

    private static int RunPromptCapture(string processName, string? outputPath, bool captureOnly)
    {
        var target = new WindowTargetSettings { ProcessName = processName };
        if (!WindowTargetService.TryResolve(target, out var resolved, out var detail))
        {
            Console.Error.WriteLine($"PROMPT_CAPTURE_FAILED {detail}");
            return 2;
        }

        using var source = FrameSourceFactory.Create();
        var region = new Rectangle(Point.Empty, resolved.Bounds.Size);
        if (!source.TryCapture(target, region, out var frame, out var status) || frame is null)
        {
            Console.Error.WriteLine($"PROMPT_CAPTURE_FAILED backend={status.Backend} detail={status.Detail}");
            return 3;
        }

        using (frame)
        {
            outputPath ??= Path.Combine(Path.GetTempPath(), "cuepilot-prompt-capture.png");
            frame.Bitmap.Save(outputPath);
            if (captureOnly)
            {
                Console.WriteLine($"PROMPT_CAPTURE_OK backend={status.Backend} capture_ms={status.CaptureMilliseconds:F2} size={frame.Bitmap.Width}x{frame.Bitmap.Height} output={outputPath}");
                return 0;
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var observation = FishingPromptDetector.Analyze(frame.Bitmap);
            Console.WriteLine($"PROMPT_CAPTURE_OK backend={status.Backend} capture_ms={status.CaptureMilliseconds:F2} detector_ms={clock.Elapsed.TotalMilliseconds:F2} size={frame.Bitmap.Width}x{frame.Bitmap.Height} kind={observation.Kind} confidence={observation.Confidence:P0} cast={observation.CastConfidence:P0} collect={observation.CollectConfidence:P0} output={outputPath}");
        }

        return 0;
    }

    private static int RunInputProbe(string processName)
    {
        var capability = new TargetInputRouter(InputDeliveryMode.Automatic).Probe(
            new WindowTargetSettings { ProcessName = processName });
        Console.WriteLine($"INPUT_PROBE_{(capability.Ready ? "READY" : "FAILED")} backend={capability.Backend} covered={capability.SupportsCoveredWindow} detail={capability.Detail}");
        return capability.Ready ? 0 : 2;
    }

    private static int RunMeterBenchmark(string directory)
    {
        var files = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .ToArray();
        var elapsedTicks = 0L;
        var visible = 0;
        var missed = new List<string>();
        foreach (var file in files)
        {
            using var frame = new Bitmap(file);
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var observation = FishingMeterService.AnalyzeFrame(frame);
            elapsedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startedAt;
            if (observation.IsVisible)
            {
                visible++;
            }
            else
            {
                missed.Add(Path.GetFileName(file));
            }
        }
        var milliseconds = elapsedTicks * 1_000d / System.Diagnostics.Stopwatch.Frequency;
        Console.WriteLine($"frames={files.Length} visible={visible} missed={missed.Count} detector_mean_ms={(files.Length == 0 ? 0 : milliseconds / files.Length):F2}");
        if (missed.Count > 0) Console.WriteLine($"missed_frames={string.Join(',', missed)}");
        return 0;
    }

    private static int RunPromptBenchmark(string directory)
    {
        var files = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .ToArray();
        var elapsedTicks = 0L;
        var matches = new List<string>();
        foreach (var file in files)
        {
            using var frame = new Bitmap(file);
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var observation = FishingPromptDetector.Analyze(frame);
            elapsedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startedAt;
            if (observation.Kind != FishingPromptKind.None)
            {
                matches.Add($"{Path.GetFileName(file)}:{observation.Kind}:{observation.Confidence:F3}");
            }
        }

        var milliseconds = elapsedTicks * 1_000d / System.Diagnostics.Stopwatch.Frequency;
        Console.WriteLine($"frames={files.Length} matches={matches.Count} detector_mean_ms={(files.Length == 0 ? 0 : milliseconds / files.Length):F2}");
        if (matches.Count > 0) Console.WriteLine($"matched_frames={string.Join(',', matches)}");
        return 0;
    }

    private static int ReplayDebugSession(string directory)
    {
        var manifestPath = Path.Combine(directory, "session.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"REPLAY_FAILED missing={manifestPath}");
            return 2;
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!manifest.RootElement.TryGetProperty("frames", out var frames)
            || frames.ValueKind != JsonValueKind.Array)
        {
            Console.Error.WriteLine("REPLAY_FAILED session manifest has no frames array.");
            return 2;
        }

        var checkedFrames = 0;
        var failures = 0;
        var meterTracker = new FishingMeterTracker();
        foreach (var frame in frames.EnumerateArray())
        {
            var label = frame.GetProperty("label").GetString() ?? "unknown";
            var imageName = frame.GetProperty("imageName").GetString() ?? string.Empty;
            var imagePath = Path.Combine(directory, imageName);
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"frame={label} result=missing_file path={imageName}");
                failures++;
                continue;
            }

            using var bitmap = new Bitmap(imagePath);
            checkedFrames++;
            if (label.StartsWith("prompt-", StringComparison.OrdinalIgnoreCase))
            {
                var observation = FishingPromptDetector.Analyze(bitmap, out var evidence);
                var expected = label.EndsWith("-confirmed", StringComparison.OrdinalIgnoreCase);
                var passed = !expected || observation.Kind != FishingPromptKind.None;
                if (!passed) failures++;
                Console.WriteLine(
                    $"frame={label} detector=prompt result={(passed ? "pass" : "fail")} kind={observation.Kind} confidence={observation.Confidence:F3} reason={evidence.DecisionReason}");
            }
            else if (label.StartsWith("meter-", StringComparison.OrdinalIgnoreCase))
            {
                var analysis = FishingMeterService.AnalyzeFrameDetailed(bitmap, meterTracker);
                var expected = label.Equals("meter-confirmed", StringComparison.OrdinalIgnoreCase);
                var passed = !expected || analysis.Observation.IsVisible;
                if (!passed) failures++;
                Console.WriteLine(
                    $"frame={label} detector=meter result={(passed ? "pass" : "fail")} visible={analysis.Observation.IsVisible} confidence={analysis.Observation.Confidence:F3} reason={analysis.PrimaryCandidate?.Evidence.DecisionReason ?? "no candidate"}");
            }
        }

        Console.WriteLine($"REPLAY_COMPLETE frames={checkedFrames} failures={failures}");
        return failures == 0 ? 0 : 2;
    }
}
