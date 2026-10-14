using System.Globalization;
using System.Text.Encodings.Web;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace AgenticLab.Web.Services;

/// <summary>Renders untrusted replies without raw HTML, image requests or unrestricted navigation.</summary>
internal static class ReplyMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .DisableHtml()
        .Build();

    internal static string Render(string? source)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<LinkInlineRenderer>(new SafeLinkRenderer());
        renderer.ObjectRenderers.Replace<AutolinkInlineRenderer>(new SafeAutolinkRenderer());
        renderer.Render(Markdown.Parse(source, Pipeline));
        return writer.ToString();
    }

    private static string? AllowedUrl(string? destination)
    {
        if (string.IsNullOrEmpty(destination) || destination.Any(char.IsControl)
            || destination != destination.Trim()
            || !Uri.TryCreate(destination, UriKind.Absolute, out var uri)) return null;

        return uri.Scheme is "http" or "https" or "mailto" ? uri.AbsoluteUri : null;
    }

    private static void OpenLink(HtmlRenderer renderer, string url, string? title = null)
    {
        renderer.Write("<a href=\"").Write(HtmlEncoder.Default.Encode(url))
            .Write("\" target=\"_blank\" rel=\"noopener noreferrer\"");
        if (!string.IsNullOrEmpty(title))
            renderer.Write(" title=\"").Write(HtmlEncoder.Default.Encode(title)).Write('"');
        renderer.Write('>');
    }

    private sealed class SafeLinkRenderer : HtmlObjectRenderer<LinkInline>
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            if (link.IsImage)
            {
                var enableHtml = renderer.EnableHtmlForInline;
                renderer.EnableHtmlForInline = false;
                renderer.WriteChildren(link);
                renderer.EnableHtmlForInline = enableHtml;
                return;
            }

            var url = AllowedUrl(link.GetDynamicUrl?.Invoke() ?? link.Url);
            var isLink = renderer.EnableHtmlForInline && url is not null;
            if (isLink) OpenLink(renderer, url!, link.Title);
            renderer.WriteChildren(link);
            if (isLink) renderer.Write("</a>");
        }
    }

    private sealed class SafeAutolinkRenderer : HtmlObjectRenderer<AutolinkInline>
    {
        protected override void Write(HtmlRenderer renderer, AutolinkInline link)
        {
            var url = AllowedUrl(link.IsEmail ? $"mailto:{link.Url}" : link.Url);
            var isLink = renderer.EnableHtmlForInline && url is not null;
            if (isLink) OpenLink(renderer, url!);
            renderer.Write(HtmlEncoder.Default.Encode(link.Url));
            if (isLink) renderer.Write("</a>");
        }
    }
}