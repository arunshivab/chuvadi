using System;
using Chuvadi.OpenType;
using Chuvadi.Text.Shaping.Unicode;

namespace Chuvadi.Text.Shaping;

/// <summary>A writing system, identified by its four-letter ISO 15924 code (for example "Deva").</summary>
/// <param name="Iso15924">The ISO 15924 code as a tag, first letter upper case ("Latn", "Taml").</param>
public readonly record struct Script(Tag Iso15924)
{
    /// <summary>Gets Latin (Latn).</summary>
    public static Script Latin { get; } = Parse("Latn");

    /// <summary>Gets Tamil (Taml).</summary>
    public static Script Tamil { get; } = Parse("Taml");

    /// <summary>Gets Devanagari (Deva).</summary>
    public static Script Devanagari { get; } = Parse("Deva");

    /// <summary>Gets Bengali (Beng).</summary>
    public static Script Bengali { get; } = Parse("Beng");

    /// <summary>Gets Gurmukhi (Guru).</summary>
    public static Script Gurmukhi { get; } = Parse("Guru");

    /// <summary>Gets Gujarati (Gujr).</summary>
    public static Script Gujarati { get; } = Parse("Gujr");

    /// <summary>Gets Odia, ISO 15924 Oriya (Orya).</summary>
    public static Script Odia { get; } = Parse("Orya");

    /// <summary>Gets Telugu (Telu).</summary>
    public static Script Telugu { get; } = Parse("Telu");

    /// <summary>Gets Kannada (Knda).</summary>
    public static Script Kannada { get; } = Parse("Knda");

    /// <summary>Gets Malayalam (Mlym).</summary>
    public static Script Malayalam { get; } = Parse("Mlym");

    /// <summary>Gets Common (Zyyy): characters used with several scripts, such as digits and punctuation.</summary>
    public static Script Common { get; } = Parse("Zyyy");

    /// <summary>Gets Inherited (Zinh): characters that take the script of the preceding character, such as combining marks.</summary>
    public static Script Inherited { get; } = Parse("Zinh");

    /// <summary>Gets Unknown (Zzzz): unassigned code points.</summary>
    public static Script Unknown { get; } = Parse("Zzzz");

    /// <summary>Parses a four-letter ISO 15924 code. Case is normalised to one upper-case letter followed by lower case.</summary>
    /// <param name="code">The code, for example "deva" or "Deva".</param>
    /// <returns>The script.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is not four ASCII letters.</exception>
    public static Script Parse(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 4)
        {
            throw new ArgumentException("An ISO 15924 code has four letters.", nameof(code));
        }

        Span<char> c = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            char ch = code[i];
            if (!char.IsAsciiLetter(ch))
            {
                throw new ArgumentException("An ISO 15924 code has four letters.", nameof(code));
            }

            c[i] = i == 0 ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch);
        }

        return new Script(Tag.Parse(new string(c)));
    }

    /// <summary>Gets the Unicode Script property of a code point (Unicode 17.0.0 data).</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The script; <see cref="Unknown"/> for unassigned or invalid code points.</returns>
    public static Script FromCodePoint(int codePoint)
    {
        // TRACE R-TS-0005
        return new Script(Tag.Parse(UnicodeData.GetScriptCode(codePoint)));
    }

    /// <summary>Returns the ISO 15924 code.</summary>
    /// <returns>The four-letter code.</returns>
    public override string ToString() => Iso15924.ToString();
}

/// <summary>An OpenType language system, identified by its OpenType language-system tag (for example "MAR " for Marathi).</summary>
/// <param name="OpenTypeTag">The language-system tag.</param>
public readonly record struct Language(Tag OpenTypeTag)
{
    /// <summary>Gets the default language system ("dflt"): the script's default rules in the font.</summary>
    public static Language Default { get; } = new Language(Tag.Parse("dflt"));

    /// <summary>Returns the tag.</summary>
    /// <returns>The four-character tag.</returns>
    public override string ToString() => OpenTypeTag.ToString();
}

/// <summary>The direction text is laid out in.</summary>
public enum TextDirection
{
    /// <summary>Left to right.</summary>
    LeftToRight,

    /// <summary>Right to left. Not supported yet; shaping throws <see cref="NotSupportedException"/>.</summary>
    RightToLeft,
}

/// <summary>An OpenType feature setting: a tag, a value, and the UTF-16 range of the text it applies to.</summary>
/// <param name="Tag">The feature tag, for example "liga".</param>
/// <param name="Value">0 turns the feature off; 1 turns it on; larger values pick an alternate (for features such as 'aalt' or 'salt').</param>
/// <param name="Start">The first UTF-16 index the setting covers.</param>
/// <param name="End">The UTF-16 index just after the last one covered; <see cref="int.MaxValue"/> means to the end.</param>
public readonly record struct Feature(Tag Tag, uint Value = 1, int Start = 0, int End = int.MaxValue)
{
    /// <summary>Creates a setting for the whole text from a tag string.</summary>
    /// <param name="tag">The feature tag.</param>
    /// <param name="value">The value.</param>
    /// <returns>The setting.</returns>
    public static Feature Create(string tag, uint value = 1) => new Feature(Tag.Parse(tag), value);
}
