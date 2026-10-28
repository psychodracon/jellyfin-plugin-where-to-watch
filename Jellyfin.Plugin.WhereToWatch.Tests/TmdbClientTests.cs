using System.Net;
using System.Text;
using Jellyfin.Plugin.WhereToWatch.Tmdb;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public sealed class TmdbClientTests
{
    private const string Page = "https://www.themoviedb.org/movie/603/watch?locale=NL";

    [Fact]
    public async Task Redirect_within_tmdb_is_followed()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/movie/603/watch"
            ? Redirect("/movie/603-the-matrix/watch?locale=NL")
            : Ok("<html>ok</html>"));
        using var client = new TmdbClient(NullLogger<TmdbClient>.Instance, handler);

        var html = await client.GetWatchPageAsync(Page, CancellationToken.None);

        Assert.Equal("<html>ok</html>", html);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Redirect_to_another_host_is_refused()
    {
        var handler = new FakeHandler(_ => Redirect("https://evil.example/steal"));
        using var client = new TmdbClient(NullLogger<TmdbClient>.Instance, handler);

        Assert.Null(await client.GetWatchPageAsync(Page, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Redirect_to_plain_http_is_refused()
    {
        var handler = new FakeHandler(_ => Redirect("http://www.themoviedb.org/movie/603/watch"));
        using var client = new TmdbClient(NullLogger<TmdbClient>.Instance, handler);

        Assert.Null(await client.GetWatchPageAsync(Page, CancellationToken.None));
    }

    [Fact]
    public async Task Redirect_loop_gives_up()
    {
        var handler = new FakeHandler(_ => Redirect("/again"));
        using var client = new TmdbClient(NullLogger<TmdbClient>.Instance, handler);

        Assert.Null(await client.GetWatchPageAsync(Page, CancellationToken.None));
        Assert.True(handler.Requests.Count <= 6);
    }

    [Fact]
    public async Task Url_outside_tmdb_is_never_requested()
    {
        var handler = new FakeHandler(_ => Ok("x"));
        using var client = new TmdbClient(NullLogger<TmdbClient>.Instance, handler);

        Assert.Null(await client.GetWatchPageAsync("https://evil.example/page", CancellationToken.None));
        Assert.Null(await client.GetWatchPageAsync("http://www.themoviedb.org/page", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Oversized_body_without_content_length_is_ignored()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(new byte[4 * 1024 * 1024])),
        });
        using var client = new TmdbClient(NullLogger<TmdbClient>.Instance, handler);

        Assert.Null(await client.GetWatchPageAsync(Page, CancellationToken.None));
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Moved);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
