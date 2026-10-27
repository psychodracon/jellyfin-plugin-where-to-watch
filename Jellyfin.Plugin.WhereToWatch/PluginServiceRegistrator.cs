using Jellyfin.Plugin.WhereToWatch.Logos;
using Jellyfin.Plugin.WhereToWatch.Middleware;
using Jellyfin.Plugin.WhereToWatch.Rendering;
using Jellyfin.Plugin.WhereToWatch.Scanning;
using Jellyfin.Plugin.WhereToWatch.State;
using Jellyfin.Plugin.WhereToWatch.Tmdb;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.WhereToWatch;

/// <summary>
/// Registers the plugin's services.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<TmdbClient>();
        serviceCollection.AddSingleton<LogoStore>();
        serviceCollection.AddSingleton<RenderCache>();
        serviceCollection.AddSingleton<ProviderScanner>();
        serviceCollection.AddSingleton(_ =>
        {
            var folder = Plugin.Instance!.DataFolderPath;
            Directory.CreateDirectory(folder);
            return new StateStore(Path.Combine(folder, "state.json"), TimeProvider.System);
        });
        serviceCollection.AddSingleton<IStartupFilter, BadgeStartupFilter>();
    }
}
