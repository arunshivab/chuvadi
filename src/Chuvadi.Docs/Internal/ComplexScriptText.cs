using System.Text;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Docs 2.2.0, N4 (D-343): the Unicode blocks whose text Word only shows in a run's complex-script font when the
/// run is marked <c>w:cs</c>. A fixed list, documented in the README and CHANGELOG; it does not change with the runtime.
/// </summary>
internal static class ComplexScriptText
{
    /// <summary>The blocks, as inclusive code point ranges, in code point order.</summary>
    internal static readonly (int First, int Last, string Name)[] Blocks =
    {
        (0x0590, 0x05FF, "Hebrew"),
        (0x0600, 0x06FF, "Arabic"),
        (0x0700, 0x074F, "Syriac"),
        (0x0750, 0x077F, "Arabic Supplement"),
        (0x0780, 0x07BF, "Thaana"),
        (0x07C0, 0x07FF, "NKo"),
        (0x0800, 0x083F, "Samaritan"),
        (0x0840, 0x085F, "Mandaic"),
        (0x0860, 0x086F, "Syriac Supplement"),
        (0x0870, 0x089F, "Arabic Extended-B"),
        (0x08A0, 0x08FF, "Arabic Extended-A"),
        (0x0900, 0x097F, "Devanagari"),
        (0x0980, 0x09FF, "Bengali"),
        (0x0A00, 0x0A7F, "Gurmukhi"),
        (0x0A80, 0x0AFF, "Gujarati"),
        (0x0B00, 0x0B7F, "Oriya"),
        (0x0B80, 0x0BFF, "Tamil"),
        (0x0C00, 0x0C7F, "Telugu"),
        (0x0C80, 0x0CFF, "Kannada"),
        (0x0D00, 0x0D7F, "Malayalam"),
        (0x0D80, 0x0DFF, "Sinhala"),
        (0x0E00, 0x0E7F, "Thai"),
        (0x0E80, 0x0EFF, "Lao"),
        (0x0F00, 0x0FFF, "Tibetan"),
        (0x1000, 0x109F, "Myanmar"),
        (0x1780, 0x17FF, "Khmer"),
        (0x1800, 0x18AF, "Mongolian"),
        (0x19E0, 0x19FF, "Khmer Symbols"),
        (0x1CD0, 0x1CFF, "Vedic Extensions"),
        (0xA8E0, 0xA8FF, "Devanagari Extended"),
        (0xA9E0, 0xA9FF, "Myanmar Extended-B"),
        (0xAA60, 0xAA7F, "Myanmar Extended-A"),
        (0xFB1D, 0xFB4F, "Alphabetic Presentation Forms (Hebrew)"),
        (0xFB50, 0xFDFF, "Arabic Presentation Forms-A"),
        (0xFE70, 0xFEFF, "Arabic Presentation Forms-B"),
        (0x10E60, 0x10E7F, "Rumi Numeral Symbols"),
        (0x11FC0, 0x11FFF, "Tamil Supplement"),
        (0x1EE00, 0x1EEFF, "Arabic Mathematical Alphabetic Symbols"),
    };

    /// <summary>True when <paramref name="text"/> has at least one character in <see cref="Blocks"/>.</summary>
    public static bool Contains(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var rune in text.EnumerateRunes())
        {
            int v = rune.Value;
            if (v < 0x0590) continue; // Latin, Greek, Cyrillic, Armenian: never
            foreach (var (first, last, _) in Blocks)
            {
                if (v < first) break;
                if (v <= last) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether a caller-built run gets <c>w:cs</c> although <see cref="Model.TextFormat.ComplexScript"/> is unset (N4):
    /// a format built in code (<see cref="Model.TextFormat.MirrorToComplexScript"/>), with a complex-script font and
    /// complex-script text. Loaded formats never qualify, so loaded documents save as they were.
    /// </summary>
    public static bool ImpliesFlag(Model.TextFormat f, string? text)
        => f.ComplexScript is null && f.MirrorToComplexScript && f.ComplexScriptFont is not null && Contains(text);
}
