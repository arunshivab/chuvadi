using System;

namespace Chuvadi.Layout.Text
{
    /// <summary>What may happen at a position between two characters.</summary>
    public enum LineBreakOpportunity : byte
    {
        /// <summary>A line must not break here.</summary>
        NoBreak,

        /// <summary>A line may break here.</summary>
        Allowed,

        /// <summary>A line must break here (after a hard line break, and at end of text).</summary>
        Mandatory,
    }

    /// <summary>
    /// The Unicode Line Breaking Algorithm (UAX #14, revision 53, Unicode 16.0): the default, untailored
    /// rules LB1–LB31, including the Brahmic orthographic-syllable rule LB28a. Conformant with the Unicode
    /// LineBreakTest.txt. LB1 resolves AI, SG and XX to AL, SA to CM (Mn/Mc) or AL, and CJ to NS.
    /// </summary>
    public static class LineBreaker
    {
        private const int DottedCircle = 0x25CC;
        private const int Hyphen = 0x2010;

        /// <summary>
        /// Opportunities for every UTF-16 position: element <c>i</c> describes the boundary before
        /// <c>text[i]</c>; element <c>text.Length</c> is the end of text. Positions inside a surrogate
        /// pair are <see cref="LineBreakOpportunity.NoBreak"/>.
        /// </summary>
        public static LineBreakOpportunity[] GetOpportunities(ReadOnlySpan<char> text)
        {
            LineBreakOpportunity[] result = new LineBreakOpportunity[text.Length + 1];
            GetOpportunities(text, result);
            return result;
        }

        /// <summary>Fills <paramref name="result"/> (length must be <c>text.Length + 1</c>).</summary>
        public static void GetOpportunities(ReadOnlySpan<char> text, Span<LineBreakOpportunity> result)
        {
            if (result.Length != text.Length + 1)
            {
                throw new ArgumentException("result must have text.Length + 1 elements.", nameof(result));
            }
            result.Clear();
            if (text.Length == 0)
            {
                result[0] = LineBreakOpportunity.Mandatory;
                return;
            }

            // Decode to code points.
            int n = 0;
            int[] cps = new int[text.Length];
            int[] offsets = new int[text.Length + 1];
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                offsets[n] = i;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                cps[n++] = cp;
            }
            offsets[n] = text.Length;

            LineBreakOpportunity[] cpResult = new LineBreakOpportunity[n + 1];
            Compute(cps.AsSpan(0, n), cpResult);
            for (int k = 0; k <= n; k++)
            {
                result[offsets[k]] = cpResult[k];
            }
        }

