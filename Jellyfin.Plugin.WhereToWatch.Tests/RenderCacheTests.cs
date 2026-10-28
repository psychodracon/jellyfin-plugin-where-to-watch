using Jellyfin.Plugin.WhereToWatch.Rendering;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public sealed class RenderCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wtw-cache-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Query_keeps_only_image_parameters_in_a_stable_order()
    {
        var a = RenderCache.NormalizeQuery("?fillWidth=300&api_key=secret&Tag=abc&x=1");
        var b = RenderCache.NormalizeQuery("?x=2&tag=abc&FILLWIDTH=300&ApiKey=other");

        Assert.Equal("fillwidth=300&tag=abc", a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Query_without_image_parameters_is_empty()
    {
        Assert.Equal(string.Empty, RenderCache.NormalizeQuery("?a=1&b=2"));
        Assert.Equal(string.Empty, RenderCache.NormalizeQuery(string.Empty));
    }

    [Fact]
    public void Accept_keeps_only_known_image_types()
    {
        Assert.Equal("image/avif,image/webp", RenderCache.NormalizeAccept("image/webp,image/avif,*/*;q=0.8, x/" + Guid.NewGuid()));
        Assert.Equal(string.Empty, RenderCache.NormalizeAccept("text/html"));
    }

    [Fact]
    public void Stored_render_is_returned_with_its_content_type()
    {
        var cache = new RenderCache(Path.Combine(_dir, "c"));
        var key = RenderCache.Key("a");

        Assert.True(cache.Set(key, new RenderedImage([1, 2, 3], "image/webp")));

        var hit = cache.Get(key);
        Assert.NotNull(hit);
        Assert.Equal([1, 2, 3], hit.Bytes);
        Assert.Equal("image/webp", hit.ContentType);
    }

    [Fact]
    public void Missing_render_is_a_miss()
    {
        Assert.Null(new RenderCache(Path.Combine(_dir, "c")).Get(RenderCache.Key("nothing")));
    }

    [Fact]
    public void Unknown_content_type_is_not_stored_and_does_not_throw()
    {
        var cache = new RenderCache(Path.Combine(_dir, "c"));

        Assert.False(cache.Set(RenderCache.Key("a"), new RenderedImage([1], "image/bmp")));
    }

    [Fact]
    public void Write_failure_is_not_thrown()
    {
        // A file where the cache folder should be makes CreateDirectory fail.
        var blocked = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocked, "x");
        var cache = new RenderCache(blocked);

        Assert.False(cache.Set(RenderCache.Key("a"), new RenderedImage([1], "image/png")));
        Assert.Null(cache.Get(RenderCache.Key("a")));
    }

    [Fact]
    public void Prune_removes_the_oldest_renders_first()
    {
        var root = Path.Combine(_dir, "c");
        var cache = new RenderCache(root);
        var old = RenderCache.Key("old");
        var recent = RenderCache.Key("recent");
        cache.Set(old, new RenderedImage(new byte[100], "image/jpeg"));
        cache.Set(recent, new RenderedImage(new byte[100], "image/jpeg"));
        File.SetLastWriteTimeUtc(Path.Combine(root, old + ".jpg"), DateTime.UtcNow.AddDays(-1));

        cache.Prune(150);

        Assert.Null(cache.Get(old));
        Assert.NotNull(cache.Get(recent));
    }

    [Fact]
    public void Clear_and_prune_tolerate_a_missing_folder()
    {
        var cache = new RenderCache(Path.Combine(_dir, "never-created"));

        cache.Prune(0);
        cache.Clear();
    }
}
