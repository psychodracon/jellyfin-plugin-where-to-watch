namespace Jellyfin.Plugin.WhereToWatch.Rendering;

/// <summary>
/// Poster corner the badges are anchored to.
/// </summary>
public enum BadgeCorner
{
    /// <summary>Bottom-left corner.</summary>
    BottomLeft,

    /// <summary>Bottom-right corner.</summary>
    BottomRight,

    /// <summary>Top-left corner.</summary>
    TopLeft,

    /// <summary>Top-right corner.</summary>
    TopRight,
}

/// <summary>
/// Settings that decide where badges go. Sizes are percentages of the poster width so layouts scale with any poster size.
/// </summary>
/// <param name="Corner">Corner badges are anchored to.</param>
/// <param name="WidthPercent">Badge width as a percentage of the poster width.</param>
/// <param name="AspectRatio">Badge width divided by badge height.</param>
/// <param name="MarginPercent">Distance from the poster edges as a percentage of the poster width.</param>
/// <param name="SpacingPercent">Gap between stacked badges as a percentage of the poster width.</param>
public sealed record LayoutSettings(
    BadgeCorner Corner,
    double WidthPercent,
    double AspectRatio,
    double MarginPercent,
    double SpacingPercent);

/// <summary>
/// A badge rectangle in poster pixels.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public sealed record BadgeRect(int X, int Y, int Width, int Height);

/// <summary>
/// Places stacked badges on a poster.
/// </summary>
public static class BadgeLayout
{
    /// <summary>
    /// Computes badge rectangles, first badge nearest the anchored edge.
    /// </summary>
    /// <param name="posterWidth">Poster width in pixels.</param>
    /// <param name="posterHeight">Poster height in pixels.</param>
    /// <param name="count">Number of badges to place.</param>
    /// <param name="settings">Layout settings.</param>
    /// <param name="region">The part of the poster that is visible, see <see cref="VisibleRegion"/>; the whole poster when omitted.</param>
    /// <returns>One rectangle per badge that fits.</returns>
    public static IReadOnlyList<BadgeRect> Compute(int posterWidth, int posterHeight, int count, LayoutSettings settings, BadgeRect? region = null)
    {
        var visible = region ?? new BadgeRect(0, 0, posterWidth, posterHeight);
        var width = (int)Math.Round(visible.Width * settings.WidthPercent / 100);
        var height = (int)Math.Round(width / settings.AspectRatio);
        var margin = (int)Math.Round(visible.Width * settings.MarginPercent / 100);
        var spacing = (int)Math.Round(visible.Width * settings.SpacingPercent / 100);

        var rects = new List<BadgeRect>();
        var x = settings.Corner is BadgeCorner.BottomRight or BadgeCorner.TopRight ? visible.X + visible.Width - margin - width : visible.X + margin;
        var fromBottom = settings.Corner is BadgeCorner.BottomLeft or BadgeCorner.BottomRight;
        var top = visible.Y + margin;
        var bottom = visible.Y + visible.Height - margin;

        for (var i = 0; i < count; i++)
        {
            var offset = i * (height + spacing);
            var y = fromBottom ? bottom - height - offset : top + offset;
            if (y < top || y + height > bottom)
            {
                break;
            }

            rects.Add(new BadgeRect(x, y, width, height));
        }

        return rects;
    }

    /// <summary>
    /// Works out which part of an image a card shows. Cards scale the image to cover the card and centre it, so whatever
    /// overflows on the longer axis is cropped away equally on both sides.
    /// </summary>
    /// <param name="imageWidth">Image width in pixels.</param>
    /// <param name="imageHeight">Image height in pixels.</param>
    /// <param name="cardAspect">Card width divided by card height.</param>
    /// <returns>The visible rectangle in image pixels.</returns>
    public static BadgeRect VisibleRegion(int imageWidth, int imageHeight, double cardAspect)
    {
        var width = Math.Min(imageWidth, (int)Math.Round(imageHeight * cardAspect));
        var height = Math.Min(imageHeight, (int)Math.Round(imageWidth / cardAspect));
        return new BadgeRect((imageWidth - width) / 2, (imageHeight - height) / 2, width, height);
    }
}
