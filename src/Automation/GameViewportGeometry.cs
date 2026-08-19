using System.Drawing;

namespace CuePilot;

internal readonly record struct GameSafeViewport(double Left, double Top, double Width, double Height)
{
    internal double MapX(double ratio) => Left + Width * Math.Clamp(ratio, 0, 1);
    internal double MapY(double ratio) => Top + Height * Math.Clamp(ratio, 0, 1);

    internal Rectangle SearchRegion(
        double leftRatio,
        double topRatio,
        double rightRatio,
        double bottomRatio,
        Rectangle bounds)
    {
        var left = Math.Clamp((int)Math.Floor(MapX(leftRatio)), bounds.Left, bounds.Right);
        var top = Math.Clamp((int)Math.Floor(MapY(topRatio)), bounds.Top, bounds.Bottom);
        var right = Math.Clamp((int)Math.Ceiling(MapX(rightRatio)), left, bounds.Right);
        var bottom = Math.Clamp((int)Math.Ceiling(MapY(bottomRatio)), top, bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}

internal static class GameViewportGeometry
{
    private const double ReferenceAspectRatio = 16d / 9d;

    internal static GameSafeViewport CenteredSafeViewport(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return default;
        }

        // GTA/FiveM anchors HUD elements to a height-scaled 16:9 canvas. On a
        // wide display that canvas is pillarboxed inside the captured frame;
        // on a narrower display it extends past the left and right edges and is
        // effectively center-cropped. Keeping the virtual width in both cases
        // makes the same normalized HUD coordinates valid for 5:4 through 32:9.
        var width = bounds.Height * ReferenceAspectRatio;
        return new GameSafeViewport(
            bounds.Left + (bounds.Width - width) / 2d,
            bounds.Top,
            width,
            bounds.Height);
    }
}
