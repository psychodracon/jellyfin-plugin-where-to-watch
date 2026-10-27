using SkiaSharp;

namespace Jellyfin.Plugin.WhereToWatch.Rendering;

/// <summary>
/// Format of the rendered poster.
/// </summary>
public enum OutputFormat
{
    /// <summary>Same format as the poster Jellyfin served, so clients keep getting a format they accept.</summary>
    Auto,

    /// <summary>JPEG, lossy.</summary>
    Jpeg,

    /// <summary>PNG, lossless but large.</summary>
    Png,

    /// <summary>WebP, lossy below quality 100 and lossless at 100.</summary>
    Webp,
}

/// <summary>
/// A rendered poster.
/// </summary>
/// <param name="Bytes">Encoded image.</param>
/// <param name="ContentType">MIME type of <paramref name="Bytes"/>.</param>
public sealed record RenderedImage(byte[] Bytes, string ContentType);

/// <summary>
/// Settings for drawing badges.
/// </summary>
/// <param name="Layout">Where badges go.</param>
/// <param name="BackdropColor">Backdrop colour as #AARRGGBB or #RRGGBB.</param>
/// <param name="CornerRadiusPercent">Backdrop corner radius as a percentage of the badge height.</param>
/// <param name="JpegQuality">Quality for JPEG and lossy WebP output, 1-100.</param>
/// <param name="PaddingPercent">Space between the backdrop edge and the logo, as a percentage of the badge height.</param>
/// <param name="LogoCornerRadiusPercent">Logo corner radius as a percentage of the logo's shorter side; 50 makes a circle.</param>
/// <param name="Format">Output format.</param>
public sealed record RenderSettings(
    LayoutSettings Layout,
    string BackdropColor,
    double CornerRadiusPercent,
    int JpegQuality,
    double PaddingPercent = 10,
    double LogoCornerRadiusPercent = 0,
    OutputFormat Format = OutputFormat.Auto);

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
    /// <returns>The badged poster and its content type.</returns>
    public static RenderedImage Render(byte[] poster, IReadOnlyList<byte[]> logos, RenderSettings settings)
    {
        var sourceFormat = DetectFormat(poster);
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
                DrawLogo(canvas, logo, logoPaint, FitInside(badge, logo.Width, logo.Height, settings.PaddingPercent), settings.LogoCornerRadiusPercent);
            }
        }

        return Encode(surface, settings, sourceFormat);
    }

    private static void DrawLogo(SKCanvas canvas, SKBitmap logo, SKPaint paint, SKRect target, double cornerRadiusPercent)
    {
        var radius = (float)(Math.Min(target.Width, target.Height) * Math.Clamp(cornerRadiusPercent, 0, 50) / 100);
        canvas.Save();
        if (radius > 0)
        {
            canvas.ClipRoundRect(new SKRoundRect(target, radius, radius), antialias: true);
        }

        // Mipmapped linear sampling averages pixels when the logo is scaled down; the default would pick single pixels.
        using var image = SKImage.FromBitmap(logo);
        canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        canvas.Restore();
    }

    private static SKEncodedImageFormat DetectFormat(byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var codec = SKCodec.Create(stream);
        return codec?.EncodedFormat ?? SKEncodedImageFormat.Jpeg;
    }

    private static RenderedImage Encode(SKBitmap surface, RenderSettings settings, SKEncodedImageFormat sourceFormat)
    {
        var format = settings.Format switch
        {
            OutputFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            OutputFormat.Png => SKEncodedImageFormat.Png,
            OutputFormat.Webp => SKEncodedImageFormat.Webp,
            _ => sourceFormat is SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp ? sourceFormat : SKEncodedImageFormat.Jpeg,
        };
        var quality = Math.Clamp(settings.JpegQuality, 1, 100);

        using var pixels = surface.PeekPixels();
        using var output = new SKDynamicMemoryWStream();
        switch (format)
        {
            case SKEncodedImageFormat.Png:
                pixels.Encode(output, SKPngEncoderOptions.Default);
                return new RenderedImage(output.DetachAsData().ToArray(), "image/png");
            case SKEncodedImageFormat.Webp:
                var compression = quality >= 100 ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy;
                pixels.Encode(output, new SKWebpEncoderOptions(compression, quality));
                return new RenderedImage(output.DetachAsData().ToArray(), "image/webp");
            default:
                // 4:4:4 keeps colour edges (logos) crisp; the default 4:2:0 smears them.
                pixels.Encode(output, new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore));
                return new RenderedImage(output.DetachAsData().ToArray(), "image/jpeg");
        }
    }

    private static SKRect FitInside(SKRect badge, int logoWidth, int logoHeight, double paddingPercent)
    {
        var padding = (float)(badge.Height * Math.Clamp(paddingPercent, 0, 40) / 100);
        var area = SKRect.Inflate(badge, -padding, -padding);
        var scale = Math.Min(area.Width / logoWidth, area.Height / logoHeight);
        var width = logoWidth * scale;
        var height = logoHeight * scale;
        return SKRect.Create(area.MidX - (width / 2), area.MidY - (height / 2), width, height);
    }
}
