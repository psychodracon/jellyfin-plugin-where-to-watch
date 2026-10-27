using Jellyfin.Plugin.WhereToWatch.Rendering;
using SkiaSharp;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class BadgeRendererTests
{
    // 200x300 grey poster, one badge: 40% wide at aspect 2 = 80x40, margin 5% = 10px, so it sits at (10, 250).
    private static readonly RenderSettings Settings = new(
        new LayoutSettings(BadgeCorner.BottomLeft, 40, 2, 5, 1),
        BackdropColor: "#99000000",
        CornerRadiusPercent: 20,
        JpegQuality: 95);

    [Fact]
    public void Output_is_a_jpeg_of_the_same_size_with_only_the_badge_area_darkened()
    {
        var poster = Png(200, 300, new SKColor(128, 128, 128));
        var logo = Png(40, 40, SKColors.Red);

        var output = BadgeRenderer.Render(poster, [logo], Settings with { Format = OutputFormat.Jpeg });

        Assert.Equal("image/jpeg", output.ContentType);
        Assert.Equal([0xFF, 0xD8], output.Bytes[..2]);
        using var result = SKBitmap.Decode(output.Bytes);
        Assert.Equal((200, 300), (result.Width, result.Height));
        Assert.InRange(result.GetPixel(190, 10).Red, 122, 134);
        var backdrop = result.GetPixel(14, 270);
        Assert.InRange(backdrop.Red, 40, 65);
        Assert.InRange(backdrop.Green, 40, 65);
    }

    [Fact]
    public void Auto_format_keeps_the_format_of_the_poster_it_was_given()
    {
        var logo = Png(40, 40, SKColors.Red);
        var grey = new SKColor(128, 128, 128);

        var fromPng = BadgeRenderer.Render(Png(200, 300, grey), [logo], Settings);
        var fromJpeg = BadgeRenderer.Render(Jpeg(200, 300, grey), [logo], Settings);
        var fromWebp = BadgeRenderer.Render(Webp(200, 300, grey), [logo], Settings);

        Assert.Equal("image/png", fromPng.ContentType);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], fromPng.Bytes[..4]);
        Assert.Equal("image/jpeg", fromJpeg.ContentType);
        Assert.Equal([0xFF, 0xD8], fromJpeg.Bytes[..2]);
        Assert.Equal("image/webp", fromWebp.ContentType);
        Assert.Equal("RIFF"u8.ToArray(), fromWebp.Bytes[..4]);
    }

    [Theory]
    [InlineData(OutputFormat.Png, "image/png")]
    [InlineData(OutputFormat.Webp, "image/webp")]
    [InlineData(OutputFormat.Jpeg, "image/jpeg")]
    public void Explicit_format_overrides_the_poster_format(OutputFormat format, string contentType)
    {
        var output = BadgeRenderer.Render(Jpeg(200, 300, new SKColor(128, 128, 128)), [Png(40, 40, SKColors.Red)], Settings with { Format = format });

        Assert.Equal(contentType, output.ContentType);
        using var decoded = SKBitmap.Decode(output.Bytes);
        Assert.Equal((200, 300), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void Jpeg_output_keeps_colour_detail_at_the_edges_of_the_logo()
    {
        // Red next to green: with 4:2:0 chroma subsampling the colours bleed across the edge.
        // The edge sits on an odd pixel so it cannot line up with the 2x2 chroma blocks.
        using var half = new SKBitmap(40, 40);
        half.Erase(SKColors.Lime);
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 21; x++)
            {
                half.SetPixel(x, y, SKColors.Red);
            }
        }

        using var logo = SKImage.FromBitmap(half);
        var output = BadgeRenderer.Render(Png(200, 300, new SKColor(128, 128, 128)), [logo.Encode(SKEncodedImageFormat.Png, 100).ToArray()], Settings with { Format = OutputFormat.Jpeg, PaddingPercent = 0 });

        // The logo spans x 30..70, so its last red column is x=50 and its first green column is x=51.
        using var result = SKBitmap.Decode(output.Bytes);
        var red = result.GetPixel(50, 270);
        var green = result.GetPixel(51, 270);
        Assert.True(red.Red > 200 && red.Green < 60, $"red side bled: {red}");
        Assert.True(green.Green > 200 && green.Red < 60, $"green side bled: {green}");
    }

    [Fact]
    public void Downscaled_logo_is_smoothed_instead_of_picking_single_pixels()
    {
        // 1px black/white stripes scaled down 5x must average to grey; point sampling would give pure black or white.
        using var stripes = new SKBitmap(200, 200);
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                stripes.SetPixel(x, y, x % 2 == 0 ? SKColors.Black : SKColors.White);
            }
        }

        using var logo = SKImage.FromBitmap(stripes);
        var output = BadgeRenderer.Render(Png(200, 300, new SKColor(128, 128, 128)), [logo.Encode(SKEncodedImageFormat.Png, 100).ToArray()], Settings with { PaddingPercent = 0, LogoCornerRadiusPercent = 0 });

        // Padding 0 puts the 40x40 logo at x 30..70, y 250..290.
        using var result = SKBitmap.Decode(output.Bytes);
        var values = Enumerable.Range(40, 8).Select(x => (int)result.GetPixel(x, 270).Red).ToList();
        Assert.All(values, v => Assert.InRange(v, 80, 180));
    }

    [Fact]
    public void Logo_corner_radius_rounds_the_logo_independently_of_the_backdrop()
    {
        var poster = Png(200, 300, new SKColor(128, 128, 128));
        var squareLogo = Png(40, 40, SKColors.Red);

        // Padding 0: the logo fills the badge height, spanning x 30..70, y 250..290. Pixel (32,252) is in its top-left corner.
        var sharp = BadgeRenderer.Render(poster, [squareLogo], Settings with { PaddingPercent = 0, LogoCornerRadiusPercent = 0 });
        var round = BadgeRenderer.Render(poster, [squareLogo], Settings with { PaddingPercent = 0, LogoCornerRadiusPercent = 50 });

        using var sharpImage = SKBitmap.Decode(sharp.Bytes);
        using var roundImage = SKBitmap.Decode(round.Bytes);
        Assert.True(sharpImage.GetPixel(32, 252).Red > 200, $"expected a square corner, got {sharpImage.GetPixel(32, 252)}");
        Assert.True(roundImage.GetPixel(32, 252).Red < 120, $"expected a rounded corner, got {roundImage.GetPixel(32, 252)}");
        Assert.True(roundImage.GetPixel(50, 270).Red > 200, "the middle of the logo must stay visible");
    }

    [Fact]
    public void Logo_is_drawn_centred_in_the_badge_without_stretching()
    {
        var poster = Png(200, 300, new SKColor(128, 128, 128));
        var squareLogo = Png(40, 40, SKColors.Red);

        var output = BadgeRenderer.Render(poster, [squareLogo], Settings);

        // Badge is (10,250) 80x40; with 10% padding the square logo is 32x32 at x 34..66, y 254..286.
        using var result = SKBitmap.Decode(output.Bytes);
        var centre = result.GetPixel(50, 270);
        Assert.True(centre.Red > 200 && centre.Green < 60 && centre.Blue < 60, $"expected red logo, got {centre}");
        var besideLogo = result.GetPixel(20, 270);
        Assert.True(besideLogo.Red < 80, $"logo stretched over the whole badge: {besideLogo}");
    }

    [Fact]
    public void Padding_setting_controls_how_much_backdrop_surrounds_the_logo()
    {
        var poster = Png(200, 300, new SKColor(128, 128, 128));
        var squareLogo = Png(40, 40, SKColors.Red);

        // Badge is (10,250) 80x40. Pixel (50,253) is 3px below the badge top, inside the logo only when there is no padding.
        using var tight = SKBitmap.Decode(BadgeRenderer.Render(poster, [squareLogo], Settings with { PaddingPercent = 0 }).Bytes);
        using var loose = SKBitmap.Decode(BadgeRenderer.Render(poster, [squareLogo], Settings with { PaddingPercent = 25 }).Bytes);

        Assert.True(tight.GetPixel(50, 253).Red > 200, $"expected logo up to the edge, got {tight.GetPixel(50, 253)}");
        Assert.True(loose.GetPixel(50, 253).Red < 120, $"expected backdrop around the logo, got {loose.GetPixel(50, 253)}");
    }

    private static byte[] Png(int width, int height, SKColor color) => Encode(width, height, color, SKEncodedImageFormat.Png);

    private static byte[] Jpeg(int width, int height, SKColor color) => Encode(width, height, color, SKEncodedImageFormat.Jpeg);

    private static byte[] Webp(int width, int height, SKColor color) => Encode(width, height, color, SKEncodedImageFormat.Webp);

    private static byte[] Encode(int width, int height, SKColor color, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, 100).ToArray();
    }
}
