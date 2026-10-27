using System.Globalization;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WhereToWatch.Logos;
using Jellyfin.Plugin.WhereToWatch.Rendering;
using Jellyfin.Plugin.WhereToWatch.State;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.WhereToWatch.Middleware;

/// <summary>
/// Draws provider badges onto Primary images as they are served, leaving stored images untouched.
/// </summary>
public sealed partial class BadgeMiddleware
{
    private const long MaxImageBytes = 25 * 1024 * 1024;
    private const long MaxPageBytes = 5 * 1024 * 1024;
    // Poster cards are a fixed 2:3 box. The web client scales the poster to cover that box and centres it, so a poster of any
    // other shape is cropped at the sides (or top and bottom). The fillWidth/fillHeight in the request is the poster's own shape,
    // not the card's, so it cannot be used for this.
    private const double CardAspect = 2.0 / 3;
    private static readonly SemaphoreSlim RenderSlots = new(Math.Max(1, Environment.ProcessorCount));

    // Logged once per provider so a missing logo does not flood the log with one line per poster request.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> WarnedProviders = new();

    private readonly RequestDelegate _next;
    private readonly ILibraryManager _libraryManager;
    private readonly StateStore _state;
    private readonly LogoStore _logos;
    private readonly RenderCache _cache;
    private readonly ILogger<BadgeMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BadgeMiddleware"/> class.
    /// </summary>
    /// <param name="next">Next middleware.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="state">State store.</param>
    /// <param name="logos">Logo store.</param>
    /// <param name="cache">Render cache.</param>
    /// <param name="logger">Logger.</param>
    public BadgeMiddleware(RequestDelegate next, ILibraryManager libraryManager, StateStore state, LogoStore logos, RenderCache cache, ILogger<BadgeMiddleware> logger)
    {
        _next = next;
        _libraryManager = libraryManager;
        _state = state;
        _logos = logos;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (IsWebIndex(context.Request))
        {
            await AddScriptToWebIndexAsync(context).ConfigureAwait(false);
            return;
        }

        var plan = TryBuildPlan(context);
        if (plan is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var etag = "\"wtw-" + plan.Key[..32] + "\"";
        var cached = _cache.Get(plan.Key);
        if (cached is not null)
        {
            if (context.Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                SetHeaders(context, etag);
                return;
            }

            await WriteAsync(context, cached, etag).ConfigureAwait(false);
            return;
        }

        // Ask Jellyfin for the plain image: no conditional, ranged or compressed variants.
        context.Request.Headers.Remove(HeaderNames.IfNoneMatch);
        context.Request.Headers.Remove(HeaderNames.IfModifiedSince);
        context.Request.Headers.Remove(HeaderNames.IfRange);
        context.Request.Headers.Remove(HeaderNames.Range);
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }

        var contentType = context.Response.ContentType ?? string.Empty;
        var renderable = context.Response.StatusCode == StatusCodes.Status200OK
            && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !context.Response.Headers.ContainsKey(HeaderNames.ContentEncoding)
            && buffer.Length is > 0 and <= MaxImageBytes;

        RenderedImage? rendered = null;
        if (renderable)
        {
            rendered = await RenderAsync(buffer.ToArray(), plan, context.RequestAborted).ConfigureAwait(false);
        }

        if (rendered is null)
        {
            buffer.Position = 0;
            if (buffer.Length > 0)
            {
                context.Response.ContentLength = buffer.Length;
                await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
            }

            return;
        }

        _cache.Set(plan.Key, rendered);
        await WriteAsync(context, rendered, etag).ConfigureAwait(false);
    }

