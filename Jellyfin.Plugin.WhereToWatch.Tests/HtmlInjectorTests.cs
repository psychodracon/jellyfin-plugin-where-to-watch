using Jellyfin.Plugin.WhereToWatch.Middleware;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class HtmlInjectorTests
{
    private const string Script = "../WhereToWatch/watchon.js";

    [Fact]
    public void Adds_the_script_tag_just_before_the_closing_body()
    {
        var result = HtmlInjector.Inject("<html><body><div id=\"app\"></div></body></html>", Script);

        Assert.Equal("<html><body><div id=\"app\"></div><script src=\"../WhereToWatch/watchon.js\" defer></script></body></html>", result);
    }

    [Fact]
    public void Is_idempotent_when_the_script_is_already_there()
    {
        var once = HtmlInjector.Inject("<html><body></body></html>", Script);

        Assert.Equal(once, HtmlInjector.Inject(once, Script));
    }

    [Fact]
    public void Leaves_a_document_without_a_body_alone()
    {
        const string Fragment = "<div>not a page</div>";

        Assert.Equal(Fragment, HtmlInjector.Inject(Fragment, Script));
    }

    [Fact]
    public void Finds_the_closing_body_tag_whatever_its_case()
    {
        var result = HtmlInjector.Inject("<HTML><BODY>x</BODY></HTML>", Script);

        Assert.Contains("<script src=\"../WhereToWatch/watchon.js\" defer></script></BODY>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_only_the_last_closing_body_when_one_appears_inside_a_script()
    {
        // A literal "</body>" inside inline JavaScript must not be mistaken for the real end of the page.
        var result = HtmlInjector.Inject("<body><script>var s = '</body>';</script></body>", Script);

        Assert.EndsWith("<script src=\"../WhereToWatch/watchon.js\" defer></script></body>", result, StringComparison.Ordinal);
        Assert.Equal(1, result.Split("watchon.js").Length - 1);
    }
}
