using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.WhereToWatch.Resolution;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WhereToWatch.Tmdb;

/// <summary>
/// A provider as listed by TMDB for a country.
/// </summary>
/// <param name="Id">Provider id.</param>
/// <param name="Name">Provider name.</param>
/// <param name="LogoPath">TMDB logo path, if any.</param>
/// <param name="Priority">TMDB display priority in the requested country (lower is more prominent).</param>
public sealed record TmdbProvider(int Id, string Name, string? LogoPath, int Priority);

/// <summary>
/// Minimal TMDB client for watch providers.
/// </summary>
public sealed class TmdbClient : IDisposable
{
    private const string ApiBase = "https://api.themoviedb.org/3";
    private const int MaxRetries = 4;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // A plain HttpClient on purpose: IHttpClientFactory logs full request URLs, which would put the api_key in the log.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _limiter = new(4);
    private readonly ILogger<TmdbClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public TmdbClient(ILogger<TmdbClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Lists the flatrate providers TMDB knows for a country, for both movies and series.
    /// </summary>
    /// <param name="apiKey">TMDB key or read access token.</param>
    /// <param name="country">ISO country code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Providers ordered by TMDB display priority.</returns>
    public async Task<IReadOnlyList<TmdbProvider>> GetProvidersAsync(string apiKey, string country, CancellationToken cancellationToken)
    {
        var byId = new Dictionary<int, TmdbProvider>();
        foreach (var kind in new[] { "movie", "tv" })
        {
            var response = await GetAsync<ProviderListResponse>(apiKey, $"/watch/providers/{kind}?watch_region={Uri.EscapeDataString(country)}", cancellationToken).ConfigureAwait(false);
            foreach (var p in response?.Results ?? [])
            {
                var priority = p.DisplayPriorities is not null && p.DisplayPriorities.TryGetValue(country.ToUpperInvariant(), out var value) ? value : int.MaxValue;
                if (!byId.TryGetValue(p.ProviderId, out var existing) || priority < existing.Priority)
                {
                    byId[p.ProviderId] = new TmdbProvider(p.ProviderId, p.ProviderName, p.LogoPath, priority);
                }
            }
        }

        return byId.Values.OrderBy(p => p.Priority).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Gets the flatrate providers of a title in every country TMDB has data for.
    /// </summary>
    /// <param name="apiKey">TMDB key or read access token.</param>
    /// <param name="isSeries">True for a series, false for a movie.</param>
    /// <param name="tmdbId">TMDB id of the title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Flatrate providers per country, or null when TMDB does not know the title.</returns>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ProviderRef>>?> GetFlatrateAsync(string apiKey, bool isSeries, int tmdbId, CancellationToken cancellationToken)
    {
        var kind = isSeries ? "tv" : "movie";
        var response = await GetAsync<TitleProvidersResponse>(apiKey, $"/{kind}/{tmdbId.ToString(CultureInfo.InvariantCulture)}/watch/providers", cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        var result = new Dictionary<string, IReadOnlyList<ProviderRef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (country, region) in response.Results ?? [])
        {
            result[country] = (region.Flatrate ?? []).Select(p => new ProviderRef(p.ProviderId, p.ProviderName)).ToList();
        }

        return result;
    }

    /// <summary>
    /// Downloads a provider logo.
    /// </summary>
    /// <param name="logoPath">TMDB logo path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image bytes, or null when the download failed.</returns>
    public async Task<byte[]?> DownloadLogoAsync(string logoPath, CancellationToken cancellationToken)
    {
        await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _http.GetByteArrayAsync(new Uri("https://image.tmdb.org/t/p/original" + logoPath), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not download TMDB logo {LogoPath}", logoPath);
            return null;
        }
        finally
        {
            _limiter.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _limiter.Dispose();
    }

    private static HttpRequestMessage BuildRequest(string apiKey, string path)
    {
        // v4 read access tokens are JWTs and go in a header; v3 keys go in the query string.
        if (apiKey.StartsWith("eyJ", StringComparison.Ordinal))
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return request;
        }

        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}{path}{separator}api_key={Uri.EscapeDataString(apiKey)}");
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }

    private async Task<T?> GetAsync<T>(string apiKey, string path, CancellationToken cancellationToken)
    {
        await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = BuildRequest(apiKey, path);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return default;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
                {
                    var delay = RetryDelay(response, attempt);
                    _logger.LogInformation("TMDB rate limit hit, waiting {Delay}", delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _limiter.Release();
        }
    }

    private sealed class ProviderListResponse
    {
        public List<ProviderEntry>? Results { get; set; }
    }

    private sealed class ProviderEntry
    {
        [JsonPropertyName("provider_id")]
        public int ProviderId { get; set; }

        [JsonPropertyName("provider_name")]
        public string ProviderName { get; set; } = string.Empty;

        [JsonPropertyName("logo_path")]
        public string? LogoPath { get; set; }

        [JsonPropertyName("display_priorities")]
        public Dictionary<string, int>? DisplayPriorities { get; set; }
    }

    private sealed class TitleProvidersResponse
    {
        public Dictionary<string, RegionEntry>? Results { get; set; }
    }

    private sealed class RegionEntry
    {
        public List<ProviderEntry>? Flatrate { get; set; }
    }
}
