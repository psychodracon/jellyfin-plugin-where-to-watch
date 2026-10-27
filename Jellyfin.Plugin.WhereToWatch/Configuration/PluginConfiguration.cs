using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.WhereToWatch.Rendering;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WhereToWatch.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the TMDB API key (v3 key or v4 read access token).
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ISO country codes to look providers up in, primary first.
    /// </summary>
    public string[] Countries { get; set; } = ["PL", "GB"];

    /// <summary>
    /// Gets or sets the selected TMDB provider ids, in badge priority order.
    /// </summary>
    public int[] SelectedProviderIds { get; set; } = [];

    /// <summary>
    /// Gets or sets how many days a lookup stays valid.
    /// </summary>
    public int TtlDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets a value indicating whether stream tags are applied.
    /// </summary>
    public bool TagsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether badges are drawn on posters.
    /// </summary>
    public bool BadgesEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of badges per poster.
    /// </summary>
    public int MaxBadges { get; set; } = 3;

    /// <summary>
    /// Gets or sets the corner badges are anchored to.
    /// </summary>
    public BadgeCorner Corner { get; set; } = BadgeCorner.BottomLeft;

    /// <summary>
    /// Gets or sets the badge width as a percentage of the poster width.
    /// </summary>
    public double WidthPercent { get; set; } = 22;

    /// <summary>
    /// Gets or sets the badge width divided by its height.
    /// </summary>
    public double AspectRatio { get; set; } = 2.9;

    /// <summary>
    /// Gets or sets the margin as a percentage of the poster width.
    /// </summary>
    public double MarginPercent { get; set; } = 3;

    /// <summary>
    /// Gets or sets the gap between badges as a percentage of the poster width.
    /// </summary>
    public double SpacingPercent { get; set; } = 1;

    /// <summary>
    /// Gets or sets the backdrop colour as #AARRGGBB.
    /// </summary>
    public string BackdropColor { get; set; } = "#99000000";

    /// <summary>
    /// Gets or sets the backdrop corner radius as a percentage of the badge height.
    /// </summary>
    public double CornerRadiusPercent { get; set; } = 25;

    /// <summary>
    /// Gets or sets the JPEG quality of rendered posters.
    /// </summary>
    public int JpegQuality { get; set; } = 90;

    /// <summary>
    /// Builds the render settings from this configuration.
    /// </summary>
    /// <returns>The render settings.</returns>
    public RenderSettings ToRenderSettings() => new(
        new LayoutSettings(Corner, WidthPercent, AspectRatio, MarginPercent, SpacingPercent),
        BackdropColor,
        CornerRadiusPercent,
        Math.Clamp(JpegQuality, 1, 100));

    /// <summary>
    /// Gets a fingerprint of everything that changes how a poster looks, so cached renders go stale when it changes.
    /// </summary>
    /// <returns>A short hex string.</returns>
    public string RenderFingerprint()
    {
        var text = string.Join(
            '|',
            Corner,
            WidthPercent.ToString(CultureInfo.InvariantCulture),
            AspectRatio.ToString(CultureInfo.InvariantCulture),
            MarginPercent.ToString(CultureInfo.InvariantCulture),
            SpacingPercent.ToString(CultureInfo.InvariantCulture),
            BackdropColor,
            CornerRadiusPercent.ToString(CultureInfo.InvariantCulture),
            JpegQuality,
            MaxBadges,
            string.Join(',', SelectedProviderIds));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
