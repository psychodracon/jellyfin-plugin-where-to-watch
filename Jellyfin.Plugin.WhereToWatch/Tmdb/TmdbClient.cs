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
    private const int MaxRedirects = 5;
    private const long MaxPageBytes = 3 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // A plain HttpClient on purpose: IHttpClientFactory logs full request URLs, which would put the api_key in the log.
    // Redirects are followed by hand (see GetWatchPageAsync) so every hop can be checked.
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _limiter = new(4);
    private readonly ILogger<TmdbClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public TmdbClient(ILogger<TmdbClient> logger)
        : this(logger, new HttpClientHandler { AllowAutoRedirect = false })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class with a custom message handler.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="handler">The handler requests go through; it must not follow redirects itself.</param>
    internal TmdbClient(ILogger<TmdbClient> logger, HttpMessageHandler handler)
    {
        _logger = logger;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
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
    /// <returns>Flatrate providers and watch-page links per country, or null when TMDB does not know the title.</returns>
    public async Task<TitleAvailability?> GetFlatrateAsync(string apiKey, bool isSeries, int tmdbId, CancellationToken cancellationToken)
    {
        var kind = isSeries ? "tv" : "movie";
        var response = await GetAsync<TitleProvidersResponse>(apiKey, $"/{kind}/{tmdbId.ToString(CultureInfo.InvariantCulture)}/watch/providers", cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        var flatrate = new Dictionary<string, IReadOnlyList<ProviderRef>>(StringComparer.OrdinalIgnoreCase);
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (country, region) in response.Results ?? [])
        {
            flatrate[country] = (region.Flatrate ?? []).Select(p => new ProviderRef(p.ProviderId, p.ProviderName)).ToList();
            if (!string.IsNullOrWhiteSpace(region.Link))
            {
                links[country] = region.Link;
            }
        }

        return new TitleAvailability(flatrate, links);
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
            return await _http.GetByteArrayAsync(new Uri("https://image.tmdb.org/t/p/w500" + logoPath), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not download TMDB logo {LogoPath}", logoPath);
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Downloading TMDB logo {LogoPath} timed out", logoPath);
            return null;
        }
        finally
        {
            _limiter.Release();
        }
    }

    /// <summary>
    /// Downloads a TMDB watch page. Used only for the optional direct provider links.
    /// </summary>
    /// <param name="pageUrl">The watch page URL TMDB's API returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page HTML, or null when the URL is not a TMDB page or the request failed.</returns>
        public async Task<string?> GetWatchPageAsync(string pageUrl, CancellationToken cancellationToken)
    {
        // The URL comes from a response, so only ever fetch https pages on TMDB's own site.
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri) || !IsTmdbPage(uri))
        {
            return null;
        }

        await _limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Jellyfin-WhereToWatch/1.0 (personal media server)");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    // TMDB redirects to the canonical slug URL; follow it, but only while it stays on TMDB.
                    var location = response.Headers.Location;
                    if (location is null || !Uri.TryCreate(uri, location, out var next) || !IsTmdbPage(next))
                    {
                        _logger.LogInformation("TMDB watch page redirected somewhere that is not allowed");
                        return null;
                    }

                    uri = next;
                    continue;
                }

                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxPageBytes)
                {
                    _logger.LogInformation("TMDB watch page request returned {Status}", (int)response.StatusCode);
                    return null;
                }

                return await ReadCappedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("TMDB watch page redirected too many times");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogInformation(ex, "Could not fetch the TMDB watch page");
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Fetching the TMDB watch page timed out");
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

    private static bool IsTmdbPage(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps
            && (uri.Host.Equals("www.themoviedb.org", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("themoviedb.org", StringComparison.OrdinalIgnoreCase));

    // Content-Length is absent on chunked responses, so the size limit has to be enforced while reading.
    private async Task<string?> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxPageBytes)
            {
                _logger.LogInformation("TMDB watch page is larger than {Limit} bytes and was ignored", MaxPageBytes);
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
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

        public string? Link { get; set; }
    }
}

/// <summary>
/// What TMDB knows about where a title can be watched.
/// </summary>
/// <param name="Flatrate">Subscription providers per country.</param>
/// <param name="Links">TMDB watch page (powered by JustWatch) per country, where TMDB provides one.</param>
public sealed record TitleAvailability(
    IReadOnlyDictionary<string, IReadOnlyList<ProviderRef>> Flatrate,
    IReadOnlyDictionary<string, string> Links);
