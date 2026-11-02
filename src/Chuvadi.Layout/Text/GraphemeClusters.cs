using System;

namespace Chuvadi.Layout.Text
{
    /// <summary>
    /// Extended grapheme cluster boundaries (UAX #29, revision 45, Unicode 16.0), including GB9c
    /// (Indic conjunct sequences) and GB11 (emoji ZWJ sequences). Conformant with GraphemeBreakTest.txt.
    /// Layout never breaks a line, places a caret, or applies a font change inside a cluster.
    /// </summary>
    public static class GraphemeClusters
    {
        /// <summary>
        /// Boundary flags for every UTF-16 position: element <c>i</c> is true when a cluster starts at
        /// <c>text[i]</c>; element <c>text.Length</c> (end of text) is true. Element 0 is true for non-empty text.
        /// </summary>
        public static bool[] GetBoundaries(ReadOnlySpan<char> text)
        {
            bool[] result = new bool[text.Length + 1];
            GetBoundaries(text, result);
            return result;
        }

        /// <summary>Fills <paramref name="result"/> (length must be <c>text.Length + 1</c>).</summary>
        public static void GetBoundaries(ReadOnlySpan<char> text, Span<bool> result)
        {
            if (result.Length != text.Length + 1)
            {
                throw new ArgumentException("result must have text.Length + 1 elements.", nameof(result));
            }
            result.Clear();
            result[text.Length] = true;
            if (text.Length == 0)
            {
                return;
            }
            result[0] = true;

            int prevStart = 0;
            int prevCp = Decode(text, 0, out int prevLen);
            TextProps prev = TextProps.Of(prevCp);
            // State for GB9c and GB11/GB12/13.
            bool incbConsonantSeen = prev.InCB == IndicConjunctBreak.Consonant;
            bool incbLinkerSeen = false;
            bool extPictThenExtends = prev.ExtendedPictographic;
            bool extPictZwj = false;
            int riCount = prev.Grapheme == GraphemeClusterBreak.RegionalIndicator ? 1 : 0;

            int i = prevStart + prevLen;
            while (i < text.Length)
            {
                int cp = Decode(text, i, out int len);
                TextProps cur = TextProps.Of(cp);
                bool brk = IsBoundary(prev, cur, incbConsonantSeen && incbLinkerSeen, extPictZwj, riCount);
                result[i] = brk;

                // Update GB9c state: Consonant [Extend Linker]* Linker [Extend Linker]* × Consonant
                IndicConjunctBreak ic = cur.InCB;
                if (ic == IndicConjunctBreak.Consonant)
                {
                    incbConsonantSeen = true;
                    incbLinkerSeen = false;
                }
                else if (ic == IndicConjunctBreak.Linker && incbConsonantSeen)
                {
                    incbLinkerSeen = true;
                }
                else if (ic != IndicConjunctBreak.Extend)
                {
                    incbConsonantSeen = false;
                    incbLinkerSeen = false;
                }

                // GB11 state: ExtPict Extend* ZWJ × ExtPict
                GraphemeClusterBreak g = cur.Grapheme;
                if (cur.ExtendedPictographic)
                {
                    extPictThenExtends = true;
                    extPictZwj = false;
                }
                else if (g == GraphemeClusterBreak.Extend && extPictThenExtends && !extPictZwj)
                {
                    // stays in ExtPict Extend*
                }
                else if (g == GraphemeClusterBreak.ZWJ && extPictThenExtends && !extPictZwj)
                {
                    extPictZwj = true;
                }
                else
                {
                    extPictThenExtends = false;
                    extPictZwj = false;
                }

                // GB12/13 state: count of consecutive RIs ending at cur.
                riCount = g == GraphemeClusterBreak.RegionalIndicator ? riCount + 1 : 0;

                prev = cur;
                i += len;
            }
        }

        /// <summary>The UTF-16 index where the cluster that starts at <paramref name="index"/> ends.</summary>
        public static int NextBoundary(ReadOnlySpan<char> text, int index)
        {
            if ((uint)index > (uint)text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            if (index == text.Length)
            {
                return index;
            }
            bool[] b = GetBoundaries(text);
            int j = index + 1;
            while (j < text.Length && !b[j])
            {
                j++;
            }
            return j;
        }

        private static bool IsBoundary(TextProps prev, TextProps cur, bool conjunctLinked, bool extPictZwj, int riCountBefore)
        {
            GraphemeClusterBreak a = prev.Grapheme;
            GraphemeClusterBreak b = cur.Grapheme;
            // GB3
            if (a == GraphemeClusterBreak.CR && b == GraphemeClusterBreak.LF)
            {
                return false;
            }
            // GB4, GB5
            if (a is GraphemeClusterBreak.Control or GraphemeClusterBreak.CR or GraphemeClusterBreak.LF)
            {
                return true;
            }
            if (b is GraphemeClusterBreak.Control or GraphemeClusterBreak.CR or GraphemeClusterBreak.LF)
            {
                return true;
            }
            // GB6-8
            if (a == GraphemeClusterBreak.L && b is GraphemeClusterBreak.L or GraphemeClusterBreak.V or GraphemeClusterBreak.LV or GraphemeClusterBreak.LVT)
            {
                return false;
            }
            if (a is GraphemeClusterBreak.LV or GraphemeClusterBreak.V && b is GraphemeClusterBreak.V or GraphemeClusterBreak.T)
            {
                return false;
            }
            if (a is GraphemeClusterBreak.LVT or GraphemeClusterBreak.T && b == GraphemeClusterBreak.T)
            {
                return false;
            }
            // GB9, GB9a, GB9b
            if (b is GraphemeClusterBreak.Extend or GraphemeClusterBreak.ZWJ or GraphemeClusterBreak.SpacingMark)
            {
                return false;
            }
            if (a == GraphemeClusterBreak.Prepend)
            {
                return false;
            }
            // GB9c
            if (conjunctLinked && cur.InCB == IndicConjunctBreak.Consonant)
            {
                return false;
            }
            // GB11
            if (extPictZwj && cur.ExtendedPictographic)
            {
                return false;
            }
            // GB12, GB13
            if (a == GraphemeClusterBreak.RegionalIndicator && b == GraphemeClusterBreak.RegionalIndicator)
            {
                return riCountBefore % 2 == 0;
            }
            return true;
        }

        internal static int Decode(ReadOnlySpan<char> text, int i, out int length)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                length = 2;
                return char.ConvertToUtf32(c, text[i + 1]);
            }
            length = 1;
            return c;
        }
    }
}
