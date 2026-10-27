using Jellyfin.Plugin.WhereToWatch.Configuration;
using Jellyfin.Plugin.WhereToWatch.State;
using Jellyfin.Plugin.WhereToWatch.Tags;
using Jellyfin.Plugin.WhereToWatch.Tmdb;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
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
    public ActionResult<PluginConfiguration> GetConfig()
    {
        var config = Plugin.Instance!.Configuration;
        return new PluginConfiguration
        {
            TmdbApiKey = string.IsNullOrEmpty(config.TmdbApiKey) ? string.Empty : "********",
            Countries = config.Countries,
            SelectedProviderIds = config.SelectedProviderIds,
            TtlDays = config.TtlDays,
            TagsEnabled = config.TagsEnabled,
            BadgesEnabled = config.BadgesEnabled,
            MaxBadges = config.MaxBadges,
            Corner = config.Corner,
            WidthPercent = config.WidthPercent,
            AspectRatio = config.AspectRatio,
            MarginPercent = config.MarginPercent,
            SpacingPercent = config.SpacingPercent,
            BackdropColor = config.BackdropColor,
            CornerRadiusPercent = config.CornerRadiusPercent,
            JpegQuality = config.JpegQuality,
        };
    }

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

        var region = string.IsNullOrWhiteSpace(country) ? config.Countries.FirstOrDefault() ?? "US" : country;
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
    /// Shows how one item was resolved.
    /// </summary>
    /// <param name="id">Jellyfin item id.</param>
    /// <returns>The item's tags and recorded state.</returns>
    [HttpGet("items/{id}")]
    public ActionResult<object> GetItem([FromRoute] Guid id)
    {
        var item = _libraryManager.GetItemById(id);
        if (item is null)
        {
            return NotFound();
        }

        var config = Plugin.Instance!.Configuration;
        var state = _state.Get(id);
        return new
        {
            item.Id,
            item.Name,
            StreamTags = (item.Tags ?? []).Where(t => t.StartsWith(TagDiff.Prefix, StringComparison.OrdinalIgnoreCase)).ToArray(),
            State = state,
            Badges = state?.ProviderIds.Take(Math.Max(0, config.MaxBadges)).ToArray() ?? [],
        };
    }

    /// <summary>
    /// Summarizes recorded state and the latest run.
    /// </summary>
    /// <returns>The summary.</returns>
    [HttpGet("state")]
    public ActionResult<StateSummary> GetState() => _state.Summary();
}
