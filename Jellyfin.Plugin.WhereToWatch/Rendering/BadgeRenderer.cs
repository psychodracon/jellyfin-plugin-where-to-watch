using SkiaSharp;

namespace Jellyfin.Plugin.WhereToWatch.Rendering;

/// <summary>
/// Settings for drawing badges.
/// </summary>
/// <param name="Layout">Where badges go.</param>
/// <param name="BackdropColor">Backdrop colour as #AARRGGBB or #RRGGBB.</param>
/// <param name="CornerRadiusPercent">Backdrop corner radius as a percentage of the badge height.</param>
/// <param name="JpegQuality">JPEG quality, 1-100.</param>
public sealed record RenderSettings(
    LayoutSettings Layout,
    string BackdropColor,
    double CornerRadiusPercent,
    int JpegQuality);

/// <summary>
/// Draws provider badges onto a poster.
/// </summary>
public static class BadgeRenderer
{
    /// <summary>
    /// Draws one badge per logo onto the poster.
    /// </summary>
    /// <param name="poster">Encoded poster image.</param>
    /// <param name="logos">Encoded logo images in priority order.</param>
    /// <param name="settings">Render settings.</param>
    /// <returns>The badged poster as JPEG bytes.</returns>
    public static byte[] Render(byte[] poster, IReadOnlyList<byte[]> logos, RenderSettings settings)
    {
        using var source = SKBitmap.Decode(poster);
        using var surface = new SKBitmap(source.Width, source.Height);
        using var canvas = new SKCanvas(surface);
        canvas.DrawBitmap(source, 0, 0);

        var rects = BadgeLayout.Compute(source.Width, source.Height, logos.Count, settings.Layout);
        using var backdrop = new SKPaint { Color = SKColor.Parse(settings.BackdropColor), IsAntialias = true };
        using var logoPaint = new SKPaint { IsAntialias = true };
        for (var i = 0; i < rects.Count; i++)
        {
            var rect = rects[i];
            var radius = (float)(rect.Height * settings.CornerRadiusPercent / 100);
            var badge = new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
            canvas.DrawRoundRect(badge, radius, radius, backdrop);

            using var logo = SKBitmap.Decode(logos[i]);
            if (logo is not null)
            {
                canvas.DrawBitmap(logo, FitInside(badge, logo.Width, logo.Height), logoPaint);
            }
        }

        using var image = SKImage.FromBitmap(surface);
        return image.Encode(SKEncodedImageFormat.Jpeg, settings.JpegQuality).ToArray();
    }

    private static SKRect FitInside(SKRect badge, int logoWidth, int logoHeight)
    {
        var padding = badge.Height * 0.1f;
        var area = SKRect.Inflate(badge, -padding, -padding);
        var scale = Math.Min(area.Width / logoWidth, area.Height / logoHeight);
        var width = logoWidth * scale;
        var height = logoHeight * scale;
        return SKRect.Create(area.MidX - (width / 2), area.MidY - (height / 2), width, height);
    }
}
