using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using Jellyfin.Plugin.WhereToWatch.Rendering;
using Jellyfin.Plugin.WhereToWatch.State;
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
    public string[] Countries { get; set; } = ["PL", "US"];

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
    public double WidthPercent { get; set; } = 30;

    /// <summary>
    /// Gets or sets the badge width divided by its height.
    /// </summary>
    public double AspectRatio { get; set; } = 1.1;

    /// <summary>
    /// Gets or sets the margin as a percentage of the poster width.
    /// </summary>
    public double MarginPercent { get; set; } = 3;

    /// <summary>
    /// Gets or sets the gap between badges as a percentage of the poster width.
    /// </summary>
    public double SpacingPercent { get; set; } = 3;

    /// <summary>
    /// Gets or sets the backdrop colour as #AARRGGBB.
    /// </summary>
    public string BackdropColor { get; set; } = "#99000000";

    /// <summary>
    /// Gets or sets the backdrop corner radius as a percentage of the badge height.
    /// </summary>
    public double CornerRadiusPercent { get; set; } = 10;

    /// <summary>
    /// Gets or sets the space between the backdrop edge and the logo, as a percentage of the badge height.
    /// </summary>
    public double PaddingPercent { get; set; } = 4;

    /// <summary>
    /// Gets or sets the logo corner radius as a percentage of the logo's shorter side; 50 makes a circle.
    /// </summary>
    public double LogoCornerRadiusPercent { get; set; } = 8;

    /// <summary>
    /// Gets or sets the format of rendered posters. Auto keeps the format Jellyfin served.
    /// </summary>
    public OutputFormat Format { get; set; } = OutputFormat.Auto;

    /// <summary>
    /// Gets or sets the quality of JPEG and lossy WebP output; 100 makes WebP lossless.
    /// </summary>
    public int JpegQuality { get; set; } = 95;

    /// <summary>
    /// Gets the primary country, the first in <see cref="Countries"/>.
    /// </summary>
    [XmlIgnore]
    public string PrimaryCountry => Countries.FirstOrDefault() ?? "US";

    /// <summary>
    /// Builds the render settings from this configuration.
    /// </summary>
    /// <returns>The render settings.</returns>
    public RenderSettings ToRenderSettings() => new(
        new LayoutSettings(Corner, WidthPercent, AspectRatio, MarginPercent, SpacingPercent),
        BackdropColor,
        CornerRadiusPercent,
        Math.Clamp(JpegQuality, 1, 100),
        PaddingPercent,
        LogoCornerRadiusPercent,
        Format);

    /// <summary>
    /// Picks the providers whose badges are drawn for an item.
    /// </summary>
    /// <param name="state">The item's recorded state, if any.</param>
    /// <returns>Provider ids in priority order, capped at <see cref="MaxBadges"/>.</returns>
    public IReadOnlyList<int> BadgeProviderIds(ItemState? state)
        => state?.ProviderIds.Take(Math.Max(0, MaxBadges)).ToList() ?? [];

    /// <summary>
    /// Returns a copy that is safe to show in an API response: the TMDB key is masked.
    /// </summary>
    /// <returns>The copy.</returns>
    public PluginConfiguration Redacted()
    {
        var copy = (PluginConfiguration)MemberwiseClone();
        copy.TmdbApiKey = string.IsNullOrEmpty(TmdbApiKey) ? string.Empty : "********";
        return copy;
    }

    /// <summary>
    /// Gets a fingerprint of everything that changes how a poster looks, so cached renders go stale when it changes.
    /// </summary>
    /// <returns>A short hex string.</returns>
    public string RenderFingerprint()
    {
        var text = JsonSerializer.Serialize(new { Settings = ToRenderSettings(), MaxBadges, SelectedProviderIds });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>
    /// Gets a fingerprint of the settings that decide what an item resolves to, so a change triggers a re-check.
    /// </summary>
    /// <returns>A short hex string.</returns>
    public string ResolutionFingerprint()
    {
        var text = JsonSerializer.Serialize(new { Countries, SelectedProviderIds, TagsEnabled });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
