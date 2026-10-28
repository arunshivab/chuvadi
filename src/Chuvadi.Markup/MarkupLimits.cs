using System;

namespace Chuvadi.Markup
{
    /// <summary>
    /// Resource limits applied by every parser, renderer and sanitizer in Chuvadi.Markup.
    /// Untrusted input can never make the library allocate past these limits or run without bound:
    /// a limit that cannot be degraded gracefully throws <see cref="MarkupLimitExceededException"/>.
    /// Each property documents whether exceeding it throws or degrades.
    /// </summary>
    /// <remarks>
    /// A new instance holds the default limits, sized for real-world documents and HTML email.
    /// <see cref="Strict"/> returns tighter limits suitable for a server handling hostile input.
    /// Instances are mutable; parsers read the values once at the start of a call.
    /// </remarks>
    public sealed class MarkupLimits
    {
        /// <summary>Maximum input length in UTF-16 chars (bytes for byte input). Throws. Default 32 Mi.</summary>
        public int MaxInputChars { get; set; } = 32 * 1024 * 1024;

        /// <summary>Maximum number of nodes (AST nodes, DOM nodes, CSS rules) a single call may create. Throws. Default 4 Mi.</summary>
        public int MaxNodes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Maximum tree depth. Degrades, never throws: HTML elements deeper than this are attached to the
        /// deepest allowed ancestor (the behaviour of Chromium-based browsers at 512); Markdown container
        /// blocks deeper than this are not opened and their markers are read as text; Markdown emphasis
        /// nested deeper keeps its text but loses the formatting. Default 512.
        /// </summary>
        public int MaxNestingDepth { get; set; } = 512;

        /// <summary>
        /// Bound on parser work, as steps per input char: HTML tree construction (scope checks, stack and
        /// formatting-list walks) and Markdown inline parsing (delimiter and bracket processing). Real
        /// documents use a few steps per char; adversarial input that would make either algorithm
        /// super-linear throws instead of running unbounded. Default 64.
        /// </summary>
        public int MaxParserWorkPerChar { get; set; } = 64;

        /// <summary>Maximum attributes kept per HTML element; further attributes are dropped and a parse error recorded. Default 512.</summary>
        public int MaxAttributesPerElement { get; set; } = 512;

        /// <summary>Maximum length of a single attribute value; longer values are truncated by the tokenizer and a parse error recorded. Default 32 Mi.</summary>
        public int MaxAttributeValueChars { get; set; } = 32 * 1024 * 1024;

        /// <summary>Maximum output length in chars for renderers and serializers. Throws. Default 128 Mi.</summary>
        public long MaxOutputChars { get; set; } = 128L * 1024 * 1024;

        /// <summary>Maximum decoded size of a single <c>data:</c> URI the sanitizer keeps; larger URIs are removed. Default 16 MiB.</summary>
        public int MaxDataUriBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>Maximum columns in a Markdown (GFM) table; a delimiter row with more cells does not start a table. Default 1000.</summary>
        public int MaxTableColumns { get; set; } = 1000;

        /// <summary>Maximum total cells (rows x columns) across all Markdown tables in a document; further rows are not added to a table (they read as paragraphs). Default 1 Mi.</summary>
        public int MaxTableCells { get; set; } = 1024 * 1024;

        /// <summary>Maximum link reference definitions kept in a Markdown document; further definitions are read as text. Default 100 000.</summary>
        public int MaxLinkReferenceDefinitions { get; set; } = 100_000;

        /// <summary>Maximum parse errors recorded per call; later errors are counted but not stored. Default 1000.</summary>
        public int MaxRecordedErrors { get; set; } = 1000;

        /// <summary>Returns tighter limits for servers that process hostile input (for example mail).</summary>
        public static MarkupLimits Strict() => new MarkupLimits
        {
            MaxInputChars = 8 * 1024 * 1024,
            MaxNodes = 500_000,
            MaxNestingDepth = 256,
            MaxParserWorkPerChar = 32,
            MaxAttributesPerElement = 64,
            MaxAttributeValueChars = 8 * 1024 * 1024,
            MaxOutputChars = 32L * 1024 * 1024,
            MaxDataUriBytes = 4 * 1024 * 1024,
            MaxTableColumns = 256,
            MaxTableCells = 200_000,
            MaxLinkReferenceDefinitions = 10_000,
            MaxRecordedErrors = 200,
        };

        /// <summary>Returns a copy of these limits.</summary>
        public MarkupLimits Clone() => (MarkupLimits)MemberwiseClone();

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> if any limit is not positive.</summary>
        public void Validate()
        {
            Positive(MaxInputChars, nameof(MaxInputChars));
            Positive(MaxNodes, nameof(MaxNodes));
            Positive(MaxNestingDepth, nameof(MaxNestingDepth));
            Positive(MaxParserWorkPerChar, nameof(MaxParserWorkPerChar));
            Positive(MaxAttributesPerElement, nameof(MaxAttributesPerElement));
            Positive(MaxAttributeValueChars, nameof(MaxAttributeValueChars));
            Positive(MaxOutputChars, nameof(MaxOutputChars));
            Positive(MaxDataUriBytes, nameof(MaxDataUriBytes));
            Positive(MaxTableColumns, nameof(MaxTableColumns));
            Positive(MaxTableCells, nameof(MaxTableCells));
            Positive(MaxLinkReferenceDefinitions, nameof(MaxLinkReferenceDefinitions));
            if (MaxRecordedErrors < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxRecordedErrors), MaxRecordedErrors, "Must not be negative.");
        }

        private static void Positive(long value, string name)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(name, value, "Must be positive.");
        }
    }
}
