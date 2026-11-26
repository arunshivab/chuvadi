using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// 2.2.1 (P2, SEC-10): Excel's wildcard text patterns (<c>?</c> one character, <c>*</c> any run, <c>~</c> escapes the
/// next character), used by SEARCH and by the COUNTIF/SUMIF/AVERAGEIF criteria. Until 2.2.0 they became a backtracking
/// regex, and a pattern such as <c>*a*a*a*a*b</c> over a few hundred letters ran for minutes and could not be stopped.
/// This matcher walks the text once and keeps the set of pattern positions still possible, so a match costs at most
/// text length × pattern length steps, whatever the pattern.
/// <para>
/// The results are the regex's, character for character (the build checks them against it on random patterns):
/// case is ignored the way <c>RegexOptions.IgnoreCase | CultureInvariant</c> ignores it, which is equality of
/// <see cref="char.ToLowerInvariant"/> (checked on every BMP character); <c>?</c> and <c>*</c> do not match a line
/// break (<c>\n</c>), as <c>.</c> does not; and a whole-text match may end just before one final <c>\n</c>, as <c>$</c> may.
/// </para>
/// </summary>
internal sealed class Wildcard
{
    private const char Any = '\0', Star = '\u0001';

    // One entry per pattern step: the lower-cased literal, or a marker in _kind.
    private readonly char[] _lit;
    private readonly byte[] _kind;   // 0 literal, 1 '?', 2 '*'
    private readonly int _m;

    private Wildcard(List<(byte kind, char lit)> steps)
    {
        _m = steps.Count;
        _lit = new char[_m];
        _kind = new byte[_m];
        for (int i = 0; i < _m; i++) { _kind[i] = steps[i].kind; _lit[i] = steps[i].lit; }
    }

    /// <summary>Compiles a pattern. A <c>~</c> at the very end is a literal <c>~</c>; repeated stars count as one.</summary>
    public static Wildcard Compile(string pattern)
    {
        var steps = new List<(byte, char)>(pattern.Length);
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '~' && i + 1 < pattern.Length) { steps.Add((0, char.ToLowerInvariant(pattern[++i]))); continue; }
            if (ch == '*') { if (steps.Count == 0 || steps[^1].Item1 != 2) steps.Add((2, Star)); }
            else if (ch == '?') steps.Add((1, Any));
            else steps.Add((0, char.ToLowerInvariant(ch)));
        }
        return new Wildcard(steps);
    }

    private bool Step(int j, char lower, char c, out int next)
    {
        switch (_kind[j])
        {
            case 0: next = j + 1; return _lit[j] == lower;
            case 1: next = j + 1; return c != '\n';
            default: next = j; return c != '\n';
        }
    }

    // Adds state j and, through stars (which may match nothing), every state after them.
    private void Add(int[] start, int j, int s)
    {
        while (true)
        {
            if (start[j] <= s) return;
            start[j] = s;
            if (j < _m && _kind[j] == 2) { j++; continue; }
            return;
        }
    }

    /// <summary>True when the whole <paramref name="text"/> matches (the anchored regex <c>^…$</c>).</summary>
    public bool IsMatch(string text)
    {
        var cur = NewSet(); var nxt = NewSet();
        Add(cur, 0, 0);
        int n = text.Length;
        for (int p = 0; p < n; p++)
        {
            if (p == n - 1 && text[p] == '\n' && cur[_m] != int.MaxValue) return true;   // '$' before a final '\n'
            char c = text[p], lower = char.ToLowerInvariant(c);
            Array.Fill(nxt, int.MaxValue);
            bool any = false;
            for (int j = 0; j < _m; j++)
            {
                if (cur[j] == int.MaxValue) continue;
                if (Step(j, lower, c, out var k)) { Add(nxt, k, 0); any = true; }
            }
            if (!any) return false;
            (cur, nxt) = (nxt, cur);
        }
        return cur[_m] != int.MaxValue;
    }

    /// <summary>
    /// The index of the leftmost match at or after <paramref name="startIndex"/>, or -1 (the unanchored regex's
    /// <c>Match(text, startIndex).Index</c>).
    /// </summary>
    public int IndexOf(string text, int startIndex)
    {
        if ((uint)startIndex > (uint)text.Length) throw new ArgumentOutOfRangeException(nameof(startIndex));
        // cur[j] = the leftmost start from which state j is reachable at this position.
        var cur = NewSet(); var nxt = NewSet();
        int best = int.MaxValue;
        for (int p = startIndex; ; p++)
        {
            Add(cur, 0, p);                                  // a match may also begin here
            if (cur[_m] < best) best = cur[_m];
            if (p == text.Length) break;
            // Every live state started at or after 'best' cannot give an earlier match.
            bool live = false;
            for (int j = 0; j < _m; j++) if (cur[j] < best) { live = true; break; }
            if (!live && best != int.MaxValue) break;
            char c = text[p], lower = char.ToLowerInvariant(c);
            Array.Fill(nxt, int.MaxValue);
            for (int j = 0; j < _m; j++)
            {
                int s = cur[j];
                if (s == int.MaxValue || s >= best) continue;
                if (Step(j, lower, c, out var k)) Add(nxt, k, s);
            }
            (cur, nxt) = (nxt, cur);
        }
        return best == int.MaxValue ? -1 : best;
    }

    private int[] NewSet()
    {
        var a = new int[_m + 1];
        Array.Fill(a, int.MaxValue);
        return a;
    }
}
