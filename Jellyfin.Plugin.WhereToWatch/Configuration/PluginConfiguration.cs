using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WhereToWatch.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the TMDB API key.
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;
}
