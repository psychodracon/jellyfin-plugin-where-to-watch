using Jellyfin.Plugin.WhereToWatch.Links;
using Jellyfin.Plugin.WhereToWatch.Logos;
using Jellyfin.Plugin.WhereToWatch.State;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WhereToWatch.Api;

/// <summary>
/// One provider in the "Watch on" row.
/// </summary>
/// <param name="Id">TMDB provider id.</param>
/// <param name="Name">Provider name.</param>
/// <param name="Url">Direct link to the title at this provider, when direct links are enabled and TMDB's page has one; otherwise the TMDB page link is used.</param>
public sealed record WatchOnProvider(int Id, string Name, string? Url);

/// <summary>
/// What the details page shows for an item.
/// </summary>
/// <param name="Country">Country the providers were found in.</param>
/// <param name="Link">TMDB watch page (powered by JustWatch); every logo and the credit link to it, because TMDB's API gives no per-provider links.</param>
/// <param name="Providers">Providers in priority order.</param>
public sealed record WatchOnResponse(string? Country, string? Link, IReadOnlyList<WatchOnProvider> Providers);

/// <summary>
/// Endpoints used by the "Watch on" row of the web UI.
/// </summary>
[ApiController]
[Route("WhereToWatch")]
public sealed class WatchOnController : ControllerBase
{
    private readonly StateStore _state;
    private readonly ILibraryManager _libraryManager;
    private readonly LogoStore _logos;
    private readonly DirectLinkService _directLinks;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchOnController"/> class.
    /// </summary>
    /// <param name="state">State store.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="logos">Logo store.</param>
    /// <param name="directLinks">Direct provider links, used only when enabled.</param>
    public WatchOnController(StateStore state, ILibraryManager libraryManager, LogoStore logos, DirectLinkService directLinks)
    {
        _state = state;
        _libraryManager = libraryManager;
        _logos = logos;
        _directLinks = directLinks;
    }

    /// <summary>
    /// Gets the providers to show for an item.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <returns>The providers, or an empty list when there is nothing to show.</returns>
    [HttpGet("watch/{itemId}")]
    [Authorize]
    public async Task<ActionResult<WatchOnResponse>> GetWatchOn([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var empty = new WatchOnResponse(null, null, []);
        var state = _state.Get(itemId);
        var item = _libraryManager.GetItemById(itemId);
        if (!config.WatchOnEnabled || state is null || item is null || state.ProviderIds.Count == 0)
        {
            return empty;
        }

        // Direct links are opt-in; without them (or when they cannot be found) every logo uses the TMDB page link.
        var direct = config.DirectLinksEnabled
            ? await _directLinks.GetAsync(itemId, TimeSpan.FromDays(Math.Max(1, config.TtlDays)), cancellationToken).ConfigureAwait(false)
            : null;

        var providers = state.ProviderIds
            .Select((id, index) => new WatchOnProvider(
                id,
                index < state.ProviderNames.Count ? state.ProviderNames[index] : string.Empty,
                direct is not null && direct.TryGetValue(id, out var url) ? url : null))
            .ToList();
        return new WatchOnResponse(state.Country, state.Link, providers);
    }

    /// <summary>
    /// Serves a provider logo. Anonymous, because the browser loads it through an img tag that cannot send credentials.
    /// The URL deliberately has no file extension, like Jellyfin's own image URLs, so proxies treat it as an API call.
    /// </summary>
    /// <param name="providerId">TMDB provider id.</param>
    /// <returns>The logo image.</returns>
    [HttpGet("logo/{providerId}")]
    [AllowAnonymous]
    public ActionResult GetLogo([FromRoute] int providerId)
    {
        var bytes = _logos.Load(providerId);
        if (bytes is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(bytes, ContentTypeOf(bytes));
    }

    private static string ContentTypeOf(byte[] image) => image switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, ..] => "image/webp",
        _ => "application/octet-stream",
    };
}