        /// <summary>Opportunities over code points: element k is the boundary before code point k.</summary>
        internal static void Compute(ReadOnlySpan<int> cps, Span<LineBreakOpportunity> result)
        {
            int n = cps.Length;
            TextProps[] props = new TextProps[n];
            LineBreakClass[] raw = new LineBreakClass[n];
            for (int i = 0; i < n; i++)
            {
                props[i] = TextProps.Of(cps[i]);
                raw[i] = Resolve(props[i]);
            }

            // LB9/LB10: absorb CM/ZWJ into the preceding base; the unit takes the base's class and properties.
            bool[] absorbed = new bool[n];
            LineBreakClass[] cls = new LineBreakClass[n];
            int[] baseOf = new int[n];
            for (int i = 0; i < n; i++)
            {
                LineBreakClass c = raw[i];
                baseOf[i] = i;
                if ((c == LineBreakClass.CM || c == LineBreakClass.ZWJ) && i > 0)
                {
                    LineBreakClass prev = cls[i - 1];
                    if (prev is not (LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                        or LineBreakClass.SP or LineBreakClass.ZW))
                    {
                        absorbed[i] = true;
                        cls[i] = prev;
                        baseOf[i] = baseOf[i - 1];
                        continue;
                    }
                }
                cls[i] = c is LineBreakClass.CM or LineBreakClass.ZWJ ? LineBreakClass.AL : c;
            }

            Ctx ctx = new Ctx(cps, props, raw, cls, absorbed, baseOf);
            result[0] = LineBreakOpportunity.NoBreak;
            for (int i = 1; i < n; i++)
            {
                // Fast paths for the commonest pairs in running text; everything else goes through the full rules.
                LineBreakClass rawCur = raw[i];
                LineBreakClass prevCls = cls[i - 1];
                if (rawCur == LineBreakClass.SP && prevCls is not (LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL))
                {
                    result[i] = LineBreakOpportunity.NoBreak; // LB7
                    continue;
                }
                if (rawCur == LineBreakClass.AL && prevCls == LineBreakClass.AL && raw[i - 1] is LineBreakClass.AL or LineBreakClass.CM)
                {
                    result[i] = LineBreakOpportunity.NoBreak; // LB28
                    continue;
                }
                if (rawCur == LineBreakClass.AL && raw[i - 1] == LineBreakClass.SP)
                {
                    int j = i - 1;
                    while (j >= 0 && raw[j] == LineBreakClass.SP)
                    {
                        j--;
                    }
                    if (j < 0 || cls[j] is not (LineBreakClass.OP or LineBreakClass.QU))
                    {
                        result[i] = LineBreakOpportunity.Allowed; // LB18 (no LB14/LB15 context before the spaces)
                        continue;
                    }
                }
                result[i] = Decide(ref ctx, i);
            }
            result[n] = LineBreakOpportunity.Mandatory;
        }

        private static LineBreakClass Resolve(TextProps p)
        {
            LineBreakClass c = p.LineBreak;
            return c switch
            {
                LineBreakClass.AI or LineBreakClass.SG or LineBreakClass.XX => LineBreakClass.AL,
                LineBreakClass.SA => p.CombiningMark ? LineBreakClass.CM : LineBreakClass.AL,
                LineBreakClass.CJ => LineBreakClass.NS,
                _ => c,
            };
        }

        private readonly ref struct Ctx
        {
            public readonly ReadOnlySpan<int> Cps;
            public readonly TextProps[] Props;
            public readonly LineBreakClass[] Raw;
            public readonly LineBreakClass[] Cls;
            public readonly bool[] Absorbed;
            public readonly int[] BaseOf;

            public Ctx(ReadOnlySpan<int> cps, TextProps[] props, LineBreakClass[] raw, LineBreakClass[] cls, bool[] absorbed, int[] baseOf)
            {
                Cps = cps;
                Props = props;
                Raw = raw;
                Cls = cls;
                Absorbed = absorbed;
                BaseOf = baseOf;
            }

            public int N => Cls.Length;

            /// <summary>Index of the start of the unit containing position i.</summary>
            public int UnitStart(int i)
            {
                while (i > 0 && Absorbed[i])
                {
                    i--;
                }
                return i;
            }

            /// <summary>Start index of the unit before the unit that ends at index end (inclusive); -1 at sot.</summary>
            public int PrevUnit(int unitStart) => unitStart <= 0 ? -1 : UnitStart(unitStart - 1);

            /// <summary>Start index of the unit after the unit starting at i; N at eot.</summary>
            public int NextUnit(int i)
            {
                int j = i + 1;
                while (j < N && Absorbed[j])
                {
                    j++;
                }
                return j;
            }

            public TextProps BaseProps(int unit) => Props[BaseOf[unit]];
            public int BaseCp(int unit) => Cps[BaseOf[unit]];
        }

        private static bool IsAkLike(ref Ctx ctx, int unit)
        {
            LineBreakClass c = ctx.Cls[unit];
            return c == LineBreakClass.AK || c == LineBreakClass.AS || ctx.BaseCp(unit) == DottedCircle;
        }

