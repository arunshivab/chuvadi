using System;

namespace Chuvadi.Text.Shaping.Unicode;

/// <summary>General_Category values, in the order the generator assigns them (Cn = 0).</summary>
internal enum GeneralCategory : byte
{
    // TRACE R-TS-0001
    Unassigned, UppercaseLetter, LowercaseLetter, TitlecaseLetter, ModifierLetter, OtherLetter,
    NonspacingMark, SpacingMark, EnclosingMark, DecimalNumber, LetterNumber, OtherNumber,
    ConnectorPunctuation, DashPunctuation, OpenPunctuation, ClosePunctuation, InitialPunctuation,
    FinalPunctuation, OtherPunctuation, MathSymbol, CurrencySymbol, ModifierSymbol, OtherSymbol,
    SpaceSeparator, LineSeparator, ParagraphSeparator, Control, Format, Surrogate, PrivateUse,
}

/// <summary>Lookups into the generated Unicode tables (see UnicodeData.g.cs).</summary>
internal static partial class UnicodeData
{
    private const int MidSize = 1 << (TopShift - BlockShift);
    private const int BlockMask = (1 << BlockShift) - 1;

    // TRACE R-TS-0006
    internal static uint Record(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
        {
            return 0;
        }

        int mid = PropertyStage1[codePoint >> TopShift];
        int block = PropertyStage2[(mid * MidSize) + ((codePoint >> BlockShift) & (MidSize - 1))];
        return PropertyRecords[PropertyStage3[(block << BlockShift) + (codePoint & BlockMask)]];
    }

    internal static GeneralCategory GetGeneralCategory(int codePoint) => (GeneralCategory)(Record(codePoint) & 31);

    internal static int GetCombiningClass(int codePoint) => (int)((Record(codePoint) >> 5) & 255);

    internal static IndicSyllabicCategory GetIndicSyllabicCategory(int codePoint) => (IndicSyllabicCategory)((Record(codePoint) >> 13) & 63);

    internal static IndicPositionalCategory GetIndicPositionalCategory(int codePoint) => (IndicPositionalCategory)((Record(codePoint) >> 19) & 31);

    internal static bool IsDefaultIgnorable(int codePoint) => ((Record(codePoint) >> 24) & 1) != 0;

    internal static bool IsMark(int codePoint)
    {
        GeneralCategory gc = GetGeneralCategory(codePoint);
        return gc is GeneralCategory.NonspacingMark or GeneralCategory.SpacingMark or GeneralCategory.EnclosingMark;
    }

    /// <summary>Gets the ISO 15924 code of the code point's Script property ("Zzzz" when unassigned).</summary>
    internal static string GetScriptCode(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
        {
            return "Zzzz";
        }

        int mid = ScriptStage1[codePoint >> TopShift];
        int block = ScriptStage2[(mid * MidSize) + ((codePoint >> BlockShift) & (MidSize - 1))];
        return ScriptCodes[ScriptStage3[(block << BlockShift) + (codePoint & BlockMask)]];
    }

    /// <summary>Gets the canonical (not compatibility) decomposition mapping of a code point, one level deep.</summary>
    /// <returns>False when the code point has no canonical mapping. <paramref name="second"/> is -1 for a singleton.</returns>
    internal static bool TryGetCanonicalDecomposition(int codePoint, out int first, out int second)
    {
        // TRACE R-TS-0002
        int i = DecompositionCodePoints.BinarySearch(codePoint);
        if (i < 0)
        {
            first = 0;
            second = -1;
            return false;
        }

        first = DecompositionFirst[i];
        second = DecompositionSecond[i];
        return true;
    }

    /// <summary>Gets the primary composite of a pair, excluding Full_Composition_Exclusion characters.</summary>
    internal static bool TryCompose(int first, int second, out int composite)
    {
        // TRACE R-TS-0002
        if ((uint)first > 0x10FFFF || (uint)second > 0x10FFFF)
        {
            composite = 0;
            return false;
        }

        int i = CompositionKeys.BinarySearch(((long)first << 21) | (uint)second);
        composite = i >= 0 ? CompositionValues[i] : 0;
        return i >= 0;
    }
}
