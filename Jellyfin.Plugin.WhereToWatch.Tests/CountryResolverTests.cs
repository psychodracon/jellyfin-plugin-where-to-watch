using Jellyfin.Plugin.WhereToWatch.Resolution;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class CountryResolverTests
{
    private static readonly ProviderRef Netflix = new(8, "Netflix");
    private static readonly ProviderRef Disney = new(337, "Disney Plus");
    private static readonly ProviderRef Hbo = new(1899, "HBO Max");
    private static readonly ProviderRef Apple = new(350, "Apple TV Plus");

    [Fact]
    public void Primary_country_wins_and_returns_only_selected_providers_in_priority_order()
    {
        var available = new Dictionary<string, IReadOnlyList<ProviderRef>>
        {
            ["PL"] = [Hbo, Apple, Netflix, Disney],
        };

        var result = CountryResolver.Resolve(["PL", "US"], [8, 1899], available);

        Assert.Equal("PL", result.Country);
        Assert.Equal([Netflix, Hbo], result.Providers);
    }

    [Fact]
    public void Falls_back_to_next_country_when_primary_has_no_selected_provider()
    {
        var available = new Dictionary<string, IReadOnlyList<ProviderRef>>
        {
            ["PL"] = [Apple],
            ["US"] = [Disney, Netflix],
        };

        var result = CountryResolver.Resolve(["PL", "US"], [8, 337], available);

        Assert.Equal("US", result.Country);
        Assert.Equal([Netflix, Disney], result.Providers);
    }

    [Fact]
    public void Skips_countries_the_item_has_no_data_for()
    {
        var available = new Dictionary<string, IReadOnlyList<ProviderRef>>
        {
            ["US"] = [Netflix],
        };

        var result = CountryResolver.Resolve(["PL", "US"], [8], available);

        Assert.Equal("US", result.Country);
    }

    [Fact]
    public void Matches_country_codes_case_insensitively()
    {
        var available = new Dictionary<string, IReadOnlyList<ProviderRef>>
        {
            ["PL"] = [Netflix],
        };

        var result = CountryResolver.Resolve(["pl"], [8], available);

        Assert.Equal("PL", result.Country);
    }

    [Fact]
    public void Returns_no_country_when_no_selected_provider_is_available_anywhere()
    {
        var available = new Dictionary<string, IReadOnlyList<ProviderRef>>
        {
            ["PL"] = [Apple],
            ["US"] = [Apple],
        };

        var result = CountryResolver.Resolve(["PL", "US"], [8], available);

        Assert.Null(result.Country);
        Assert.Empty(result.Providers);
    }
}
