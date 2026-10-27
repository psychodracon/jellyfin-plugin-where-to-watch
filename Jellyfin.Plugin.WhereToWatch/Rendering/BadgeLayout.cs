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
    /// <returns>One rectangle per badge that fits.</returns>
    public static IReadOnlyList<BadgeRect> Compute(int posterWidth, int posterHeight, int count, LayoutSettings settings)
    {
        var width = (int)Math.Round(posterWidth * settings.WidthPercent / 100);
        var height = (int)Math.Round(width / settings.AspectRatio);
        var margin = (int)Math.Round(posterWidth * settings.MarginPercent / 100);
        var spacing = (int)Math.Round(posterWidth * settings.SpacingPercent / 100);

        var rects = new List<BadgeRect>();
        var x = settings.Corner is BadgeCorner.BottomRight or BadgeCorner.TopRight ? posterWidth - margin - width : margin;
        var fromBottom = settings.Corner is BadgeCorner.BottomLeft or BadgeCorner.BottomRight;

        for (var i = 0; i < count; i++)
        {
            var offset = margin + (i * (height + spacing));
            var y = fromBottom ? posterHeight - offset - height : offset;
            if (y < margin || y + height > posterHeight - margin)
            {
                break;
            }

            rects.Add(new BadgeRect(x, y, width, height));
        }

        return rects;
    }
}
