using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.WhereToWatch.Tmdb;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class WatchPageParserTests
{
    // Real stream links from TMDB's watch page, as pasted by the user.
    private const string RealNetflixLink = "https://click.justwatch.com/a?cx=eyJzY2hlbWEiOiJpZ2x1OmNvbS5zbm93cGxvd2FuYWx5dGljcy5zbm93cGxvdy9jb250ZXh0cy9qc29uc2NoZW1hLzEtMC0wIiwiZGF0YSI6W3sic2NoZW1hIjoiaWdsdTpjb20uanVzdHdhdGNoL2NsaWNrb3V0X2NvbnRleHQvanNvbnNjaGVtYS8xLTMtMiIsImRhdGEiOnsicHJvdmlkZXIiOiJOZXRmbGl4IiwibW9uZXRpemF0aW9uVHlwZSI6ImZsYXRyYXRlIiwicHJlc2VudGF0aW9uVHlwZSI6ImhkIiwiY3VycmVuY3kiOiJQTE4iLCJwYXJ0bmVySWQiOjYsInByb3ZpZGVySWQiOjgsImNsaWNrb3V0VHlwZSI6Imp3LWNvbnRlbnQtcGFydG5lci1leHBvcnQtYXBpIn19LHsic2NoZW1hIjoiaWdsdTpjb20uanVzdHdhdGNoL3RpdGxlX2NvbnRleHQvanNvbnNjaGVtYS8xLTMtMCIsImRhdGEiOnsidGl0bGVJZCI6Mjk2MDQ2LCJvYmplY3RUeXBlIjoic2hvdyIsImp3RW50aXR5SWQiOiJ0czI5NjA0NiJ9fV19&amp;r=https%3A%2F%2Fwww.netflix.com%2Ftitle%2F80994082&amp;uct_country=pl";

    // Disney+ in Poland: the page says providerId 2706, while TMDB's API calls the same service 337.
    private const string RealDisneyLink = "https://click.justwatch.com/a?cx=eyJzY2hlbWEiOiJpZ2x1OmNvbS5zbm93cGxvd2FuYWx5dGljcy5zbm93cGxvdy9jb250ZXh0cy9qc29uc2NoZW1hLzEtMC0wIiwiZGF0YSI6W3sic2NoZW1hIjoiaWdsdTpjb20uanVzdHdhdGNoL2NsaWNrb3V0X2NvbnRleHQvanNvbnNjaGVtYS8xLTMtMiIsImRhdGEiOnsicHJvdmlkZXIiOiJEaXNuZXkgUGx1cyIsIm1vbmV0aXphdGlvblR5cGUiOiJmbGF0cmF0ZSIsInByZXNlbnRhdGlvblR5cGUiOiJoZCIsImN1cnJlbmN5IjoiUExOIiwicGFydG5lcklkIjo2LCJwcm92aWRlcklkIjoyNzA2LCJjbGlja291dFR5cGUiOiJqdy1jb250ZW50LXBhcnRuZXItZXhwb3J0LWFwaSJ9fSx7InNjaGVtYSI6ImlnbHU6Y29tLmp1c3R3YXRjaC90aXRsZV9jb250ZXh0L2pzb25zY2hlbWEvMS0zLTAiLCJkYXRhIjp7InRpdGxlSWQiOjEzOTMzMzYsIm9iamVjdFR5cGUiOiJtb3ZpZSIsImp3RW50aXR5SWQiOiJ0bTEzOTMzMzYifX1dfQ&amp;r=https%3A%2F%2Fdisneyplus.bn5x.net%2Fc%2F1206980%2F705874%2F9358%3Fu%3Dhttps%253A%252F%252Fwww.disneyplus.com%252Fpl-pl%252Fbrowse%252Fentity-c99203fc-eeff-4531-a35e-1f558bedce4f%26subId3%3Djustappsvod&amp;uct_country=pl";

    [Fact]
    public void Real_netflix_stream_link_is_read_with_its_id_name_and_entities_decoded()
    {
        var link = Assert.Single(WatchPageParser.StreamLinks(Anchor(RealNetflixLink)));

        Assert.Equal(8, link.ProviderId);
        Assert.Equal("Netflix", link.Name);
        Assert.StartsWith("https://click.justwatch.com/a?cx=", link.Url, StringComparison.Ordinal);
        Assert.EndsWith("&r=https%3A%2F%2Fwww.netflix.com%2Ftitle%2F80994082&uct_country=pl", link.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;", link.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Real_disney_link_carries_the_pages_own_provider_id_and_the_service_name()
    {
        var link = Assert.Single(WatchPageParser.StreamLinks(Anchor(RealDisneyLink)));

        Assert.Equal(2706, link.ProviderId);
        Assert.Equal("Disney Plus", link.Name);
        Assert.Contains("disneyplus.bn5x.net", Uri.UnescapeDataString(link.Url), StringComparison.Ordinal);
    }

    [Fact]
    public void Several_providers_are_returned_each_with_its_own_id_and_name()
    {
        var html = Anchor(Link("Amazon Prime Video", 119, "flatrate", "https://app.primevideo.com/detail?gti=x"))
            + Anchor(Link("CANAL+", 2101, "flatrate", "https://www.canalplus.com/pl/film/x"))
            + Anchor(Link("Polsat Box Go", 2363, "flatrate", "https://polsatboxgo.pl/wideo/film/x"));

        var links = WatchPageParser.StreamLinks(html);

        Assert.Equal([119, 2101, 2363], links.Select(l => l.ProviderId).Order());
        Assert.Equal("CANAL+", links.Single(l => l.ProviderId == 2101).Name);
        Assert.Contains("primevideo.com", Uri.UnescapeDataString(links.Single(l => l.ProviderId == 119).Url), StringComparison.Ordinal);
    }

    [Fact]
    public void The_hidden_sd_duplicate_of_a_provider_does_not_replace_the_hd_link()
    {
        var html = Anchor(Link("Netflix", 8, "flatrate", "https://www.netflix.com/title/1", "hd"))
            + Anchor(Link("Netflix", 8, "flatrate", "https://www.netflix.com/title/2", "sd"));

        var link = Assert.Single(WatchPageParser.StreamLinks(html));

        Assert.Contains("title%2F1", link.Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rent")]
    [InlineData("buy")]
    [InlineData("ads")]
    [InlineData("free")]
    public void Only_subscription_stream_links_are_returned(string monetization)
    {
        Assert.Empty(WatchPageParser.StreamLinks(Anchor(Link("Apple TV", 2, monetization, "https://tv.apple.com/x"))));
    }

    [Fact]
    public void Links_to_other_hosts_are_ignored_even_if_they_look_like_justwatch_links()
    {
        var evil = Link("Netflix", 8, "flatrate", "https://www.netflix.com/title/1").Replace("https://click.justwatch.com", "https://evil.example", StringComparison.Ordinal);

        Assert.Empty(WatchPageParser.StreamLinks(Anchor(evil)));
        Assert.Empty(WatchPageParser.StreamLinks(Anchor(Link("Netflix", 8, "flatrate", "https://x.test").Replace("https://click.justwatch.com", "http://click.justwatch.com", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("https://click.justwatch.com/a?cx=not-base64!!&amp;r=x")]
    [InlineData("https://click.justwatch.com/a?cx=bm90IGpzb24&amp;r=x")]
    [InlineData("https://click.justwatch.com/a?r=https%3A%2F%2Fx.test")]
    public void Malformed_links_are_skipped_without_failing(string href)
    {
        Assert.Empty(WatchPageParser.StreamLinks(Anchor(href)));
    }

    [Fact]
    public void A_page_without_any_links_gives_nothing()
    {
        Assert.Empty(WatchPageParser.StreamLinks("<html><body><h3>Stream</h3></body></html>"));
        Assert.Empty(WatchPageParser.StreamLinks(string.Empty));
    }

    private static string Anchor(string href) =>
        $"<li><div><a class=\"relative\" href=\"{href}\" title=\"Watch x\" target=\"_blank\" rel=\"noopener\"><img src=\"https://media.themoviedb.org/t/p/original/a.png\" width=\"50\"></a></div></li>";

    // Same structure as TMDB's real links: cx is base64url JSON whose first entry is the clickout context.
    private static string Link(string provider, int providerId, string monetization, string target, string presentation = "hd")
    {
        var json = JsonSerializer.Serialize(new
        {
            schema = "iglu:com.snowplowanalytics.snowplow/contexts/jsonschema/1-0-0",
            data = new object[]
            {
                new { schema = "iglu:com.justwatch/clickout_context/jsonschema/1-3-2", data = new { provider, monetizationType = monetization, presentationType = presentation, currency = "PLN", partnerId = 6, providerId } },
                new { schema = "iglu:com.justwatch/title_context/jsonschema/1-3-0", data = new { titleId = 1, objectType = "movie" } },
            },
        });
        var cx = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"https://click.justwatch.com/a?cx={cx}&amp;r={Uri.EscapeDataString(target)}&amp;uct_country=pl";
    }
}
