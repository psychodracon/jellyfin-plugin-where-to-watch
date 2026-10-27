using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.WhereToWatch.Tmdb;

/// <summary>
/// Reads per-provider links out of the HTML of a TMDB watch page. TMDB's API has no such links, so this is optional and
/// best-effort: it relies on the page's markup and returns nothing when that changes.
/// </summary>
public static partial class WatchPageParser
{
    private const string LinkPrefix = "https://click.justwatch.com/a?";

    /// <summary>
    /// Finds the subscription ("Stream") link of each provider on the page.
    /// </summary>
    /// <param name="html">HTML of a TMDB watch page.</param>
    /// <returns>TMDB provider id mapped to its link; the first link wins when a provider is listed twice (HD and SD).</returns>
    public static IReadOnlyDictionary<int, string> StreamLinks(string html)
    {
        var links = new Dictionary<int, string>();
        foreach (Match match in AnchorHref().Matches(html))
        {
            var url = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (url.StartsWith(LinkPrefix, StringComparison.Ordinal) && TryReadClickout(url, out var providerId, out var monetization) && monetization == "flatrate")
            {
                links.TryAdd(providerId, url);
            }
        }

        return links;
    }

    // The cx parameter is base64url JSON; its first entry describes the provider and how it is offered.
    private static bool TryReadClickout(string url, out int providerId, out string monetization)
    {
        providerId = 0;
        monetization = string.Empty;
        var cx = QueryValue(url, "cx");
        if (cx is null)
        {
            return false;
        }

        try
        {
            var base64 = cx.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            foreach (var entry in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (entry.TryGetProperty("data", out var data) && data.TryGetProperty("providerId", out var id) && id.TryGetInt32(out providerId))
                {
                    monetization = data.TryGetProperty("monetizationType", out var type) ? type.GetString() ?? string.Empty : string.Empty;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Not a link we understand; skip it.
        }

        return false;
    }

    private static string? QueryValue(string url, string name)
    {
        var query = url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..];
        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && pair[..separator] == name)
            {
                return pair[(separator + 1)..];
            }
        }

        return null;
    }

    [GeneratedRegex("<a\\b[^>]*?\\bhref=\"(?<href>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorHref();
}