        private static LineBreakOpportunity Decide(ref Ctx ctx, int i)
        {
            LineBreakClass rawPrev = ctx.Raw[i - 1];
            LineBreakClass b = ctx.Cls[i];
            int aUnit = ctx.UnitStart(i - 1);
            LineBreakClass a = ctx.Cls[i - 1];

            // LB4, LB5
            if (a == LineBreakClass.BK && !ctx.Absorbed[i - 1])
            {
                return LineBreakOpportunity.Mandatory;
            }
            if (rawPrev == LineBreakClass.CR && ctx.Raw[i] == LineBreakClass.LF)
            {
                return LineBreakOpportunity.NoBreak;
            }
            if (!ctx.Absorbed[i - 1] && (a == LineBreakClass.CR || a == LineBreakClass.LF || a == LineBreakClass.NL))
            {
                return LineBreakOpportunity.Mandatory;
            }
            LineBreakClass rawB = ctx.Raw[i];
            // LB6
            if (rawB is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB7
            if (rawB is LineBreakClass.SP or LineBreakClass.ZW)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB8: ZW SP* ÷
            {
                int u = aUnit;
                while (u >= 0 && ctx.Cls[u] == LineBreakClass.SP)
                {
                    u = ctx.PrevUnit(u);
                }
                if (u >= 0 && ctx.Cls[u] == LineBreakClass.ZW)
                {
                    return LineBreakOpportunity.Allowed;
                }
            }
            // LB8a
            if (rawPrev == LineBreakClass.ZWJ)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB9
            if (ctx.Absorbed[i])
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB11
            if (b == LineBreakClass.WJ || a == LineBreakClass.WJ)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB12
            if (a == LineBreakClass.GL)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB12a
            if (b == LineBreakClass.GL && a is not (LineBreakClass.SP or LineBreakClass.BA or LineBreakClass.HY))
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB13
            if (b is LineBreakClass.CL or LineBreakClass.CP or LineBreakClass.EX or LineBreakClass.SY)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // Unit before any spaces that precede the boundary.
            int beforeSpaces = aUnit;
            while (beforeSpaces >= 0 && ctx.Cls[beforeSpaces] == LineBreakClass.SP)
            {
                beforeSpaces = ctx.PrevUnit(beforeSpaces);
            }
            LineBreakClass bs = beforeSpaces >= 0 ? ctx.Cls[beforeSpaces] : LineBreakClass.XX;
            // LB14
            if (beforeSpaces >= 0 && bs == LineBreakClass.OP)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB15a
            if (beforeSpaces >= 0 && bs == LineBreakClass.QU && ctx.BaseProps(beforeSpaces).InitialPunctuation)
            {
                int p = ctx.PrevUnit(beforeSpaces);
                if (p < 0 || ctx.Cls[p] is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                    or LineBreakClass.OP or LineBreakClass.QU or LineBreakClass.GL or LineBreakClass.SP or LineBreakClass.ZW)
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            int next = ctx.NextUnit(i);
            LineBreakClass nb = next < ctx.N ? ctx.Cls[next] : LineBreakClass.XX;
            // LB15b
            if (b == LineBreakClass.QU && ctx.BaseProps(i).FinalPunctuation)
            {
                if (next >= ctx.N || nb is LineBreakClass.SP or LineBreakClass.GL or LineBreakClass.WJ or LineBreakClass.CL
                    or LineBreakClass.QU or LineBreakClass.CP or LineBreakClass.EX or LineBreakClass.IS or LineBreakClass.SY
                    or LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL or LineBreakClass.ZW)
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            // LB15c
            if (a == LineBreakClass.SP && b == LineBreakClass.IS && next < ctx.N && nb == LineBreakClass.NU)
            {
                return LineBreakOpportunity.Allowed;
            }
            // LB15d
            if (b == LineBreakClass.IS)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB16
            if (b == LineBreakClass.NS && beforeSpaces >= 0 && bs is LineBreakClass.CL or LineBreakClass.CP)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB17
            if (b == LineBreakClass.B2 && beforeSpaces >= 0 && bs == LineBreakClass.B2)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB18
            if (a == LineBreakClass.SP)
            {
                return LineBreakOpportunity.Allowed;
            }
            // LB19
            if (b == LineBreakClass.QU && !ctx.BaseProps(i).InitialPunctuation)
            {
                return LineBreakOpportunity.NoBreak;
            }
            if (a == LineBreakClass.QU && !ctx.BaseProps(aUnit).FinalPunctuation)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB19a
            bool aEa = ctx.BaseProps(aUnit).EastAsian;
            bool bEa = ctx.BaseProps(i).EastAsian;
            if (b == LineBreakClass.QU)
            {
                if (!aEa)
                {
                    return LineBreakOpportunity.NoBreak;
                }
                if (next >= ctx.N || !ctx.BaseProps(next).EastAsian)
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            if (a == LineBreakClass.QU)
            {
                if (!bEa)
                {
                    return LineBreakOpportunity.NoBreak;
                }
                int p = ctx.PrevUnit(aUnit);
                if (p < 0 || !ctx.BaseProps(p).EastAsian)
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            // LB20
            if (b == LineBreakClass.CB || a == LineBreakClass.CB)
            {
                return LineBreakOpportunity.Allowed;
            }
            // LB20a
            if ((a == LineBreakClass.HY || ctx.BaseCp(aUnit) == Hyphen) && b == LineBreakClass.AL)
            {
                int p = ctx.PrevUnit(aUnit);
                if (p < 0 || ctx.Cls[p] is LineBreakClass.BK or LineBreakClass.CR or LineBreakClass.LF or LineBreakClass.NL
                    or LineBreakClass.SP or LineBreakClass.ZW or LineBreakClass.CB or LineBreakClass.GL)
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            // LB21
            if (b is LineBreakClass.BA or LineBreakClass.HY or LineBreakClass.NS || a == LineBreakClass.BB)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB21a
            if ((a == LineBreakClass.HY || (a == LineBreakClass.BA && !aEa)) && b != LineBreakClass.HL)
            {
                int p = ctx.PrevUnit(aUnit);
                if (p >= 0 && ctx.Cls[p] == LineBreakClass.HL)
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            // LB21b
            if (a == LineBreakClass.SY && b == LineBreakClass.HL)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB22
            if (b == LineBreakClass.IN)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB23
            if ((a is LineBreakClass.AL or LineBreakClass.HL && b == LineBreakClass.NU)
                || (a == LineBreakClass.NU && b is LineBreakClass.AL or LineBreakClass.HL))
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB23a
            if ((a == LineBreakClass.PR && b is LineBreakClass.ID or LineBreakClass.EB or LineBreakClass.EM)
                || (a is LineBreakClass.ID or LineBreakClass.EB or LineBreakClass.EM && b == LineBreakClass.PO))
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB24
            if ((a is LineBreakClass.PR or LineBreakClass.PO && b is LineBreakClass.AL or LineBreakClass.HL)
                || (a is LineBreakClass.AL or LineBreakClass.HL && b is LineBreakClass.PR or LineBreakClass.PO))
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB25
            if (Lb25(ref ctx, aUnit, a, b, next, nb))
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB26
            if ((a == LineBreakClass.JL && b is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.H2 or LineBreakClass.H3)
                || (a is LineBreakClass.JV or LineBreakClass.H2 && b is LineBreakClass.JV or LineBreakClass.JT)
                || (a is LineBreakClass.JT or LineBreakClass.H3 && b == LineBreakClass.JT))
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB27
            bool aKorean = a is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.JT or LineBreakClass.H2 or LineBreakClass.H3;
            bool bKorean = b is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.JT or LineBreakClass.H2 or LineBreakClass.H3;
            if ((aKorean && b == LineBreakClass.PO) || (a == LineBreakClass.PR && bKorean))
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB28
            if (a is LineBreakClass.AL or LineBreakClass.HL && b is LineBreakClass.AL or LineBreakClass.HL)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB28a
            bool aAk = IsAkLike(ref ctx, aUnit);
            bool bAk = IsAkLike(ref ctx, i);
            if (a == LineBreakClass.AP && (b == LineBreakClass.AK || b == LineBreakClass.AS || ctx.BaseCp(i) == DottedCircle))
            {
                return LineBreakOpportunity.NoBreak;
            }
            if (aAk && b is LineBreakClass.VF or LineBreakClass.VI)
            {
                return LineBreakOpportunity.NoBreak;
            }
            if (a == LineBreakClass.VI && (b == LineBreakClass.AK || ctx.BaseCp(i) == DottedCircle))
            {
                int p = ctx.PrevUnit(aUnit);
                if (p >= 0 && IsAkLike(ref ctx, p))
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            if (aAk && bAk && next < ctx.N && nb == LineBreakClass.VF)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB29
            if (a == LineBreakClass.IS && b is LineBreakClass.AL or LineBreakClass.HL)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB30
            if (a is LineBreakClass.AL or LineBreakClass.HL or LineBreakClass.NU && b == LineBreakClass.OP && !bEa)
            {
                return LineBreakOpportunity.NoBreak;
            }
            if (a == LineBreakClass.CP && !aEa && b is LineBreakClass.AL or LineBreakClass.HL or LineBreakClass.NU)
            {
                return LineBreakOpportunity.NoBreak;
            }
            // LB30a
            if (a == LineBreakClass.RI && b == LineBreakClass.RI)
            {
                int count = 0;
                int u = aUnit;
                while (u >= 0 && ctx.Cls[u] == LineBreakClass.RI)
                {
                    count++;
                    u = ctx.PrevUnit(u);
                }
                if (count % 2 == 1)
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            // LB30b
            if (b == LineBreakClass.EM)
            {
                TextProps ap = ctx.BaseProps(aUnit);
                if (a == LineBreakClass.EB || (ap.ExtendedPictographic && ap.Unassigned))
                {
                    return LineBreakOpportunity.NoBreak;
                }
            }
            // LB31
            return LineBreakOpportunity.Allowed;
        }

        private static bool Lb25(ref Ctx ctx, int aUnit, LineBreakClass a, LineBreakClass b, int next, LineBreakClass nb)
        {
            if (b is LineBreakClass.PO or LineBreakClass.PR)
            {
                int u = aUnit;
                if (a is LineBreakClass.CL or LineBreakClass.CP)
                {
                    u = ctx.PrevUnit(aUnit);
                }
                while (u >= 0 && ctx.Cls[u] is LineBreakClass.SY or LineBreakClass.IS)
                {
                    u = ctx.PrevUnit(u);
                }
                if (u >= 0 && ctx.Cls[u] == LineBreakClass.NU)
                {
                    return true;
                }
            }
            if (a is LineBreakClass.PO or LineBreakClass.PR)
            {
                if (b == LineBreakClass.NU)
                {
                    return true;
                }
                if (b == LineBreakClass.OP && next < ctx.N)
                {
                    if (nb == LineBreakClass.NU)
                    {
                        return true;
                    }
                    if (nb == LineBreakClass.IS)
                    {
                        int nn = ctx.NextUnit(next);
                        if (nn < ctx.N && ctx.Cls[nn] == LineBreakClass.NU)
                        {
                            return true;
                        }
                    }
                }
            }
            if (a is LineBreakClass.HY or LineBreakClass.IS && b == LineBreakClass.NU)
            {
                return true;
            }
            if (b == LineBreakClass.NU)
            {
                int u = aUnit;
                while (u >= 0 && ctx.Cls[u] is LineBreakClass.SY or LineBreakClass.IS)
                {
                    u = ctx.PrevUnit(u);
                }
                if (u >= 0 && ctx.Cls[u] == LineBreakClass.NU)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
