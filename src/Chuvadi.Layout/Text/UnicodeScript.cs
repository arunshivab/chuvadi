using System;

namespace Chuvadi.Layout.Text
{
    /// <summary>
    /// Unicode Script property (UAX #24) values Chuvadi.Layout distinguishes. Scripts not listed
    /// individually report <see cref="Other"/>. Values are derived from UCD 16.0.0 Scripts.txt.
    /// </summary>
    public enum UnicodeScript : byte
    {
        Unknown = 0,
        Common,
        Inherited,
        Latin,
        Greek,
        Cyrillic,
        Armenian,
        Hebrew,
        Arabic,
        Syriac,
        Thaana,
        Devanagari,
        Bengali,
        Gurmukhi,
        Gujarati,
        Oriya,
        Tamil,
        Telugu,
        Kannada,
        Malayalam,
        Sinhala,
        Thai,
        Lao,
        Tibetan,
        Myanmar,
        Georgian,
        Hangul,
        Ethiopic,
        Khmer,
        Mongolian,
        Hiragana,
        Katakana,
        Bopomofo,
        Han,
        Other,
    }

    /// <summary>Script lookups.</summary>
    public static class UnicodeScripts
    {
        /// <summary>The Unicode version the tables were generated from.</summary>
        public const string UnicodeVersion = UnicodeScriptData.UnicodeVersion;

        /// <summary>Script property of a code point (U+0000..U+10FFFF); <see cref="UnicodeScript.Unknown"/> outside that range.</summary>
        public static UnicodeScript GetScript(int codepoint)
        {
            if ((uint)codepoint < 0x10000)
            {
                return (UnicodeScript)Bmp.Table[codepoint];
            }
            if ((uint)codepoint > 0x10FFFF)
            {
                return UnicodeScript.Unknown;
            }
            int i = Array.BinarySearch(UnicodeScriptData.RangeStarts, codepoint);
            if (i < 0)
            {
                i = ~i - 1;
            }
            return (UnicodeScript)UnicodeScriptData.RangeScripts[i];
        }

        /// <summary>Script of every BMP code point (64 KB), built once from the range data.</summary>
        private static class Bmp
        {
            public static readonly byte[] Table = Build();

            private static byte[] Build()
            {
                byte[] t = new byte[0x10000];
                int[] starts = UnicodeScriptData.RangeStarts;
                byte[] scripts = UnicodeScriptData.RangeScripts;
                int r = 0;
                for (int cp = 0; cp < t.Length; cp++)
                {
                    while (r + 1 < starts.Length && starts[r + 1] <= cp)
                    {
                        r++;
                    }
                    t[cp] = cp >= starts[0] ? scripts[r] : (byte)UnicodeScript.Unknown;
                }
                return t;
            }
        }

        /// <summary>True for the Brahmic scripts that need OpenType shaping (reordering, conjuncts) to render correctly.</summary>
        public static bool IsIndic(UnicodeScript script) => script is UnicodeScript.Devanagari or UnicodeScript.Bengali
            or UnicodeScript.Gurmukhi or UnicodeScript.Gujarati or UnicodeScript.Oriya or UnicodeScript.Tamil
            or UnicodeScript.Telugu or UnicodeScript.Kannada or UnicodeScript.Malayalam or UnicodeScript.Sinhala;

        /// <summary>True for scripts written right-to-left. Bidi reordering is not part of Chuvadi.Layout 1.0.</summary>
        public static bool IsRightToLeft(UnicodeScript script) => script is UnicodeScript.Hebrew or UnicodeScript.Arabic
            or UnicodeScript.Syriac or UnicodeScript.Thaana;

        /// <summary>True for scripts that need complex shaping beyond cmap + kerning (Indic, Arabic-family, Thai-family, Tibetan, Myanmar, Khmer, Mongolian).</summary>
        public static bool NeedsComplexShaping(UnicodeScript script) => IsIndic(script) || IsRightToLeft(script)
            || script is UnicodeScript.Thai or UnicodeScript.Lao or UnicodeScript.Tibetan or UnicodeScript.Myanmar
            or UnicodeScript.Khmer or UnicodeScript.Mongolian;
    }
}
