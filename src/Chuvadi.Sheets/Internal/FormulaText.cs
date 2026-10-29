using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Lexical helpers for A1-style formula text: shifting the relative references of a shared
/// formula to a child cell, and spotting references the Workbook model cannot keep
/// (other workbooks, structured table references). Not a parser: it recognises string
/// literals, quoted sheet names, bracketed parts, numbers, function names and A1 references,
/// which is all that reference shifting needs.
/// </summary>
internal static class FormulaText
{
    private const int MaxRow = 1048576;
    private const int MaxCol = 16384;

    /// <summary>
    /// Returns <paramref name="formula"/> with every relative row/column reference moved by
    /// (<paramref name="dRow"/>, <paramref name="dCol"/>), as Excel does when it fills a shared
    /// formula. Absolute parts ($A, $1) stay. A reference moved off the sheet becomes #REF!.
    /// </summary>
    public static string Shift(string formula, int dRow, int dCol)
    {
        if ((dRow == 0 && dCol == 0) || string.IsNullOrEmpty(formula)) return formula;
        var sb = new StringBuilder(formula.Length + 8);
        int i = 0, n = formula.Length;
        while (i < n)
        {
            char c = formula[i];
            if (c == '"')
            {
                int j = SkipString(formula, i);
                sb.Append(formula, i, j - i); i = j; continue;
            }
            if (c == '\'')
            {
                int j = SkipQuotedName(formula, i);
                sb.Append(formula, i, j - i); i = j; continue;
            }
            if (c == '[')
            {
                int j = SkipBrackets(formula, i);
                sb.Append(formula, i, j - i); i = j; continue;
            }
            if (c == '#')
            {
                // Error literal (#REF!, #N/A ...) or spill operator after a reference.
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(formula[j]) || formula[j] is '/' or '!' or '?' or '_')) j++;
                sb.Append(formula, i, j - i); i = j; continue;
            }
            if (IsTokenStart(c) && (i == 0 || !IsTokenChar(formula[i - 1])))
            {
                int j = i;
                while (j < n && IsTokenChar(formula[j])) j++;
                var token = formula.AsSpan(i, j - i);

                // Function name, or a sheet name followed by '!': not a reference.
                if (j < n && (formula[j] == '(' || formula[j] == '!')) { sb.Append(token); i = j; continue; }

                // Column range "A:C" / "$A:$C" or row range "1:5" / "$1:$5".
                if (j < n && formula[j] == ':' && TryRangeOperand(formula, j + 1, out int k, out var other))
                {
                    var left = token;
                    if (IsColumnOnly(left, out _) && IsColumnOnly(other, out _))
                    {
                        sb.Append(ShiftColumnPart(left, dCol)).Append(':').Append(ShiftColumnPart(other, dCol));
                        i = k; continue;
                    }
                    if (IsRowOnly(left, out _) && IsRowOnly(other, out _))
                    {
                        sb.Append(ShiftRowPart(left, dRow)).Append(':').Append(ShiftRowPart(other, dRow));
                        i = k; continue;
                    }
                }

                if (TryCellRef(token, out bool colAbs, out int col, out bool rowAbs, out int row))
                {
                    int nc = colAbs ? col : col + dCol;
                    int nr = rowAbs ? row : row + dRow;
                    if (nc < 1 || nc > MaxCol || nr < 1 || nr > MaxRow) sb.Append("#REF!");
                    else
                    {
                        if (colAbs) sb.Append('$');
                        sb.Append(CellAddress.ColumnLetters(nc));
                        if (rowAbs) sb.Append('$');
                        sb.Append(nr.ToString(CultureInfo.InvariantCulture));
                    }
                    i = j; continue;
                }

                sb.Append(token);
                i = j; continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(formula[i + 1])))
            {
                // Number literal (with exponent), or a row range "1:5".
                int j = i;
                while (j < n && (char.IsDigit(formula[j]) || formula[j] == '.')) j++;
                if (j < n && formula[j] == ':' && TryRangeOperand(formula, j + 1, out int k, out var other)
                    && IsRowOnly(formula.AsSpan(i, j - i), out _) && IsRowOnly(other, out _))
                {
                    sb.Append(ShiftRowPart(formula.AsSpan(i, j - i), dRow)).Append(':').Append(ShiftRowPart(other, dRow));
                    i = k; continue;
                }
                if (j < n && (formula[j] is 'e' or 'E') && j + 1 < n
                    && (char.IsDigit(formula[j + 1]) || (formula[j + 1] is '+' or '-' && j + 2 < n && char.IsDigit(formula[j + 2]))))
                {
                    j += 2;
                    while (j < n && char.IsDigit(formula[j])) j++;
                }
                sb.Append(formula, i, j - i); i = j; continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// True if the formula refers to another workbook ([1]Sheet!A1) or uses a structured table
    /// reference (Table1[Col]). Brackets inside string literals do not count.
    /// </summary>
    public static bool HasBracketReference(string? formula)
    {
        if (string.IsNullOrEmpty(formula)) return false;
        int i = 0, n = formula.Length;
        while (i < n)
        {
            char c = formula[i];
            if (c == '"') { i = SkipString(formula, i); continue; }
            if (c == '\'')
            {
                // A quoted external name looks like '[1]Sheet'!A1 or 'C:\x\[Book.xlsx]Sheet'!A1.
                int j = SkipQuotedName(formula, i);
                if (formula.AsSpan(i, j - i).IndexOf('[') >= 0) return true;
                i = j; continue;
            }
            if (c == '[') return true;
            i++;
        }
        return false;
    }

    /// <summary>
    /// True if the formula uses one of the given defined names (case-insensitive), outside
    /// string literals, quoted sheet names and bracketed parts.
    /// </summary>
    public static bool UsesAnyName(string? formula, ISet<string> names)
    {
        if (string.IsNullOrEmpty(formula) || names.Count == 0) return false;
        int i = 0, n = formula.Length;
        while (i < n)
        {
            char c = formula[i];
            if (c == '"') { i = SkipString(formula, i); continue; }
            if (c == '\'') { i = SkipQuotedName(formula, i); continue; }
            if (c == '[') { i = SkipBrackets(formula, i); continue; }
            if (IsTokenStart(c) && (i == 0 || !IsTokenChar(formula[i - 1])))
            {
                int j = i;
                while (j < n && IsTokenChar(formula[j])) j++;
                if (!(j < n && formula[j] is '(' or '!') && names.Contains(formula.Substring(i, j - i))) return true;
                i = j; continue;
            }
            i++;
        }
        return false;
    }

    // ---- Lexing helpers ---------------------------------------------------------------

    private static bool IsTokenStart(char c) => char.IsLetter(c) || c == '_' || c == '$' || c == '\\';
    private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '$' or '\\' or '?';

    private static int SkipString(string s, int i)
    {
        int j = i + 1;
        while (j < s.Length)
        {
            if (s[j] == '"') { if (j + 1 < s.Length && s[j + 1] == '"') { j += 2; continue; } return j + 1; }
            j++;
        }
        return j;
    }

    private static int SkipQuotedName(string s, int i)
    {
        int j = i + 1;
        while (j < s.Length)
        {
            if (s[j] == '\'') { if (j + 1 < s.Length && s[j + 1] == '\'') { j += 2; continue; } return j + 1; }
            j++;
        }
        return j;
    }

    private static int SkipBrackets(string s, int i)
    {
        int depth = 0, j = i;
        while (j < s.Length)
        {
            char c = s[j];
            if (c == '\'' && depth > 0 && j + 1 < s.Length) { j += 2; continue; }   // '[ escapes inside table refs
            if (c == '[') depth++;
            else if (c == ']') { depth--; if (depth == 0) return j + 1; }
            j++;
        }
        return j;
    }

    /// <summary>Reads the operand after ':' (letters, digits, '$'); k = index after it.</summary>
    private static bool TryRangeOperand(string s, int start, out int k, out ReadOnlySpan<char> operand)
    {
        k = start;
        while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '$')) k++;
        operand = s.AsSpan(start, k - start);
        return k > start;
    }

    private static bool IsColumnOnly(ReadOnlySpan<char> t, out int col)
    {
        col = 0;
        int i = 0;
        if (i < t.Length && t[i] == '$') i++;
        if (i == t.Length || t.Length - i > 3) return false;
        for (; i < t.Length; i++)
        {
            char ch = char.ToUpperInvariant(t[i]);
            if (ch < 'A' || ch > 'Z') return false;
            col = col * 26 + (ch - 'A' + 1);
        }
        return col <= MaxCol;
    }

    private static bool IsRowOnly(ReadOnlySpan<char> t, out int row)
    {
        row = 0;
        int i = 0;
        if (i < t.Length && t[i] == '$') i++;
        if (i == t.Length || t.Length - i > 7) return false;
        for (; i < t.Length; i++)
        {
            if (!char.IsDigit(t[i])) return false;
            row = row * 10 + (t[i] - '0');
        }
        return row >= 1 && row <= MaxRow;
    }

    private static string ShiftColumnPart(ReadOnlySpan<char> t, int d)
    {
        IsColumnOnly(t, out int col);
        if (t[0] == '$') return t.ToString();
        int nc = col + d;
        return nc < 1 || nc > MaxCol ? "#REF!" : CellAddress.ColumnLetters(nc);
    }

    private static string ShiftRowPart(ReadOnlySpan<char> t, int d)
    {
        IsRowOnly(t, out int row);
        if (t[0] == '$') return t.ToString();
        int nr = row + d;
        return nr < 1 || nr > MaxRow ? "#REF!" : nr.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Matches $?COL$?ROW with COL ≤ XFD and ROW ≤ 1048576.</summary>
    internal static bool TryCellRef(ReadOnlySpan<char> t, out bool colAbs, out int col, out bool rowAbs, out int row)
    {
        colAbs = rowAbs = false; col = row = 0;
        int i = 0;
        if (i < t.Length && t[i] == '$') { colAbs = true; i++; }
        int letters = 0;
        while (i < t.Length && char.IsLetter(t[i]))
        {
            char ch = char.ToUpperInvariant(t[i]);
            if (ch < 'A' || ch > 'Z') return false;
            col = col * 26 + (ch - 'A' + 1);
            i++; letters++;
            if (letters > 3) return false;
        }
        if (letters == 0 || col > MaxCol) return false;
        if (i < t.Length && t[i] == '$') { rowAbs = true; i++; }
        int digits = 0;
        while (i < t.Length && char.IsDigit(t[i]))
        {
            row = row * 10 + (t[i] - '0');
            i++; digits++;
            if (digits > 7) return false;
        }
        return digits > 0 && i == t.Length && row >= 1 && row <= MaxRow;
    }
}
