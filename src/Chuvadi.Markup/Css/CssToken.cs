using System;

namespace Chuvadi.Markup.Css
{
    /// <summary>CSS Syntax Level 3 token types.</summary>
    public enum CssTokenType
    {
        /// <summary>An identifier.</summary>
        Ident,
        /// <summary>A function name followed by '(' (the arguments are in a <see cref="CssFunction"/>).</summary>
        Function,
        /// <summary>@keyword.</summary>
        AtKeyword,
        /// <summary>#name.</summary>
        Hash,
        /// <summary>A quoted string.</summary>
        String,
        /// <summary>A string interrupted by a newline.</summary>
        BadString,
        /// <summary>An unquoted url(...).</summary>
        Url,
        /// <summary>A malformed url(...).</summary>
        BadUrl,
        /// <summary>A single code point not otherwise tokenized.</summary>
        Delim,
        /// <summary>A number.</summary>
        Number,
        /// <summary>A percentage.</summary>
        Percentage,
        /// <summary>A number with a unit.</summary>
        Dimension,
        /// <summary>Whitespace.</summary>
        Whitespace,
        /// <summary>&lt;!--</summary>
        Cdo,
        /// <summary>--&gt;</summary>
        Cdc,
        /// <summary>:</summary>
        Colon,
        /// <summary>;</summary>
        Semicolon,
        /// <summary>,</summary>
        Comma,
        /// <summary>[</summary>
        OpenSquare,
        /// <summary>]</summary>
        CloseSquare,
        /// <summary>(</summary>
        OpenParen,
        /// <summary>)</summary>
        CloseParen,
        /// <summary>{</summary>
        OpenCurly,
        /// <summary>}</summary>
        CloseCurly,
        /// <summary>End of input.</summary>
        EndOfFile,
    }

    /// <summary>Base of CSS component values: a preserved <see cref="CssToken"/>, a <see cref="CssFunction"/> or a <see cref="CssSimpleBlock"/>.</summary>
    public abstract class CssComponentValue
    {
    }

    /// <summary>A CSS token.</summary>
    public sealed class CssToken : CssComponentValue
    {
        internal CssToken(CssTokenType type, string value = "")
        {
            Type = type;
            Value = value;
        }

        /// <summary>Token type.</summary>
        public CssTokenType Type { get; }

        /// <summary>
        /// Ident/function/at-keyword/hash name, string or url value (escapes resolved), delim char,
        /// or the original text of a number.
        /// </summary>
        public string Value { get; }

        /// <summary>For hash tokens: true if the name would be a valid identifier ("id" type).</summary>
        public bool IsIdHash { get; internal set; }

        /// <summary>For numeric tokens: the numeric value.</summary>
        public double Number { get; internal set; }

        /// <summary>For numeric tokens: true if written as an integer.</summary>
        public bool IsInteger { get; internal set; }

        /// <summary>For dimension tokens: the unit (escapes resolved).</summary>
        public string Unit { get; internal set; } = string.Empty;

        /// <summary>Creates an ident token.</summary>
        public static CssToken Ident(string name) => new CssToken(CssTokenType.Ident, name ?? throw new ArgumentNullException(nameof(name)));

        /// <summary>Creates a string token.</summary>
        public static CssToken StringToken(string value) => new CssToken(CssTokenType.String, value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>Creates a whitespace token.</summary>
        public static CssToken Space() => new CssToken(CssTokenType.Whitespace, " ");

        /// <summary>Creates a delim token.</summary>
        public static CssToken Delim(char c) => new CssToken(CssTokenType.Delim, c.ToString());

        /// <inheritdoc />
        public override string ToString() => Type + "(" + Value + ")";
    }

    /// <summary>A function: name and argument component values (the closing paren is implied).</summary>
    public sealed class CssFunction : CssComponentValue
    {
        /// <summary>Creates a function.</summary>
        public CssFunction(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));

        /// <summary>Function name (escapes resolved, original case).</summary>
        public string Name { get; }

        /// <summary>Arguments.</summary>
        public System.Collections.Generic.List<CssComponentValue> Arguments { get; } = new System.Collections.Generic.List<CssComponentValue>();
    }

    /// <summary>A [], () or {} block.</summary>
    public sealed class CssSimpleBlock : CssComponentValue
    {
        /// <summary>Creates a block.</summary>
        public CssSimpleBlock(char open) => Open = open;

        /// <summary>The opening character: '[', '(' or '{'.</summary>
        public char Open { get; }

        /// <summary>The closing character.</summary>
        public char Close => Open == '[' ? ']' : Open == '(' ? ')' : '}';

        /// <summary>Contents.</summary>
        public System.Collections.Generic.List<CssComponentValue> Values { get; } = new System.Collections.Generic.List<CssComponentValue>();
    }
}
