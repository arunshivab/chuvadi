using System;

namespace Chuvadi.Layout.Text
{
    /// <summary>Unicode Line_Break property values (UAX #14, Unicode 16.0), in data-file order.</summary>
    public enum LineBreakClass : byte
    {
        BK, CR, LF, CM, NL, SG, WJ, ZW, GL, SP, ZWJ, B2, BA, BB, HY, CB, CL, CP, EX, IN,
        NS, OP, QU, IS, NU, PO, PR, SY, AI, AK, AL, AP, AS, CJ, EB, EM, H2, H3, HL, ID,
        JL, JV, JT, RI, SA, VF, VI, XX,
    }

    /// <summary>Unicode Grapheme_Cluster_Break property values (UAX #29, Unicode 16.0).</summary>
    public enum GraphemeClusterBreak : byte
    {
        Other, CR, LF, Control, Extend, ZWJ, RegionalIndicator, Prepend, SpacingMark, L, V, T, LV, LVT,
    }

    /// <summary>Unicode Indic_Conjunct_Break property values (Unicode 15.1+).</summary>
    public enum IndicConjunctBreak : byte
    {
        None, Linker, Consonant, Extend,
    }

    /// <summary>Character properties used by the segmentation algorithms, from UCD 16.0.0.</summary>
    internal readonly struct TextProps
    {
        private readonly int _v;

        public TextProps(int v) => _v = v;

        public LineBreakClass LineBreak => (LineBreakClass)(_v & 0x3F);
        public GraphemeClusterBreak Grapheme => (GraphemeClusterBreak)((_v >> 6) & 0xF);
        public IndicConjunctBreak InCB => (IndicConjunctBreak)((_v >> 10) & 0x3);
        public bool EastAsian => (_v & (1 << 12)) != 0;
        public bool ExtendedPictographic => (_v & (1 << 13)) != 0;
        public bool InitialPunctuation => (_v & (1 << 14)) != 0;
        public bool FinalPunctuation => (_v & (1 << 15)) != 0;
        public bool Unassigned => (_v & (1 << 16)) != 0;
        public bool CombiningMark => (_v & (1 << 17)) != 0;

        public static TextProps Of(int codepoint)
        {
            if ((uint)codepoint < 0x10000)
            {
                return new TextProps(BmpTable.Blocks[(BmpTable.Index[codepoint >> BmpTable.Shift] << BmpTable.Shift) | (codepoint & BmpTable.Mask)]);
            }
            return Search(codepoint);
        }

        private static TextProps Search(int codepoint)
        {
            if ((uint)codepoint > 0x10FFFF)
            {
                return new TextProps((int)LineBreakClass.XX);
            }
            int i = Array.BinarySearch(UnicodeTextData.RangeStarts, codepoint);
            if (i < 0)
            {
                i = ~i - 1;
            }
            return new TextProps(UnicodeTextData.RangeValues[i]);
        }

        /// <summary>
        /// Two-level lookup table for the Basic Multilingual Plane, built once from the range data: 64-entry blocks,
        /// identical blocks shared. Replaces a binary search per character on the hot path.
        /// </summary>
        private static class BmpTable
        {
            public const int Shift = 6;
            public const int Mask = (1 << Shift) - 1;
            public static readonly ushort[] Index;
            public static readonly int[] Blocks;

            static BmpTable()
            {
                int[] starts = UnicodeTextData.RangeStarts;
                int[] values = UnicodeTextData.RangeValues;
                Index = new ushort[0x10000 >> Shift];
                System.Collections.Generic.List<int> blocks = new System.Collections.Generic.List<int>();
                System.Collections.Generic.Dictionary<string, int> seen = new System.Collections.Generic.Dictionary<string, int>();
                int r = 0;
                int[] block = new int[1 << Shift];
                for (int b = 0; b < Index.Length; b++)
                {
                    for (int k = 0; k <= Mask; k++)
                    {
                        int cp = (b << Shift) | k;
                        while (r + 1 < starts.Length && starts[r + 1] <= cp)
                        {
                            r++;
                        }
                        block[k] = values[r];
                    }
                    string key = string.Join(",", block);
                    if (!seen.TryGetValue(key, out int at))
                    {
                        at = blocks.Count >> Shift;
                        seen[key] = at;
                        blocks.AddRange(block);
                    }
                    Index[b] = (ushort)at;
                }
                Blocks = blocks.ToArray();
            }
        }
    }

    /// <summary>Public lookups of the segmentation properties.</summary>
    public static class UnicodeTextProperties
    {
        /// <summary>The Line_Break property value as listed in LineBreak.txt (before the LB1 resolution of AI, SA, SG, XX, CJ).</summary>
        public static LineBreakClass GetLineBreakClass(int codepoint) => TextProps.Of(codepoint).LineBreak;

        public static GraphemeClusterBreak GetGraphemeClusterBreak(int codepoint) => TextProps.Of(codepoint).Grapheme;

        public static IndicConjunctBreak GetIndicConjunctBreak(int codepoint) => TextProps.Of(codepoint).InCB;

        /// <summary>East_Asian_Width is Fullwidth, Wide or Halfwidth (the UAX #14 $EastAsian set).</summary>
        public static bool IsEastAsianWide(int codepoint) => TextProps.Of(codepoint).EastAsian;

        public static bool IsExtendedPictographic(int codepoint) => TextProps.Of(codepoint).ExtendedPictographic;

        /// <summary>General_Category is Mn or Mc.</summary>
        public static bool IsCombiningMark(int codepoint) => TextProps.Of(codepoint).CombiningMark;
    }
}
