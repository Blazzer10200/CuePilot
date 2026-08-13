namespace WorkflowLooper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
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

        var meter = ArgumentValue(args, "--analyze-meter");
        if (meter is not null)
        {
            using var bitmap = new Bitmap(meter);
            var observation = FishingMeterService.AnalyzeFrame(bitmap);
            Console.WriteLine($"visible={observation.IsVisible} tension={observation.TensionRatio:P1} progress={observation.ProgressRatio:P1} caught={observation.IsCaught} failed={observation.IsFailed} confidence={observation.Confidence:P1}");
            return observation.IsVisible ? 0 : 2;
        }

        var benchmark = ArgumentValue(args, "--benchmark-meter");
        if (benchmark is not null) return RunMeterBenchmark(benchmark);

        var render = ArgumentValue(args, "--render-dashboard");
        if (render is not null)
        {
            using var form = new MainForm(false, AppSettings.Defaults())
            {
                StartPosition = FormStartPosition.Manual,
                Location = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty,
                ShowInTaskbar = false,
                TopMost = true,
            };
            var renderReady = args.Contains("--ready", StringComparer.OrdinalIgnoreCase);
            var renderEmpty = args.Contains("--empty", StringComparer.OrdinalIgnoreCase);
            var renderFault = args.Contains("--fault", StringComparer.OrdinalIgnoreCase);
            if (renderEmpty) form.PrepareEmptyForPreview();
            else if (renderFault) form.PrepareFaultForPreview();
            else if (renderReady) form.PrepareReadyForPreview();
            else form.PrepareForPreview();
            var renderAdvanced = args.Contains("--advanced", StringComparer.OrdinalIgnoreCase);
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.BringToFront();
            if (args.Contains("--compact", StringComparer.OrdinalIgnoreCase)) form.Size = form.MinimumSize;
            if (renderAdvanced) form.ShowAdvancedForPreview();
            else if (!renderReady) form.PrimeForRenderPreview();
            form.PerformLayout();
            form.Refresh();
            Application.DoEvents();
            var renderControl = args.Contains("--render-control", StringComparer.OrdinalIgnoreCase);
            using var bitmap = RenderDashboardBitmap(form, renderReady, renderAdvanced, renderControl);
            bitmap.Save(render);
            return 0;
        }

        Application.Run(new MainForm());
        return 0;
    }

    private static Bitmap RenderDashboardBitmap(MainForm form, bool ready, bool advanced, bool renderControl)
    {
        if (advanced) form.ShowAdvancedForPreview();
        else if (ready) form.HideAdvancedForPreview();
        Refresh(form);
        return renderControl ? CaptureControl(form, advanced) : CaptureWindow(form);
    }

    private static Bitmap CaptureControl(MainForm form, bool advanced)
    {
        var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
        if (advanced)
        {
            var bounds = form.AdvancedBoundsForPreview;
            using var layer = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            form.AdvancedControlForPreview.DrawToBitmap(layer, new Rectangle(Point.Empty, bounds.Size));
            using var graphics = Graphics.FromImage(bitmap);
            graphics.DrawImageUnscaled(layer, bounds.Location);
        }
        return bitmap;
    }

    private static void Refresh(Control control)
    {
        control.PerformLayout();
        control.Refresh();
        Application.DoEvents();
        Thread.Sleep(150);
        control.Refresh();
        Application.DoEvents();
    }

    private static Bitmap CaptureWindow(Form form)
    {
        var bitmap = new Bitmap(form.Width, form.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, form.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
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

        using var source = new GdiFrameSource();
        var region = new Rectangle(Point.Empty, resolved.Bounds.Size);
        if (!source.TryCapture(target, region, out var frame, out var status) || frame is null)
        {
            Console.Error.WriteLine($"PROMPT_CAPTURE_FAILED backend={status.Backend} detail={status.Detail}");
            return 3;
        }

        using (frame)
        {
            outputPath ??= Path.Combine(Path.GetTempPath(), "workflow-looper-prompt-capture.png");
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
}
