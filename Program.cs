namespace WorkflowLooper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTest.Run();
        }

        ApplicationConfiguration.Initialize();
        var previewIndex = Array.FindIndex(args, item => item.Equals("--render-preview", StringComparison.OrdinalIgnoreCase));
        var guidePreviewIndex = Array.FindIndex(args, item => item.Equals("--render-guide", StringComparison.OrdinalIgnoreCase));
        var settingsPreviewIndex = Array.FindIndex(args, item => item.Equals("--render-settings", StringComparison.OrdinalIgnoreCase));
        var presetPreviewIndex = Array.FindIndex(args, item => item.Equals("--render-preset", StringComparison.OrdinalIgnoreCase));
        var requestedPreviewIndex = new[] { previewIndex, guidePreviewIndex, settingsPreviewIndex, presetPreviewIndex }.FirstOrDefault(index => index >= 0, -1);
        if (requestedPreviewIndex >= 0 && requestedPreviewIndex + 1 < args.Length)
        {
            using var form = new MainForm(false)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-10_000, -10_000),
                ShowInTaskbar = false,
            };
            form.Show();
            Application.DoEvents();
            if (guidePreviewIndex >= 0)
            {
                form.ShowGuideForPreview();
                Application.DoEvents();
            }
            else if (settingsPreviewIndex >= 0)
            {
                form.ShowSettingsForPreview();
                Application.DoEvents();
            }
            else if (presetPreviewIndex >= 0)
            {
                form.ShowPresetForPreview();
                Application.DoEvents();
            }
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[requestedPreviewIndex + 1]);
            form.Hide();
            return 0;
        }

        Application.Run(new MainForm());
        return 0;
    }
}
