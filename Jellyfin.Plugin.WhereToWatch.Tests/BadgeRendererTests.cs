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

        var output = BadgeRenderer.Render(poster, [logo], Settings);

        Assert.Equal([0xFF, 0xD8], output[..2]);
        using var result = SKBitmap.Decode(output);
        Assert.Equal((200, 300), (result.Width, result.Height));
        Assert.InRange(result.GetPixel(190, 10).Red, 122, 134);
        var backdrop = result.GetPixel(14, 270);
        Assert.InRange(backdrop.Red, 40, 65);
        Assert.InRange(backdrop.Green, 40, 65);
    }

    [Fact]
    public void Logo_is_drawn_centred_in_the_badge_without_stretching()
    {
        var poster = Png(200, 300, new SKColor(128, 128, 128));
        var squareLogo = Png(40, 40, SKColors.Red);

        var output = BadgeRenderer.Render(poster, [squareLogo], Settings);

        // Badge is (10,250) 80x40; with 10% padding the square logo is 32x32 at x 34..66, y 254..286.
        using var result = SKBitmap.Decode(output);
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
        using var tight = SKBitmap.Decode(BadgeRenderer.Render(poster, [squareLogo], Settings with { PaddingPercent = 0 }));
        using var loose = SKBitmap.Decode(BadgeRenderer.Render(poster, [squareLogo], Settings with { PaddingPercent = 25 }));

        Assert.True(tight.GetPixel(50, 253).Red > 200, $"expected logo up to the edge, got {tight.GetPixel(50, 253)}");
        Assert.True(loose.GetPixel(50, 253).Red < 120, $"expected backdrop around the logo, got {loose.GetPixel(50, 253)}");
    }

    private static byte[] Png(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
