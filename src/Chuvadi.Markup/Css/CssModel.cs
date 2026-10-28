using System.Collections.Generic;

namespace Chuvadi.Markup.Css
{
    /// <summary>A parsed style sheet.</summary>
    public sealed class CssStyleSheet
    {
        /// <summary>Top-level rules.</summary>
        public List<CssRule> Rules { get; } = new List<CssRule>();

        /// <summary>Serializes back to CSS text.</summary>
        public override string ToString() => CssSerializer.Serialize(this);
    }

    /// <summary>Base class of rules.</summary>
    public abstract class CssRule
    {
        /// <summary>Prelude component values (selector list for style rules, condition for at-rules).</summary>
        public List<CssComponentValue> Prelude { get; } = new List<CssComponentValue>();

        /// <summary>Declarations in the rule's block.</summary>
        public List<CssDeclaration> Declarations { get; } = new List<CssDeclaration>();

        /// <summary>Nested rules in the rule's block (CSS nesting, @media contents, keyframes).</summary>
        public List<CssRule> Rules { get; } = new List<CssRule>();

        /// <summary>The prelude serialized as text.</summary>
        public string PreludeText => CssSerializer.Serialize(Prelude).Trim();
    }

    /// <summary>A style rule (selector list plus block).</summary>
    public sealed class CssStyleRule : CssRule
    {
        /// <summary>The selector list as text.</summary>
        public string SelectorText => PreludeText;
    }

    /// <summary>An at-rule (@media, @font-face, @import...).</summary>
    public sealed class CssAtRule : CssRule
    {
        /// <summary>Creates an at-rule.</summary>
        public CssAtRule(string name) => Name = name;

        /// <summary>Name without '@' (escapes resolved, original case).</summary>
        public string Name { get; }

        /// <summary>True if the rule had a {} block (false for statement rules like @import).</summary>
        public bool HasBlock { get; set; }
    }

    /// <summary>A declaration (<c>name: value !important</c>).</summary>
    public sealed class CssDeclaration
    {
        /// <summary>Creates a declaration.</summary>
        public CssDeclaration(string name) => Name = name;

        /// <summary>Property name (ASCII-lowercased unless it is a custom property starting with "--").</summary>
        public string Name { get; set; }

        /// <summary>Value component values (whitespace trimmed, without !important).</summary>
        public List<CssComponentValue> Value { get; } = new List<CssComponentValue>();

        /// <summary>The !important flag.</summary>
        public bool Important { get; set; }

        /// <summary>True for custom properties (names starting with "--").</summary>
        public bool IsCustomProperty => Name.StartsWith("--", System.StringComparison.Ordinal);

        /// <summary>The value serialized as text.</summary>
        public string ValueText => CssSerializer.Serialize(Value).Trim();

        /// <inheritdoc />
        public override string ToString() => Name + ": " + ValueText + (Important ? " !important" : string.Empty);
    }
}
