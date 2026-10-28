using System;
using System.Collections.Generic;

namespace Chuvadi.Markup.Html
{
    /// <summary>Kind of an <see cref="HtmlToken"/>.</summary>
    public enum HtmlTokenKind
    {
        /// <summary>A DOCTYPE.</summary>
        Doctype,
        /// <summary>A start tag.</summary>
        StartTag,
        /// <summary>An end tag.</summary>
        EndTag,
        /// <summary>A comment (including bogus comments).</summary>
        Comment,
        /// <summary>A run of characters.</summary>
        Character,
        /// <summary>End of input.</summary>
        EndOfFile,
    }

    /// <summary>A name/value pair on a tag token, as written (names lowercased by the tokenizer).</summary>
    public readonly record struct HtmlTokenAttribute(string Name, string Value);

    /// <summary>A token produced by <see cref="HtmlTokenizer"/>.</summary>
    public sealed class HtmlToken
    {
        internal HtmlToken(HtmlTokenKind kind) => Kind = kind;

        /// <summary>The token kind.</summary>
        public HtmlTokenKind Kind { get; internal set; }

        /// <summary>Tag or DOCTYPE name (lowercase); null for a DOCTYPE with a missing name.</summary>
        public string? Name { get; internal set; }

        /// <summary>Character data or comment data.</summary>
        public string Data { get; internal set; } = string.Empty;

        /// <summary>Tag attributes in source order (duplicates already removed, first wins).</summary>
        public IReadOnlyList<HtmlTokenAttribute> Attributes => (IReadOnlyList<HtmlTokenAttribute>?)AttributeList ?? Array.Empty<HtmlTokenAttribute>();

        internal List<HtmlTokenAttribute>? AttributeList;

        /// <summary>The self-closing flag of a tag token.</summary>
        public bool SelfClosing { get; internal set; }

        /// <summary>DOCTYPE public identifier, or null if missing.</summary>
        public string? PublicId { get; internal set; }

        /// <summary>DOCTYPE system identifier, or null if missing.</summary>
        public string? SystemId { get; internal set; }

        /// <summary>DOCTYPE force-quirks flag.</summary>
        public bool ForceQuirks { get; internal set; }

        /// <summary>Offset in the (newline-normalized) input where the token starts.</summary>
        public int Offset { get; internal set; }

        internal bool SelfClosingAcknowledged;

        /// <summary>Gets an attribute value by name, or null.</summary>
        public string? GetAttribute(string name)
        {
            if (AttributeList is null) return null;
            foreach (var a in AttributeList)
                if (a.Name == name) return a.Value;
            return null;
        }

        /// <inheritdoc />
        public override string ToString() => Kind switch
        {
            HtmlTokenKind.StartTag => "<" + Name + (SelfClosing ? "/>" : ">"),
            HtmlTokenKind.EndTag => "</" + Name + ">",
            HtmlTokenKind.Comment => "<!--" + Data + "-->",
            HtmlTokenKind.Doctype => "<!DOCTYPE " + Name + ">",
            HtmlTokenKind.Character => "\"" + Data + "\"",
            _ => "EOF",
        };
    }

    /// <summary>A parse error reported by the tokenizer or tree builder (WHATWG error codes where defined).</summary>
    public sealed class HtmlParseError
    {
        internal HtmlParseError(string code, int offset)
        {
            Code = code;
            Offset = offset;
        }

        /// <summary>Error code, e.g. <c>unexpected-null-character</c>.</summary>
        public string Code { get; }

        /// <summary>Offset in the newline-normalized input.</summary>
        public int Offset { get; }

        /// <summary>One-based line.</summary>
        public int Line { get; internal set; }

        /// <summary>One-based column.</summary>
        public int Column { get; internal set; }

        /// <inheritdoc />
        public override string ToString() => "(" + Line + "," + Column + "): " + Code;
    }
}
