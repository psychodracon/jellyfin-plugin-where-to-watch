using Jellyfin.Plugin.WhereToWatch.Middleware;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class HtmlInjectorTests
{
    private const string Script = "console.log('hi');";

    [Fact]
    public void Adds_the_script_inline_just_before_the_closing_body()
    {
        var result = HtmlInjector.Inject("<html><body><div id=\"app\"></div></body></html>", Script);

        Assert.Equal("<html><body><div id=\"app\"></div><script data-wheretowatch>console.log('hi');</script></body></html>", result);
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

        Assert.Contains("<script data-wheretowatch>console.log('hi');</script></BODY>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_only_the_last_closing_body_when_one_appears_inside_a_script()
    {
        // A literal "</body>" inside inline JavaScript must not be mistaken for the real end of the page.
        var result = HtmlInjector.Inject("<body><script>var s = '</body>';</script></body>", Script);

        Assert.EndsWith("<script data-wheretowatch>console.log('hi');</script></body>", result, StringComparison.Ordinal);
        Assert.Equal(1, result.Split("data-wheretowatch").Length - 1);
    }

    [Fact]
    public void A_closing_script_tag_inside_the_script_text_cannot_end_the_tag_early()
    {
        var result = HtmlInjector.Inject("<body></body>", "var s = '</script><b>x</b>';");

        Assert.Equal("<body><script data-wheretowatch>var s = '<\\/script><b>x</b>';</script></body>", result);
    }

    [Fact]
    public void The_escaped_closing_tag_is_matched_whatever_its_case()
    {
        var result = HtmlInjector.Inject("<body></body>", "var s = '</SCRIPT>';");

        Assert.DoesNotContain("</SCRIPT>", result, StringComparison.Ordinal);
    }
}
