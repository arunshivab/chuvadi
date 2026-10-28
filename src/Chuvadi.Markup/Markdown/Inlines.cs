using System;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>Literal text.</summary>
    public sealed class TextInline : MarkdownInline, ILiteralNode
    {
        /// <summary>Creates a text node.</summary>
        public TextInline(string text) => Text = text ?? throw new ArgumentNullException(nameof(text));
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Text;
        /// <summary>The text, with escapes and entities resolved.</summary>
        public string Text { get; set; }
        string ILiteralNode.Literal => Text;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitText(this);
    }

    /// <summary>A code span.</summary>
    public sealed class CodeSpan : MarkdownInline, ILiteralNode
    {
        /// <summary>Creates a code span.</summary>
        public CodeSpan(string code) => Code = code ?? throw new ArgumentNullException(nameof(code));
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.CodeSpan;
        /// <summary>The code.</summary>
        public string Code { get; set; }
        /// <summary>Number of backticks in the delimiter run (1 for nodes built in code).</summary>
        public int DelimiterLength { get; set; } = 1;
        string ILiteralNode.Literal => Code;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitCodeSpan(this);
    }

    /// <summary>Emphasis (<c>*a*</c>, <c>_a_</c>).</summary>
    public sealed class Emphasis : MarkdownInline
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Emphasis;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>Delimiter character ('*' or '_').</summary>
        public char Delimiter { get; set; } = '*';
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitEmphasis(this);
    }

    /// <summary>Strong emphasis (<c>**a**</c>, <c>__a__</c>).</summary>
    public sealed class Strong : MarkdownInline
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Strong;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>Delimiter character ('*' or '_').</summary>
        public char Delimiter { get; set; } = '*';
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitStrong(this);
    }

    /// <summary>GFM strikethrough (<c>~a~</c>, <c>~~a~~</c>).</summary>
    public sealed class Strikethrough : MarkdownInline
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Strikethrough;
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>Number of tildes (1 or 2).</summary>
        public int DelimiterLength { get; set; } = 2;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitStrikethrough(this);
    }

    /// <summary>How a link or image was written.</summary>
    public enum LinkStyle
    {
        /// <summary><c>[text](dest "title")</c>.</summary>
        Inline,
        /// <summary><c>[text][label]</c>.</summary>
        ReferenceFull,
        /// <summary><c>[label][]</c>.</summary>
        ReferenceCollapsed,
        /// <summary><c>[label]</c>.</summary>
        ReferenceShortcut,
        /// <summary><c>&lt;https://...&gt;</c> or <c>&lt;user@host&gt;</c>.</summary>
        Autolink,
        /// <summary>GFM extended autolink (bare <c>www.</c>, <c>https://</c> or email).</summary>
        ExtendedAutolink,
    }

    /// <summary>Base for <see cref="Link"/> and <see cref="Image"/>.</summary>
    public abstract class LinkBase : MarkdownInline
    {
        /// <inheritdoc />
        public override bool CanHaveChildren => true;
        /// <summary>Destination (escapes and entities resolved; not URL-encoded).</summary>
        public string Destination { get; set; } = string.Empty;
        /// <summary>Title, or null.</summary>
        public string? Title { get; set; }
        /// <summary>How it was written.</summary>
        public LinkStyle Style { get; set; }
        /// <summary>Reference label for reference links, otherwise null.</summary>
        public string? Label { get; set; }
    }

    /// <summary>A link; children are the link text.</summary>
    public sealed class Link : LinkBase
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Link;
        /// <summary>True when the link is an email autolink (destination already has <c>mailto:</c>).</summary>
        public bool IsEmail { get; set; }
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitLink(this);
    }

    /// <summary>An image; children are the alt text inlines.</summary>
    public sealed class Image : LinkBase
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.Image;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitImage(this);
    }

    /// <summary>Raw inline HTML (an open tag, closing tag, comment, processing instruction, declaration or CDATA).</summary>
    public sealed class HtmlInline : MarkdownInline, ILiteralNode
    {
        /// <summary>Creates the node.</summary>
        public HtmlInline(string html) => Html = html ?? throw new ArgumentNullException(nameof(html));
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.HtmlInline;
        /// <summary>The raw HTML.</summary>
        public string Html { get; set; }
        string ILiteralNode.Literal => Html;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitHtmlInline(this);
    }

    /// <summary>A hard line break (two trailing spaces or a backslash before a newline).</summary>
    public sealed class HardBreak : MarkdownInline
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.HardBreak;
        /// <summary>True if written as a backslash, false if as trailing spaces.</summary>
        public bool IsBackslash { get; set; }
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitHardBreak(this);
    }

    /// <summary>A soft line break (a newline inside a paragraph).</summary>
    public sealed class SoftBreak : MarkdownInline
    {
        /// <inheritdoc />
        public override MarkdownNodeKind Kind => MarkdownNodeKind.SoftBreak;
        /// <inheritdoc />
        public override void Accept(MarkdownVisitor visitor) => visitor.VisitSoftBreak(this);
    }
}
