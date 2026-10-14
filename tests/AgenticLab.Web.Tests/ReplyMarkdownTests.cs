using System.Net;
using AgenticLab.Web.Services;
using Xunit;

namespace AgenticLab.Web.Tests;

public sealed class ReplyMarkdownTests
{
    [Fact]
    public void FormatsSupportedMarkdown()
    {
        var html = ReplyMarkdownRenderer.Render("""
            # Heading

            **Bold** and *italic* with `inline`.

            - First
            - Second

            1. Ordered
            2. List

            ```html
            <script>alert('code')</script>
            ```

            | Name | Value |
            | --- | --- |
            | Sample | 42 |
            """);

        foreach (var tag in new[] { "<h1>", "<strong>", "<em>", "<code>", "<ul>", "<ol>", "<li>", "<pre>", "<table>", "<th>", "<td>" })
            Assert.Contains(tag, html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script", html);
    }

    [Theory]
    [InlineData("https://example.com/path?one=1&two=2")]
    [InlineData("http://example.com")]
    [InlineData("HTTPS://example.com")]
    [InlineData("mailto:person@example.com")]
    public void AllowsOnlyApprovedLinkDestinations(string destination)
    {
        foreach (var source in new[] { $"[Label]({destination})", $"[Label][ref]\n\n[ref]: {destination}", $"<{destination}>" })
        {
            var html = ReplyMarkdownRenderer.Render(source);
            Assert.Contains("<a href=\"", html);
            Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", html);
            Assert.Contains(new Uri(destination).AbsoluteUri, WebUtility.HtmlDecode(html));
        }
    }

    [Theory]
    [InlineData("javascript:alert%281%29")]
    [InlineData("JaVaScRiPt:alert%281%29")]
    [InlineData("data:text/html,test")]
    [InlineData("vbscript:test")]
    [InlineData("file:///secret.txt")]
    [InlineData("ftp://example.com")]
    [InlineData("custom:command")]
    [InlineData("//example.com")]
    [InlineData("/relative")]
    [InlineData("#fragment")]
    [InlineData("javascript&#58;alert%281%29")]
    [InlineData("java&#x09;script:alert%281%29")]
    [InlineData("java&#x0a;script:alert%281%29")]
    [InlineData("javascript%3Aalert%281%29")]
    public void RejectsUnsafeLinksButPreservesLabels(string destination)
    {
        foreach (var source in new[] { $"[Label]({destination})", $"[Label][ref]\n\n[ref]: {destination}", $"<{destination}>" })
        {
            var html = ReplyMarkdownRenderer.Render(source);
            Assert.DoesNotContain("<a ", html);
            if (source.StartsWith('[')) Assert.Contains("Label", html);
        }
    }

    [Theory]
    [InlineData("<script>alert('test')</script>")]
    [InlineData("<img src=\"https://images.invalid/pixel\" onerror=\"alert(1)\">")]
    [InlineData("<iframe src=\"https://example.com\"></iframe>")]
    [InlineData("<style>body { display:none }</style>")]
    [InlineData("<a href=\"javascript:alert(1)\">Click</a>")]
    public void RawHtmlIsVisibleText(string source)
    {
        var html = ReplyMarkdownRenderer.Render(source);
        Assert.Contains(source, WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(source, html);
        Assert.Contains("&lt;", html);
    }

    [Theory]
    [InlineData("![Alt text](https://images.invalid/pixel)")]
    [InlineData("![Alt text](/local.png)")]
    [InlineData("![Alt text](data:image/png;base64,AAAA)")]
    [InlineData("![Alt text][image]\n\n[image]: https://images.invalid/pixel")]
    [InlineData("![**Alt** text](https://images.invalid/pixel)")]
    [InlineData("![Alt [text](https://example.com)](https://images.invalid/pixel)")]
    public void ImagesBecomeAltTextWithoutResourceRequests(string source)
    {
        var html = ReplyMarkdownRenderer.Render(source);
        Assert.Contains("Alt text", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("src=", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("<strong>", html);
    }

    [Fact]
    public void EncodesLinkTitleAndQueryAttributes()
    {
        var html = ReplyMarkdownRenderer.Render("[Label](https://example.com/?value=%22&other=2 \"&quot; onmouseover=&quot;alert(1)\")");
        Assert.Contains("&amp;other=2", html);
        Assert.DoesNotContain("\" onmouseover=\"", html);
        Assert.Contains("&quot; onmouseover=&quot;", html);
    }

    [Fact]
    public void EveryPartialSnapshotRemainsSafe()
    {
        const string source = "## Heading\n\n```html\n<img src='https://images.invalid/pixel'>\n```\n\n[Link](javascript:alert%281%29)\n\n| Name | Value |\n| --- | --- |\n| First | Second |";
        for (var length = 0; length <= source.Length; length++)
        {
            var html = ReplyMarkdownRenderer.Render(source[..length]);
            Assert.DoesNotContain("<img", html);
            Assert.DoesNotContain("<a ", html);
            Assert.DoesNotContain("<script", html);
        }
        Assert.Equal(string.Empty, ReplyMarkdownRenderer.Render(null));
    }
}