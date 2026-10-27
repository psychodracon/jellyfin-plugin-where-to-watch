using Jellyfin.Plugin.WhereToWatch.Tags;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class TagDiffTests
{
    [Fact]
    public void Adds_stream_tag_for_provider_the_item_lacks()
    {
        var changes = TagDiff.Compute(["Drama"], ["Netflix"]);

        Assert.Equal(["stream:Netflix"], changes.ToAdd);
        Assert.Empty(changes.ToRemove);
    }

    [Fact]
    public void Removes_stream_tag_for_provider_the_item_left()
    {
        var changes = TagDiff.Compute(["Drama", "stream:Netflix", "stream:HBO Max"], ["HBO Max"]);

        Assert.Empty(changes.ToAdd);
        Assert.Equal(["stream:Netflix"], changes.ToRemove);
    }

    [Fact]
    public void Never_touches_tags_without_the_stream_prefix()
    {
        var changes = TagDiff.Compute(["provider:Netflix", "Streaming", "Drama"], []);

        Assert.Empty(changes.ToAdd);
        Assert.Empty(changes.ToRemove);
    }

    [Fact]
    public void Matches_existing_tags_case_insensitively()
    {
        var changes = TagDiff.Compute(["STREAM:netflix"], ["Netflix"]);

        Assert.Empty(changes.ToAdd);
        Assert.Empty(changes.ToRemove);
    }

    [Fact]
    public void Apply_swaps_stale_stream_tags_for_new_ones_and_keeps_everything_else()
    {
        var changes = new TagChanges(["stream:HBO Max"], ["stream:Netflix"]);

        var result = TagDiff.Apply(["Drama", "stream:Netflix", "provider:Netflix"], changes);

        Assert.Equal(["Drama", "provider:Netflix", "stream:HBO Max"], result);
    }

    [Theory]
    [InlineData("stream:Netflix", true)]
    [InlineData("STREAM:netflix", true)]
    [InlineData("provider:Netflix", false)]
    [InlineData("streaming", false)]
    public void Recognises_tags_it_manages_by_prefix(string tag, bool expected)
    {
        Assert.Equal(expected, TagDiff.IsManaged(tag));
    }
}
