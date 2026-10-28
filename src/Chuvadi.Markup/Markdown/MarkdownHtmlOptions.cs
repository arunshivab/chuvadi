namespace Chuvadi.Markup.Markdown
{
    /// <summary>How a soft line break is rendered.</summary>
    public enum SoftBreakStyle
    {
        /// <summary>A newline character (browsers show a space). Default.</summary>
        Newline,
        /// <summary>A space.</summary>
        Space,
        /// <summary>A <c>&lt;br /&gt;</c> (hard break).</summary>
        LineBreak,
    }

    /// <summary>How GFM task list markers are rendered.</summary>
    public enum TaskListStyle
    {
        /// <summary><c>&lt;input type="checkbox" disabled="" /&gt;</c>, as GitHub does. Default.</summary>
        DisabledCheckbox,
        /// <summary>A glyph (U+2610 ballot box, U+2611 checked) in a <c>span</c>; use when the sanitizer drops inputs.</summary>
        Glyph,
        /// <summary>The literal markers <c>[ ]</c> and <c>[x]</c>.</summary>
        Text,
    }

    /// <summary>How table column alignment is written.</summary>
    public enum TableAlignmentStyle
    {
        /// <summary><c>align="left"</c> (cmark-gfm default).</summary>
        AlignAttribute,
        /// <summary><c>style="text-align: left"</c>.</summary>
        StyleAttribute,
    }

    /// <summary>How link and image URLs with dangerous schemes are handled.</summary>
    public enum UnsafeUrlPolicy
    {
        /// <summary>
        /// Replace with an empty URL. Dangerous: <c>javascript:</c>, <c>vbscript:</c>, <c>file:</c> and
        /// <c>data:</c> other than <c>data:image/png|gif|jpeg|webp</c> (the cmark rule). Default.
        /// </summary>
        Remove,
        /// <summary>Emit URLs unchanged.</summary>
        Keep,
    }

    /// <summary>Options for <see cref="MarkdownHtmlRenderer"/>.</summary>
    public sealed class MarkdownHtmlOptions
    {
        /// <summary>Raw HTML handling. Default <see cref="RawHtmlMode.Escape"/>.</summary>
        public RawHtmlMode RawHtml { get; set; } = RawHtmlMode.Escape;

        /// <summary>GFM tag filter: with <see cref="RawHtmlMode.Passthrough"/>, neutralises <c>title, textarea, style, xmp, iframe, noembed, noframes, script, plaintext</c> tags by escaping their '&lt;'. Default true.</summary>
        public bool TagFilter { get; set; } = true;

        /// <summary>Dangerous URL handling. Default <see cref="UnsafeUrlPolicy.Remove"/>.</summary>
        public UnsafeUrlPolicy UnsafeUrls { get; set; } = UnsafeUrlPolicy.Remove;

        /// <summary>Soft break rendering. Default <see cref="SoftBreakStyle.Newline"/>.</summary>
        public SoftBreakStyle SoftBreak { get; set; } = SoftBreakStyle.Newline;

        /// <summary>Add GitHub-style <c>id</c> slugs to headings. Default false.</summary>
        public bool HeadingIds { get; set; }

        /// <summary>Prefix for heading ids (helps avoid clashes with the host page). Default empty.</summary>
        public string HeadingIdPrefix { get; set; } = string.Empty;

        /// <summary>Class prefix for fenced code languages; null or empty emits no class. Default <c>language-</c>.</summary>
        public string? CodeLanguageClassPrefix { get; set; } = "language-";

        /// <summary>Task list marker rendering. Default <see cref="TaskListStyle.DisabledCheckbox"/>.</summary>
        public TaskListStyle TaskLists { get; set; } = TaskListStyle.DisabledCheckbox;

        /// <summary>Table alignment rendering. Default <see cref="TableAlignmentStyle.AlignAttribute"/>.</summary>
        public TableAlignmentStyle TableAlignment { get; set; } = TableAlignmentStyle.AlignAttribute;

        /// <summary>Write void elements XHTML style (<c>&lt;br /&gt;</c>) instead of <c>&lt;br&gt;</c>. Default true.</summary>
        public bool XhtmlVoidElements { get; set; } = true;

        /// <summary>Add <c>data-sourcepos="line:col-line:col"</c> to block elements (cmark format). Default false.</summary>
        public bool SourcePositions { get; set; }

        /// <summary>Output limits (<see cref="MarkupLimits.MaxOutputChars"/>). Default <c>new MarkupLimits()</c>.</summary>
        public MarkupLimits Limits { get; set; } = new MarkupLimits();

        /// <summary>Returns a copy.</summary>
        public MarkdownHtmlOptions Clone()
        {
            var c = (MarkdownHtmlOptions)MemberwiseClone();
            c.Limits = Limits.Clone();
            return c;
        }
    }
}
