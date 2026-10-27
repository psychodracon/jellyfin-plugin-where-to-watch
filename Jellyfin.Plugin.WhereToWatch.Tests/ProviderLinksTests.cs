using Jellyfin.Plugin.WhereToWatch.Links;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class ProviderLinksTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(1796)]
    public void Netflix_gets_its_own_search_page(int providerId)
    {
        Assert.Equal("https://www.netflix.com/search?q=Lupin", ProviderLinks.SearchUrl(providerId, "Lupin"));
    }

    [Theory]
    [InlineData(119)]
    [InlineData(2100)]
    public void Prime_Video_gets_its_own_search_page(int providerId)
    {
        Assert.Equal("https://www.primevideo.com/search/ref=atv_nb_sr?phrase=The%20Boys", ProviderLinks.SearchUrl(providerId, "The Boys"));
    }

    [Fact]
    public void Titles_are_url_encoded_including_polish_letters_and_reserved_characters()
    {
        Assert.Equal(
            "https://www.netflix.com/search?q=Diabe%C5%82%20ubiera%20si%C4%99%20u%20Prady%202",
            ProviderLinks.SearchUrl(8, "Diabeł ubiera się u Prady 2"));
        Assert.Equal("https://www.netflix.com/search?q=Tom%20%26%20Jerry%3F", ProviderLinks.SearchUrl(8, "Tom & Jerry?"));
    }

    [Theory]
    [InlineData(337)]
    [InlineData(1899)]
    [InlineData(999999)]
    public void A_provider_without_a_known_search_page_has_none(int providerId)
    {
        Assert.Null(ProviderLinks.SearchUrl(providerId, "Lupin"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_title_has_no_search_page(string title)
    {
        Assert.Null(ProviderLinks.SearchUrl(8, title));
    }
}