    private static bool IsWebIndex(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        return HttpMethods.IsGet(request.Method)
            && (path.Equals("/web/", StringComparison.OrdinalIgnoreCase) || path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase));
    }

    // Relative, so it keeps working when Jellyfin is served under a base URL; the version busts the browser cache on upgrades.
    private static string ScriptUrl() => "../WhereToWatch/watchon.js?v=" + typeof(BadgeMiddleware).Assembly.GetName().Version;

    private async Task AddScriptToWebIndexAsync(HttpContext context)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.WatchOnEnabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask for the whole, uncompressed page so it can be edited as text.
        context.Request.Headers.Remove(HeaderNames.IfNoneMatch);
        context.Request.Headers.Remove(HeaderNames.IfModifiedSince);
        context.Request.Headers.Remove(HeaderNames.IfRange);
        context.Request.Headers.Remove(HeaderNames.Range);
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }

        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType ?? string.Empty).StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
            && !context.Response.Headers.ContainsKey(HeaderNames.ContentEncoding)
            && buffer.Length is > 0 and <= MaxPageBytes;

        if (!isHtml)
        {
            buffer.Position = 0;
            if (buffer.Length > 0)
            {
                context.Response.ContentLength = buffer.Length;
                await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
            }

            return;
        }

        var page = System.Text.Encoding.UTF8.GetBytes(HtmlInjector.Inject(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), ScriptUrl()));
        context.Response.ContentLength = page.Length;

        // The page changed, so the original validators no longer describe it, and it must not outlive a plugin upgrade in a cache.
        context.Response.Headers.Remove(HeaderNames.ETag);
        context.Response.Headers.Remove(HeaderNames.LastModified);
        context.Response.Headers.CacheControl = "no-cache";
        await original.WriteAsync(page, context.RequestAborted).ConfigureAwait(false);
    }

    [GeneratedRegex(@"/Items/(?<id>[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12})/Images/Primary(?:/0)?(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PrimaryImagePath();

    private static void SetHeaders(HttpContext context, string etag)
    {
        context.Response.Headers.ETag = etag;
        // The format follows what Jellyfin served for this Accept header, so caches must keep the variants apart.
        context.Response.Headers.Vary = HeaderNames.Accept;
        // The URL's image tag does not change when badges do, so clients must revalidate (cheap 304s).
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Remove(HeaderNames.LastModified);
    }

    private static async Task WriteAsync(HttpContext context, RenderedImage image, string etag)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = image.ContentType;
        context.Response.ContentLength = image.Bytes.Length;
        SetHeaders(context, etag);

        // Lets you check in the browser's network tab which build rendered the poster and which card shape it assumed.
        context.Response.Headers["X-WhereToWatch"] = FormattableString.Invariant($"{typeof(BadgeMiddleware).Assembly.GetName().Version}; card-aspect={CardAspect:F3}");
        await context.Response.Body.WriteAsync(image.Bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task<RenderedImage?> RenderAsync(byte[] poster, RenderPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            await RenderSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var logos = new List<byte[]>();
                foreach (var providerId in plan.ProviderIds)
                {
                    var logo = _logos.Load(providerId);
                    if (logo is not null)
                    {
                        logos.Add(logo);
                    }
                    else if (WarnedProviders.TryAdd(providerId, true))
                    {
                        _logger.LogWarning(
                            "Where to Watch: no logo for provider {ProviderId}, so its badge is skipped. Put {ProviderId}.png in the plugin's logos folder, or run a scan to download the TMDB icon to {Path}",
                            providerId,
                            providerId,
                            LogoStore.ExpectedPath(providerId));
                    }
                }

                return logos.Count == 0 ? null : BadgeRenderer.Render(poster, logos, plan.Settings, CardAspect);
            }
            finally
            {
                RenderSlots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Where to Watch: could not draw badges, serving the original poster");
            return null;
        }
    }

    private RenderPlan? TryBuildPlan(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return null;
        }

        var config = Plugin.Instance?.Configuration;
        var match = PrimaryImagePath().Match(context.Request.Path.Value ?? string.Empty);
        if (config is null || !config.BadgesEnabled || !match.Success || !Guid.TryParse(match.Groups["id"].Value, out var itemId))
        {
            return null;
        }

        var providerIds = config.BadgeProviderIds(_state.Get(itemId));
        if (providerIds.Count == 0)
        {
            return null;
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is not (Movie or Series))
        {
            return null;
        }

        var key = RenderCache.Key(
            itemId.ToString("N"),
            context.Request.QueryString.Value ?? string.Empty,
            context.Request.Headers.Accept.ToString(),
            CardAspect.ToString("F4", CultureInfo.InvariantCulture),
            item.DateModified.Ticks.ToString(CultureInfo.InvariantCulture),
            config.RenderFingerprint(),
            string.Join(',', providerIds.Select(id => id.ToString(CultureInfo.InvariantCulture) + ":" + _logos.Stamp(id))));
        return new RenderPlan(key, providerIds, config.ToRenderSettings());
    }

    private sealed record RenderPlan(string Key, IReadOnlyList<int> ProviderIds, RenderSettings Settings);
}
