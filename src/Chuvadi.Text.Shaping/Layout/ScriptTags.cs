using System;
using System.Collections.Concurrent;
using Chuvadi.OpenType;

namespace Chuvadi.Text.Shaping.Layout;

/// <summary>The OpenType script tags for a Unicode script, in the order a font's script list is searched.</summary>
internal static class ScriptTags
{
    // TRACE R-TS-0013
    private static readonly Tag[] Dev = Tags("dev2", "deva");
    private static readonly Tag[] Beng = Tags("bng2", "beng");
    private static readonly Tag[] Guru = Tags("gur2", "guru");
    private static readonly Tag[] Gujr = Tags("gjr2", "gujr");
    private static readonly Tag[] Orya = Tags("ory2", "orya");
    private static readonly Tag[] Taml = Tags("tml2", "taml");
    private static readonly Tag[] Telu = Tags("tel2", "telu");
    private static readonly Tag[] Knda = Tags("knd2", "knda");
    private static readonly Tag[] Mlym = Tags("mlm2", "mlym");
    private static readonly Tag[] Latn = Tags("latn");

    // Other scripts: the lower-case ISO code, built once per script and reused, so a call does not allocate.
    private static readonly ConcurrentDictionary<uint, Tag[]> Others = new ConcurrentDictionary<uint, Tag[]>();

    /// <summary>The OpenType default script tag.</summary>
    internal static readonly Tag Default = Tag.Parse("DFLT");

    /// <summary>
    /// The script's own tags: for the Indic scripts the version 2 tag first, then the original one; otherwise the
    /// ISO 15924 code in lower case. Empty for Common, Inherited and Unknown.
    /// </summary>
    internal static ReadOnlySpan<Tag> For(Script script)
    {
        if (script == Script.Devanagari)
        {
            return Dev;
        }

        if (script == Script.Bengali)
        {
            return Beng;
        }

        if (script == Script.Gurmukhi)
        {
            return Guru;
        }

        if (script == Script.Gujarati)
        {
            return Gujr;
        }

        if (script == Script.Odia)
        {
            return Orya;
        }

        if (script == Script.Tamil)
        {
            return Taml;
        }

        if (script == Script.Telugu)
        {
            return Telu;
        }

        if (script == Script.Kannada)
        {
            return Knda;
        }

        if (script == Script.Malayalam)
        {
            return Mlym;
        }

        if (script == Script.Latin)
        {
            return Latn;
        }

        if (script == Script.Common || script == Script.Inherited || script == Script.Unknown)
        {
            return ReadOnlySpan<Tag>.Empty;
        }

        // The ISO 15924 tag has its first letter in upper case; the OpenType tag is all lower case (ASCII bit 0x20).
        return Others.GetOrAdd(script.Iso15924.Value, static v => new[] { new Tag(v | 0x20000000u) });
    }

    private static Tag[] Tags(params string[] tags) => Array.ConvertAll(tags, Tag.Parse);
}
