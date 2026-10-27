using Jellyfin.Plugin.WhereToWatch.Tmdb;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class StreamLinkMatcherTests
{
    private static readonly StreamLink Netflix = new(8, "Netflix", "https://click.test/netflix");
    private static readonly StreamLink Disney = new(2706, "Disney Plus", "https://click.test/disney");
    private static readonly StreamLink Canal = new(2101, "CANAL+", "https://click.test/canal");

    [Fact]
    public void Matches_by_provider_id_when_the_ids_agree()
    {
        Assert.Equal("https://click.test/netflix", StreamLinkMatcher.Find([Netflix, Disney], 8, "Netflix"));
    }

    [Fact]
    public void Falls_back_to_the_name_when_the_page_uses_a_different_id_for_the_same_service()
    {
        // TMDB's API calls Disney+ in Poland 337; the page's own id for it is 2706.
        Assert.Equal("https://click.test/disney", StreamLinkMatcher.Find([Netflix, Disney], 337, "Disney Plus"));
    }

    [Fact]
    public void The_id_wins_over_a_name_that_differs()
    {
        Assert.Equal("https://click.test/netflix", StreamLinkMatcher.Find([Netflix, Disney], 8, "Disney Plus"));
    }

    [Theory]
    [InlineData("Disney+")]
    [InlineData("disney plus")]
    [InlineData("DISNEY PLUS")]
    [InlineData("  Disney  Plus ")]
    public void Names_are_compared_ignoring_case_spacing_and_the_plus_sign(string tmdbName)
    {
        Assert.Equal("https://click.test/disney", StreamLinkMatcher.Find([Disney], 337, tmdbName));
    }

    [Fact]
    public void A_plus_sign_and_the_word_plus_are_the_same()
    {
        Assert.Equal("https://click.test/canal", StreamLinkMatcher.Find([Canal], 381, "Canal Plus"));
    }

    [Fact]
    public void Returns_nothing_when_neither_id_nor_name_matches()
    {
        Assert.Null(StreamLinkMatcher.Find([Netflix, Disney], 1899, "HBO Max"));
        Assert.Null(StreamLinkMatcher.Find([], 8, "Netflix"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_never_matches_by_name(string name)
    {
        Assert.Null(StreamLinkMatcher.Find([new StreamLink(5, string.Empty, "https://click.test/x")], 99, name));
    }
}
