using Jellyfin.Plugin.WhereToWatch.Configuration;
using Jellyfin.Plugin.WhereToWatch.State;
using Jellyfin.Plugin.WhereToWatch.Tags;
using Jellyfin.Plugin.WhereToWatch.Tmdb;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WhereToWatch.Api;

/// <summary>
/// Admin-only endpoints for inspecting the plugin.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("WhereToWatch")]
public sealed class WhereToWatchController : ControllerBase
{
    private readonly TmdbClient _tmdb;
    private readonly StateStore _state;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhereToWatchController"/> class.
    /// </summary>
    /// <param name="tmdb">TMDB client.</param>
    /// <param name="state">State store.</param>
    /// <param name="libraryManager">Library manager.</param>
    public WhereToWatchController(TmdbClient tmdb, StateStore state, ILibraryManager libraryManager)
    {
        _tmdb = tmdb;
        _state = state;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets the effective configuration with the TMDB key redacted.
    /// </summary>
    /// <returns>The configuration.</returns>
    [HttpGet("config")]
    public ActionResult<PluginConfiguration> GetConfig() => Plugin.Instance!.Configuration.Redacted();

    /// <summary>
    /// Lists the flatrate providers TMDB offers in the primary country, using the stored key.
    /// </summary>
    /// <param name="country">Optional country override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The providers ordered by TMDB display priority.</returns>
    [HttpGet("tmdb/providers")]
    public async Task<ActionResult<IReadOnlyList<TmdbProvider>>> GetTmdbProviders([FromQuery] string? country, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            return BadRequest("Set a TMDB API key first.");
        }

        var region = string.IsNullOrWhiteSpace(country) ? config.PrimaryCountry : country;
        try
        {
            return Ok(await _tmdb.GetProvidersAsync(config.TmdbApiKey, region, cancellationToken).ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, "TMDB request failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Shows how one item was resolved, including what TMDB reports right now for each configured country.
    /// </summary>
    /// <param name="id">Jellyfin item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item's tags, recorded state, chosen badges and live per-country providers.</returns>
    [HttpGet("items/{id}")]
    public async Task<ActionResult<object>> GetItem([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(id);
        if (item is null)
        {
            return NotFound();
        }

        var config = Plugin.Instance!.Configuration;
        var state = _state.Get(id);
        var tmdbId = state?.TmdbId ?? (item.TryGetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Tmdb, out var text) && int.TryParse(text, out var parsed) ? parsed : (int?)null);

        object? providersByCountry = null;
        string? tmdbError = null;
        if (tmdbId is not null && !string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            try
            {
                var flatrate = await _tmdb.GetFlatrateAsync(config.TmdbApiKey, item is Series, tmdbId.Value, cancellationToken).ConfigureAwait(false);
                providersByCountry = config.Countries.ToDictionary(
                    c => c.ToUpperInvariant(),
                    c => flatrate is not null && flatrate.TryGetValue(c, out var offered)
                        ? offered.Select(p => new { p.Id, p.Name, Selected = config.SelectedProviderIds.Contains(p.Id) }).ToArray()
                        : []);
            }
            catch (HttpRequestException ex)
            {
                tmdbError = ex.Message;
            }
        }

        return new
        {
            item.Id,
            item.Name,
            TmdbId = tmdbId,
            StreamTags = (item.Tags ?? []).Where(TagDiff.IsManaged).ToArray(),
            State = state,
            Badges = config.BadgeProviderIds(state),
            ProvidersByCountry = providersByCountry,
            TmdbError = tmdbError,
        };
    }

    /// <summary>
    /// Summarizes recorded state and the latest run.
    /// </summary>
    /// <returns>The summary.</returns>
    [HttpGet("state")]
    public ActionResult<StateSummary> GetState() => _state.Summary();
}
