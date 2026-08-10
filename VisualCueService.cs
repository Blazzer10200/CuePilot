namespace WorkflowLooper;

internal static class VisualCueService
{
    private const int SampleColumns = 20;
    private const int SampleRows = 12;

    internal static VisualCueSettings CaptureAtCursor(WindowTargetSettings target)
    {
        if (!WindowTargetService.TryGetTargetBounds(target, out var windowBounds))
        {
            throw new InvalidOperationException("Keep the target application in front before capturing a visual cue.");
        }

        var width = Math.Min(200, windowBounds.Width);
        var height = Math.Min(120, windowBounds.Height);
        var cursor = Cursor.Position;
        var left = Math.Clamp(cursor.X - width / 2, windowBounds.Left, windowBounds.Right - width);
        var top = Math.Clamp(cursor.Y - height / 2, windowBounds.Top, windowBounds.Bottom - height);
        var region = new Rectangle(left, top, width, height);
        var fingerprint = CaptureFingerprint(region);
        return new VisualCueSettings
        {
            Enabled = true,
            OffsetX = region.Left - windowBounds.Left,
            OffsetY = region.Top - windowBounds.Top,
            Width = region.Width,
            Height = region.Height,
            Fingerprint = Convert.ToBase64String(fingerprint),
            SimilarityPercent = 86,
        };
    }

    internal static double Similarity(VisualCueSettings cue, WindowTargetSettings target)
    {
        if (!cue.IsConfigured || !WindowTargetService.TryGetTargetBounds(target, out var windowBounds))
        {
            return 0;
        }

        var region = new Rectangle(windowBounds.Left + cue.OffsetX, windowBounds.Top + cue.OffsetY, cue.Width, cue.Height);
        var current = CaptureFingerprint(region);
        var expected = Convert.FromBase64String(cue.Fingerprint);
        if (current.Length != expected.Length)
        {
            return 0;
        }

        long difference = 0;
        for (var index = 0; index < current.Length; index++)
        {
            difference += Math.Abs(current[index] - expected[index]);
        }

        return Math.Clamp(1d - difference / (current.Length * 255d), 0d, 1d);
    }

    private static byte[] CaptureFingerprint(Rectangle region)
    {
        using var bitmap = new Bitmap(region.Width, region.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
        }

        var samples = new byte[SampleColumns * SampleRows];
        for (var row = 0; row < SampleRows; row++)
        {
            for (var column = 0; column < SampleColumns; column++)
            {
                var x = Math.Clamp((int)Math.Round((column + 0.5) * region.Width / SampleColumns), 0, region.Width - 1);
                var y = Math.Clamp((int)Math.Round((row + 0.5) * region.Height / SampleRows), 0, region.Height - 1);
                var color = bitmap.GetPixel(x, y);
                samples[row * SampleColumns + column] = (byte)Math.Round(color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722);
            }
        }

        return samples;
    }
}
