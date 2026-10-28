using System.Threading;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>One-call conveniences over <see cref="MarkdownParser"/> and <see cref="MarkdownHtmlRenderer"/>.</summary>
    public static class MarkdownConverter
    {
        /// <summary>Parses Markdown and renders it to HTML.</summary>
        public static string ToHtml(string markdown, MarkdownOptions? parseOptions = null, MarkdownHtmlOptions? htmlOptions = null, CancellationToken cancellationToken = default)
        {
            var doc = MarkdownParser.Parse(markdown, parseOptions, cancellationToken);
            return MarkdownHtmlRenderer.Render(doc, htmlOptions, cancellationToken);
        }
    }
}
