using System;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>How raw HTML in Markdown source is treated.</summary>
    public enum RawHtmlMode
    {
        /// <summary>Raw HTML is parsed into <see cref="HtmlBlock"/>/<see cref="HtmlInline"/> nodes and rendered as escaped text (shows the tags literally). Default.</summary>
        Escape,
        /// <summary>Raw HTML is parsed but omitted from rendered output.</summary>
        Strip,
        /// <summary>Raw HTML is rendered unchanged. Only use when the output is passed through the sanitizer.</summary>
        Passthrough,
    }

    /// <summary>Options for <see cref="MarkdownParser"/>.</summary>
    public sealed class MarkdownOptions
    {
        /// <summary>GFM pipe tables. Default true.</summary>
        public bool Tables { get; set; } = true;

        /// <summary>GFM task list items (<c>- [ ]</c>, <c>- [x]</c>). Default true.</summary>
        public bool TaskLists { get; set; } = true;

        /// <summary>GFM strikethrough (<c>~a~</c>, <c>~~a~~</c>). Default true.</summary>
        public bool Strikethrough { get; set; } = true;

        /// <summary>GFM extended autolinks (bare <c>www.</c>, <c>http(s)://</c>, <c>ftp://</c> and email addresses). Default true.</summary>
        public bool Autolinks { get; set; } = true;

        /// <summary>Resource limits. Default <c>new MarkupLimits()</c>.</summary>
        public MarkupLimits Limits { get; set; } = new MarkupLimits();

        /// <summary>Strict CommonMark 0.31.2: all GFM extensions off.</summary>
        public static MarkdownOptions CommonMark() => new MarkdownOptions
        {
            Tables = false,
            TaskLists = false,
            Strikethrough = false,
            Autolinks = false,
        };

        /// <summary>CommonMark plus the GFM extensions (tables, task lists, strikethrough, autolinks). Same as <c>new MarkdownOptions()</c>.</summary>
        public static MarkdownOptions Gfm() => new MarkdownOptions();

        /// <summary>Returns a copy (limits are cloned too).</summary>
        public MarkdownOptions Clone()
        {
            var c = (MarkdownOptions)MemberwiseClone();
            c.Limits = Limits.Clone();
            return c;
        }
    }
}
