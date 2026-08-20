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

    internal static Rectangle AdaptiveHudSearchRegion(
        Rectangle bounds,
        double leftRatio,
        double topRatio,
        double rightRatio,
        double bottomRatio)
    {
        var safeViewport = CenteredSafeViewport(bounds);
        var region = safeViewport.SearchRegion(leftRatio, topRatio, rightRatio, bottomRatio, bounds);
        if (Math.Abs(safeViewport.Left - bounds.Left) < 1
            && Math.Abs(safeViewport.Width - bounds.Width) < 1)
        {
            return region;
        }

        // FiveM normally anchors NUI to a centered 16:9 safe canvas, but some
        // resources and UI-scale combinations anchor to the full visible frame.
        // Search the union on non-16:9 layouts so both conventions work without
        // changing any prompt identity or input threshold.
        var fullFrameViewport = new GameSafeViewport(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var fullFrameRegion = fullFrameViewport.SearchRegion(
            leftRatio,
            topRatio,
            rightRatio,
            bottomRatio,
            bounds);
        return Rectangle.Intersect(bounds, Rectangle.Union(region, fullFrameRegion));
    }
}
