using System.Text;

namespace Chuvadi.Markup.Html
{
    /// <summary>Options for <see cref="HtmlParser"/>.</summary>
    public sealed class HtmlParserOptions
    {
        /// <summary>
        /// The parser's scripting flag. True (default) parses <c>noscript</c> content as raw text, exactly as
        /// a browser with scripting enabled does; this keeps the parsed tree identical to what a web view
        /// builds, which is what matters for sanitization. False parses <c>noscript</c> content as markup.
        /// </summary>
        public bool Scripting { get; set; } = true;

        /// <summary>Record parse errors (up to <see cref="MarkupLimits.MaxRecordedErrors"/>). Default true.</summary>
        public bool CollectErrors { get; set; } = true;

        /// <summary>For byte input: an encoding to use regardless of BOM or meta declarations (for example the MIME charset). Default null.</summary>
        public Encoding? Encoding { get; set; }

        /// <summary>For byte input without a BOM, decide the encoding from a <c>&lt;meta&gt;</c> prescan of the first 1024 bytes. Default true.</summary>
        public bool PrescanMeta { get; set; } = true;

        /// <summary>For byte input: the encoding when nothing else decides. Default windows-1252 (the browser default for most locales).</summary>
        public Encoding? FallbackEncoding { get; set; }

        /// <summary>Resource limits. Default <c>new MarkupLimits()</c>.</summary>
        public MarkupLimits Limits { get; set; } = new MarkupLimits();

        /// <summary>Returns a copy.</summary>
        public HtmlParserOptions Clone()
        {
            var c = (HtmlParserOptions)MemberwiseClone();
            c.Limits = Limits.Clone();
            return c;
        }
    }
}
