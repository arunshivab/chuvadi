using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Text.Shaping.Unicode;

/// <summary>
/// Unicode canonical normalization (NFD and NFC) per UAX #15, from the generated
/// tables. Independent of the operating system and of the .NET runtime's Unicode
/// version.
/// </summary>
internal static class Normalization
{
    // TRACE R-TS-0007
    private const int SBase = 0xAC00;
    private const int LBase = 0x1100;
    private const int VBase = 0x1161;
    private const int TBase = 0x11A7;
    private const int LCount = 19;
    private const int VCount = 21;
    private const int TCount = 28;
    private const int NCount = VCount * TCount;
    private const int SCount = LCount * NCount;

    /// <summary>Appends the full canonical decomposition of a code point.</summary>
    internal static void Decompose(int codePoint, List<int> output)
    {
        // TRACE R-TS-0007
        int s = codePoint - SBase;
        if (s >= 0 && s < SCount)
        {
            output.Add(LBase + (s / NCount));
            output.Add(VBase + ((s % NCount) / TCount));
            int t = s % TCount;
            if (t != 0)
            {
                output.Add(TBase + t);
            }

            return;
        }

        if (UnicodeData.TryGetCanonicalDecomposition(codePoint, out int first, out int second))
        {
            Decompose(first, output);
            if (second >= 0)
            {
                Decompose(second, output);
            }

            return;
        }

        output.Add(codePoint);
    }

    /// <summary>Sorts runs of non-starters by canonical combining class, keeping the order of equal classes (canonical ordering).</summary>
    internal static void CanonicalOrder(List<int> cps)
    {
        // TRACE R-TS-0008
        int i = 0;
        while (i < cps.Count)
        {
            if (UnicodeData.GetCombiningClass(cps[i]) == 0)
            {
                i++;
                continue;
            }

            int start = i;
            while (i < cps.Count && UnicodeData.GetCombiningClass(cps[i]) != 0)
            {
                i++;
            }

            // Stable insertion sort of [start, i).
            for (int a = start + 1; a < i; a++)
            {
                int v = cps[a];
                int cv = UnicodeData.GetCombiningClass(v);
                int b = a - 1;
                while (b >= start && UnicodeData.GetCombiningClass(cps[b]) > cv)
                {
                    cps[b + 1] = cps[b];
                    b--;
                }

                cps[b + 1] = v;
            }
        }
    }

    /// <summary>Applies the canonical composition algorithm to a canonically ordered, fully decomposed sequence.</summary>
    internal static void Compose(List<int> cps)
    {
        // TRACE R-TS-0009
        if (cps.Count == 0)
        {
            return;
        }

        int starterIndex = -1;
        int lastClass = -1;
        int write = 0;
        for (int read = 0; read < cps.Count; read++)
        {
            int c = cps[read];
            int cc = UnicodeData.GetCombiningClass(c);
            if (starterIndex >= 0 && (lastClass < cc || (lastClass == 0 && write - 1 == starterIndex)) && TryComposePair(cps[starterIndex], c, out int composite))
            {
                // Blocked test: c is not blocked from the starter when no character between them has class 0 or class >= cc.
                cps[starterIndex] = composite;
                continue;
            }

            if (cc == 0)
            {
                starterIndex = write;
                lastClass = 0;
            }
            else
            {
                lastClass = cc;
            }

            cps[write++] = c;
        }

        cps.RemoveRange(write, cps.Count - write);
    }

    internal static string ToNfd(string text) => Normalize(text, compose: false);

    internal static string ToNfc(string text) => Normalize(text, compose: true);

    private static string Normalize(string text, bool compose)
    {
        List<int> cps = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            Decompose(cp, cps);
        }

        CanonicalOrder(cps);
        if (compose)
        {
            Compose(cps);
        }

        StringBuilder sb = new StringBuilder(cps.Count);
        foreach (int cp in cps)
        {
            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.ToString();
    }

    private static bool TryComposePair(int a, int b, out int composite)
    {
        // TRACE R-TS-0007
        int l = a - LBase;
        if (l >= 0 && l < LCount)
        {
            int v = b - VBase;
            if (v >= 0 && v < VCount)
            {
                composite = SBase + (((l * VCount) + v) * TCount);
                return true;
            }
        }

        int s = a - SBase;
        if (s >= 0 && s < SCount && s % TCount == 0)
        {
            int t = b - TBase;
            if (t > 0 && t < TCount)
            {
                composite = a + t;
                return true;
            }
        }

        return UnicodeData.TryCompose(a, b, out composite);
    }
}
