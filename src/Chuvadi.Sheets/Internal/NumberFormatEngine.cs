using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Parser and renderer behind <see cref="NumberFormatter"/>. A format code is split into up
/// to four sections, each tokenized once and cached; rendering walks the tokens.
/// </summary>
internal static class NumberFormatEngine
{
    // ======================================================================================
    // Public entry points
    // ======================================================================================

    public static FormattedValue Format(object? value, string? formatCode, NumberFormatOptions o)
    {
        var fmt = Parse(formatCode);
        switch (value)
        {
            case null:
                return new FormattedValue(string.Empty, null, null, false);
            case string s:
                return FormatText(fmt, s, o);
            case bool b:
                return new FormattedValue(b ? "TRUE" : "FALSE", null, null, false);
            case DateTime dt:
                return FormatNumber(fmt, ToSerial(dt, o.Date1904), o);
            case DateTimeOffset dto:
                return FormatNumber(fmt, ToSerial(dto.DateTime, o.Date1904), o);
            case TimeSpan ts:
                return FormatNumber(fmt, ts.TotalDays, o);
            case DateOnly d:
                return FormatNumber(fmt, ToSerial(d.ToDateTime(TimeOnly.MinValue), o.Date1904), o);
            case TimeOnly t:
                return FormatNumber(fmt, t.ToTimeSpan().TotalDays, o);
            case char c:
                return FormatText(fmt, c.ToString(), o);
            case IConvertible conv when IsNumeric(value):
                return FormatNumber(fmt, conv.ToDouble(CultureInfo.InvariantCulture), o);
            default:
                return FormatText(fmt, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, o);
        }
    }

    public static double ToSerial(DateTime value, bool date1904)
    {
        if (date1904)
            return (value - new DateTime(1904, 1, 1)).TotalDays;
        // Excel's 1900 system counts the fictitious 29-Feb-1900, so dates before 1-Mar-1900
        // are one less than the OLE Automation date.
        if (value < new DateTime(1900, 3, 1))
            return (value - new DateTime(1899, 12, 31)).TotalDays;
        return value.ToOADate();
    }

    private static bool IsNumeric(object v) => v is double or float or decimal or int or long or short
        or byte or sbyte or uint or ulong or ushort;

    // ======================================================================================
    // Model
    // ======================================================================================

    private enum K
    {
        Literal, General, Text, Fill,
        D0, DHash, DQ,                // digit placeholders 0 # ?
        Point, Comma, Percent, Exponent, Slash,
        Year, Month, Day, Hour, Minute, Second, SubSecond, AmPm,
        ElapsedH, ElapsedM, ElapsedS,
        DateSep, TimeSep,
    }

    private sealed class Tok
    {
        public K Kind;
        public string Text = string.Empty;   // literal text / AM-PM spelling / exponent letter
        public int Len;                       // run length for date tokens; exponent digit count
        public bool Plus;                     // exponent "E+" (vs "E-")
        public Tok(K k) { Kind = k; }
    }

    private sealed class Section
    {
        public readonly List<Tok> Tokens = new();
        public string? Color;
        public char? Fill;
        public string? CondOp;
        public double CondValue;
        public bool IsDate;
        public bool HasGeneral;
        public bool HasText;
        public bool HasDigits;
        public bool IsEmpty;
        public bool HasFraction;

        public bool Matches(double v) => CondOp switch
        {
            "<" => v < CondValue,
            "<=" => v <= CondValue,
            ">" => v > CondValue,
            ">=" => v >= CondValue,
            "=" => v == CondValue,
            "<>" => v != CondValue,
            _ => true,
        };
    }

    private sealed class Parsed
    {
        public readonly List<Section> Sections = new();
        public Section? TextSection;
        public readonly List<Section> Numeric = new();
        public bool IsGeneral;
    }

    private static readonly ConcurrentDictionary<string, Parsed> Cache = new(StringComparer.Ordinal);
    private static readonly Parsed GeneralFormat = BuildGeneral();

    private static Parsed BuildGeneral()
    {
        var p = new Parsed { IsGeneral = true };
        var s = new Section { HasGeneral = true };
        s.Tokens.Add(new Tok(K.General));
        p.Sections.Add(s);
        p.Numeric.Add(s);
        return p;
    }

    // ======================================================================================
    // Parsing
    // ======================================================================================

    private static Parsed Parse(string? code)
    {
        if (string.IsNullOrEmpty(code) || string.Equals(code, "General", StringComparison.OrdinalIgnoreCase))
            return GeneralFormat;
        if (Cache.TryGetValue(code, out var hit)) return hit;

        var p = new Parsed();
        foreach (var raw in SplitSections(code))
            p.Sections.Add(ParseSection(raw));

        // The text section is the 4th, or a later section that consumes text (@) and has no digits.
        if (p.Sections.Count >= 4) p.TextSection = p.Sections[3];
        for (int i = 0; i < p.Sections.Count && i < 3; i++)
        {
            var s = p.Sections[i];
            if (p.TextSection is null && i > 0 && s.HasText && !s.HasDigits && !s.IsDate)
                p.TextSection = s;
            else if (!ReferenceEquals(s, p.TextSection))
                p.Numeric.Add(s);
        }
        if (p.Sections.Count == 1 && p.Sections[0].HasText && !p.Sections[0].HasDigits && !p.Sections[0].IsDate)
        {
            p.TextSection = p.Sections[0];   // "@": text format; numbers render as General
            p.Numeric.Clear();
            p.Numeric.Add(GeneralFormat.Sections[0]);
        }
        if (p.Numeric.Count == 0) p.Numeric.Add(GeneralFormat.Sections[0]);

        if (Cache.Count > 4096) Cache.Clear();   // bound memory for pathological inputs
        Cache[code] = p;
        return p;
    }

    /// <summary>Splits on ';' outside quotes, escapes and brackets.</summary>
    private static List<string> SplitSections(string code)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '"')
            {
                int end = code.IndexOf('"', i + 1);
                if (end < 0) end = code.Length - 1;
                sb.Append(code, i, end - i + 1);
                i = end;
            }
            else if (c == '\\' && i + 1 < code.Length) { sb.Append(c).Append(code[++i]); }
            else if ((c == '_' || c == '*') && i + 1 < code.Length) { sb.Append(c).Append(code[++i]); }
            else if (c == '[')
            {
                int end = code.IndexOf(']', i + 1);
                if (end < 0) end = code.Length - 1;
                sb.Append(code, i, end - i + 1);
                i = end;
            }
            else if (c == ';') { list.Add(sb.ToString()); sb.Clear(); if (list.Count == 4) break; }
            else sb.Append(c);
        }
        if (list.Count < 4) list.Add(sb.ToString());
        return list;
    }

    private static Section ParseSection(string s)
    {
        var sec = new Section { IsEmpty = s.Length == 0 };
        var t = sec.Tokens;
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            char lc = char.ToLowerInvariant(c);

            if (c == '"')
            {
                int end = s.IndexOf('"', i + 1);
                if (end < 0) end = s.Length;
                AddLiteral(t, s.Substring(i + 1, end - i - 1));
                i = end + 1;
                continue;
            }
            if (c == '\\') { if (i + 1 < s.Length) AddLiteral(t, s[i + 1].ToString()); i += 2; continue; }
            if (c == '_') { AddLiteral(t, " "); i += 2; continue; }
            if (c == '*') { if (i + 1 < s.Length) sec.Fill = s[i + 1]; t.Add(new Tok(K.Fill)); i += 2; continue; }
            if (c == '[')
            {
                int end = s.IndexOf(']', i + 1);
                if (end < 0) end = s.Length;
                ParseBracket(sec, s.Substring(i + 1, Math.Max(0, end - i - 1)));
                i = end + 1;
                continue;
            }
            if (lc == 'g' && string.Compare(s, i, "general", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
            {
                t.Add(new Tok(K.General)); sec.HasGeneral = true; i += 7; continue;
            }
            if (c == '@') { t.Add(new Tok(K.Text)); sec.HasText = true; i++; continue; }
            if (c == '0') { t.Add(new Tok(K.D0)); i++; continue; }
            if (c == '#') { t.Add(new Tok(K.DHash)); i++; continue; }
            if (c == '?') { t.Add(new Tok(K.DQ)); i++; continue; }
            if (c == '.') { t.Add(new Tok(K.Point)); i++; continue; }
            if (c == ',') { t.Add(new Tok(K.Comma)); i++; continue; }
            if (c == '%') { t.Add(new Tok(K.Percent)); i++; continue; }
            if (c == '/') { t.Add(new Tok(K.Slash)); i++; continue; }
            if (c == ':') { t.Add(new Tok(K.TimeSep)); i++; continue; }
            if (lc == 'e' && i + 1 < s.Length && (s[i + 1] == '+' || s[i + 1] == '-'))
            {
                var tok = new Tok(K.Exponent) { Text = c.ToString(), Plus = s[i + 1] == '+' };
                i += 2;
                while (i < s.Length && (s[i] == '0' || s[i] == '#' || s[i] == '?')) { tok.Len++; i++; }
                t.Add(tok);
                continue;
            }
            if (lc == 'a' && string.Compare(s, i, "am/pm", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                t.Add(new Tok(K.AmPm) { Text = s.Substring(i, 5) }); i += 5; continue;
            }
            if (lc == 'a' && string.Compare(s, i, "a/p", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
            {
                t.Add(new Tok(K.AmPm) { Text = s.Substring(i, 3) }); i += 3; continue;
            }
            if (lc is 'y' or 'm' or 'd' or 'h' or 's' or 'e')
            {
                int j = i;
                while (j < s.Length && char.ToLowerInvariant(s[j]) == lc) j++;
                int len = j - i;
                K kind = lc switch { 'y' => K.Year, 'e' => K.Year, 'm' => K.Month, 'd' => K.Day, 'h' => K.Hour, _ => K.Second };
                t.Add(new Tok(kind) { Len = lc == 'e' ? 4 : len });
                i = j;
                continue;
            }
            if (lc == 'b' && i + 1 < s.Length && (s[i + 1] == '1' || s[i + 1] == '2')) { i += 2; continue; }   // calendar switch
            AddLiteral(t, c.ToString());
            i++;
        }
        Finish(sec);
        return sec;
    }

    private static void AddLiteral(List<Tok> t, string text)
    {
        if (text.Length == 0) return;
        if (t.Count > 0 && t[^1].Kind == K.Literal) t[^1].Text += text;
        else t.Add(new Tok(K.Literal) { Text = text });
    }

    private static readonly Dictionary<string, string> ColorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000", ["blue"] = "0000FF", ["cyan"] = "00FFFF", ["green"] = "00FF00",
        ["magenta"] = "FF00FF", ["red"] = "FF0000", ["white"] = "FFFFFF", ["yellow"] = "FFFF00",
    };

    private static void ParseBracket(Section sec, string b)
    {
        if (b.Length == 0) return;
        if (ColorNames.TryGetValue(b, out var hex)) { sec.Color = hex; return; }
        if (b.StartsWith("color", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(b.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n >= 1 && n <= 56)
        {
            sec.Color = CellStyleTable.DefaultIndexedColors[n + 7];
            return;
        }
        if (b[0] is '<' or '>' or '=')
        {
            int k = 1;
            if (k < b.Length && (b[k] == '=' || (b[0] == '<' && b[k] == '>'))) k++;
            if (double.TryParse(b.AsSpan(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                sec.CondOp = b.Substring(0, k);
                sec.CondValue = v;
            }
            return;
        }
        char l = char.ToLowerInvariant(b[0]);
        if ((l == 'h' || l == 'm' || l == 's') && AllSame(b, l))
        {
            sec.Tokens.Add(new Tok(l == 'h' ? K.ElapsedH : l == 'm' ? K.ElapsedM : K.ElapsedS) { Len = b.Length });
            return;
        }
        if (b[0] == '$')
        {
            // [$€-409]: currency symbol before the '-'; [$-409]: locale only.
            int dash = b.IndexOf('-', 1);
            var sym = dash < 0 ? b.Substring(1) : b.Substring(1, dash - 1);
            AddLiteral(sec.Tokens, sym);
        }
        // Anything else ([DBNum1], [natNum], ...) is ignored.
    }

    private static bool AllSame(string s, char lower)
    {
        foreach (var ch in s) if (char.ToLowerInvariant(ch) != lower) return false;
        return true;
    }

    /// <summary>Classifies the section and resolves context-dependent tokens.</summary>
    private static void Finish(Section sec)
    {
        var t = sec.Tokens;
        foreach (var tok in t)
        {
            if (tok.Kind is K.Year or K.Month or K.Day or K.Hour or K.Second or K.AmPm
                or K.ElapsedH or K.ElapsedM or K.ElapsedS)
                sec.IsDate = true;
            if (tok.Kind is K.D0 or K.DHash or K.DQ) sec.HasDigits = true;
        }

        if (sec.IsDate)
        {
            // m/mm after an hour or before a second means minutes.
            for (int i = 0; i < t.Count; i++)
            {
                if (t[i].Kind != K.Month) continue;
                var prev = PrevDateToken(t, i);
                var next = NextDateToken(t, i);
                if (prev is K.Hour or K.ElapsedH || next is K.Second or K.ElapsedS)
                    t[i].Kind = K.Minute;
            }
            // ".0", ".00", ".000" after seconds: fractional seconds. Other number tokens are literals.
            bool seenSecond = false;
            for (int i = 0; i < t.Count; i++)
            {
                var k = t[i].Kind;
                if (k is K.Second or K.ElapsedS) seenSecond = true;
                if (k == K.Point && seenSecond && i + 1 < t.Count && t[i + 1].Kind == K.D0)
                {
                    int j = i + 1;
                    while (j < t.Count && t[j].Kind == K.D0) j++;
                    t[i] = new Tok(K.SubSecond) { Len = j - i - 1 };
                    t.RemoveRange(i + 1, j - i - 1);
                    continue;
                }
                switch (k)
                {
                    case K.Slash: t[i] = new Tok(K.DateSep); break;
                    case K.D0: t[i] = new Tok(K.Literal) { Text = "0" }; break;
                    case K.DHash: t[i] = new Tok(K.Literal) { Text = "#" }; break;
                    case K.DQ: t[i] = new Tok(K.Literal) { Text = "?" }; break;
                    case K.Point: t[i] = new Tok(K.Literal) { Text = "." }; break;
                    case K.Comma: t[i] = new Tok(K.Literal) { Text = "," }; break;
                    case K.Percent: t[i] = new Tok(K.Literal) { Text = "%" }; break;
                }
            }
            sec.HasDigits = false;
            return;
        }

        // Number section: date letters are literal text here (e.g. "0 \"days\"" is fine,
        // but unquoted letters outside a date section are shown as-is).
        for (int i = 0; i < t.Count; i++)
        {
            switch (t[i].Kind)
            {
                case K.Month: case K.Minute:
                    t[i] = new Tok(K.Literal) { Text = new string('m', t[i].Len) }; break;
                case K.TimeSep:
                    t[i] = new Tok(K.Literal) { Text = ":" }; break;
            }
        }

        int slash = t.FindIndex(x => x.Kind == K.Slash);
        if (slash > 0 && sec.HasDigits && t.FindIndex(x => x.Kind is K.D0 or K.DHash or K.DQ) < slash)
            sec.HasFraction = true;
        else if (slash >= 0)
            t[slash] = new Tok(K.Literal) { Text = "/" };
    }

    private static K? PrevDateToken(List<Tok> t, int i)
    {
        for (int j = i - 1; j >= 0; j--)
            if (t[j].Kind is K.Year or K.Month or K.Day or K.Hour or K.Minute or K.Second or K.ElapsedH or K.ElapsedM or K.ElapsedS)
                return t[j].Kind;
        return null;
    }

    private static K? NextDateToken(List<Tok> t, int i)
    {
        for (int j = i + 1; j < t.Count; j++)
            if (t[j].Kind is K.Year or K.Month or K.Day or K.Hour or K.Minute or K.Second or K.ElapsedH or K.ElapsedM or K.ElapsedS)
                return t[j].Kind;
        return null;
    }

    // ======================================================================================
    // Section selection
    // ======================================================================================

    private static FormattedValue FormatText(Parsed p, string text, NumberFormatOptions o)
    {
        var sec = p.TextSection;
        if (sec is null) return new FormattedValue(text, null, null, false);
        var sb = new StringBuilder();
        foreach (var tok in sec.Tokens)
        {
            if (tok.Kind == K.Text) sb.Append(text);
            else if (tok.Kind == K.Literal) sb.Append(tok.Text);
        }
        return new FormattedValue(sb.ToString(), sec.Color, sec.Fill, false);
    }

    private static FormattedValue FormatNumber(Parsed p, double v, NumberFormatOptions o)
    {
        if (double.IsNaN(v)) return new FormattedValue("#NUM!", null, null, false);
        if (double.IsInfinity(v)) return new FormattedValue("#NUM!", null, null, false);

        var n = p.Numeric;
        Section sec;
        bool showMinus;      // prepend '-' for negatives (single-section / conditional rendering)
        double x = v;

        bool conditional = n[0].CondOp is not null || (n.Count > 1 && n[1].CondOp is not null);
        if (conditional)
        {
            if (n[0].Matches(v)) sec = n[0];
            else if (n.Count > 1 && n[1].Matches(v) && (n[1].CondOp is not null || n.Count == 2)) sec = n[1];
            else if (n.Count > 2) sec = n[2];
            else if (n.Count > 1 && n[1].CondOp is null) sec = n[1];
            else return new FormattedValue(new string('#', 8), null, null, false);
            // As with plain sections, the 2nd section renders the absolute value (its own
            // literals supply any sign, e.g. "[<0]-0.0"); the others prefix '-'.
            showMinus = n.Count < 2 || !ReferenceEquals(sec, n[1]);
            if (v < 0 && !showMinus) x = -v;
        }
        else if (n.Count == 1)
        {
            sec = n[0];               // one section: all numbers, '-' for negatives
            showMinus = true;
        }
        else if (v < 0)
        {
            sec = n[1];               // negative section: absolute value, no automatic '-'
            showMinus = false;
            x = -v;
        }
        else if (v == 0 && n.Count >= 3)
        {
            sec = n[2];               // zero section
            showMinus = false;
        }
        else
        {
            sec = n[0];               // positive (and zero when there are only two sections)
            showMinus = true;
        }

        if (sec.IsEmpty) return new FormattedValue(string.Empty, sec.Color, sec.Fill, false);

        if (sec.IsDate)
        {
            var dateText = RenderDate(sec, x, o);
            return new FormattedValue(dateText, sec.Color, sec.Fill, true);
        }

        bool negative = x < 0;
        double ax = Math.Abs(x);
        var text = RenderNumber(sec, ax, o, out bool roundedZero);
        if (negative && showMinus && !roundedZero) text = "-" + text;
        return new FormattedValue(text, sec.Color, sec.Fill, false);
    }

    // ======================================================================================
    // Numbers
    // ======================================================================================

    private static string RenderNumber(Section sec, double v, NumberFormatOptions o, out bool roundedZero)
    {
        var nf = o.Culture.NumberFormat;
        var t = sec.Tokens;
        roundedZero = false;

        if (!sec.HasDigits)
        {
            // Literals and/or General only.
            var sb0 = new StringBuilder();
            foreach (var tok in t)
            {
                if (tok.Kind == K.General) sb0.Append(GeneralText(v, nf));
                else if (tok.Kind == K.Literal) sb0.Append(tok.Text);
                else if (tok.Kind == K.Percent) sb0.Append('%');
                else if (tok.Kind == K.Point) sb0.Append(nf.NumberDecimalSeparator);
                else if (tok.Kind == K.Comma) sb0.Append(',');
                else if (tok.Kind == K.Slash) sb0.Append('/');
            }
            roundedZero = v == 0;
            return sb0.ToString();
        }

        if (sec.HasFraction) return RenderFraction(sec, v, nf, out roundedZero);

        // ---- Layout analysis ----
        int expIdx = t.FindIndex(x => x.Kind == K.Exponent);
        int numEnd = expIdx >= 0 ? expIdx : t.Count;
        int pointIdx = -1;
        for (int i = 0; i < numEnd; i++) if (t[i].Kind == K.Point) { pointIdx = i; break; }
        int intEnd = pointIdx >= 0 ? pointIdx : numEnd;

        var intPh = new List<int>();
        var decPh = new List<int>();
        for (int i = 0; i < numEnd; i++)
        {
            if (t[i].Kind is not (K.D0 or K.DHash or K.DQ)) continue;
            if (i < intEnd) intPh.Add(i); else decPh.Add(i);
        }

        // Comma roles: grouping (between integer placeholders) or scaling (after the last digit).
        bool grouping = false;
        int scale = 0;
        var commaRole = new Dictionary<int, char>();   // 'g' grouping, 's' scaling, 'l' literal
        int firstInt = intPh.Count > 0 ? intPh[0] : -1, lastInt = intPh.Count > 0 ? intPh[^1] : -1;
        int lastDigit = -1;
        for (int i = 0; i < numEnd; i++) if (t[i].Kind is K.D0 or K.DHash or K.DQ) lastDigit = i;
        for (int i = 0; i < t.Count; i++)
        {
            if (t[i].Kind != K.Comma) continue;
            if (i < intEnd && firstInt >= 0 && i > firstInt && i < lastInt) { grouping = true; commaRole[i] = 'g'; }
            else if (lastDigit >= 0 && i > lastDigit && i <= numEnd && OnlyCommasBetween(t, lastDigit, i))
            { scale++; commaRole[i] = 's'; }
            else if (lastDigit >= 0 && i > lastInt && i == intEnd - 1 && pointIdx >= 0) { scale++; commaRole[i] = 's'; }
            else commaRole[i] = 'l';
        }

        int pct = 0;
        foreach (var tok in t) if (tok.Kind == K.Percent) pct++;

        double val = v * Math.Pow(100, pct) / Math.Pow(1000, scale);
        int decimals = decPh.Count;

        // ---- Exponent ----
        int exponent = 0;
        if (expIdx >= 0)
        {
            int k = Math.Max(1, intPh.Count);
            bool hasHash = intPh.Exists(i => t[i].Kind == K.DHash);
            if (val != 0)
            {
                exponent = (int)Math.Floor(Math.Log10(val));
                exponent = hasHash && k > 1 ? (int)Math.Floor(exponent / (double)k) * k : exponent - (k - 1);
                double m = val / Math.Pow(10, exponent);
                double mr = Math.Round(m, Math.Min(decimals, 15), MidpointRounding.AwayFromZero);
                if (mr >= Math.Pow(10, hasHash && k > 1 ? k : k))
                {
                    exponent += hasHash && k > 1 ? k : 1;
                }
                val = val / Math.Pow(10, exponent);
            }
        }

        // ---- Round and split ----
        SplitRounded(val, decimals, out var intDigits, out var fracDigits);
        roundedZero = intDigits.Length == 0 && fracDigits.TrimEnd('0').Length == 0 && expIdx < 0;
        if (expIdx >= 0 && intDigits.Length == 0 && fracDigits.TrimEnd('0').Length == 0) roundedZero = true;

        // ---- Assign integer digits to placeholders (right-aligned, extras to the leftmost) ----
        var intOut = AssignInteger(t, intPh, intDigits, grouping, nf);

        // ---- Fraction digits ----
        int lastSig = fracDigits.TrimEnd('0').Length - 1;
        var decOut = new Dictionary<int, string>();
        for (int j = 0; j < decPh.Count; j++)
        {
            char d = fracDigits[j];
            var kind = t[decPh[j]].Kind;
            decOut[decPh[j]] = j <= lastSig ? d.ToString() : kind == K.D0 ? "0" : kind == K.DQ ? " " : "";
        }

        // ---- Emit ----
        var sb = new StringBuilder();
        for (int i = 0; i < t.Count; i++)
        {
            var tok = t[i];
            switch (tok.Kind)
            {
                case K.D0: case K.DHash: case K.DQ:
                    if (intOut.TryGetValue(i, out var s1)) sb.Append(s1);
                    else if (decOut.TryGetValue(i, out var s2)) sb.Append(s2);
                    break;
                case K.Point:
                    sb.Append(i == pointIdx ? nf.NumberDecimalSeparator : ".");
                    break;
                case K.Comma:
                    if (commaRole.TryGetValue(i, out var role) && role == 'l') sb.Append(',');
                    break;
                case K.Percent: sb.Append('%'); break;
                case K.Exponent:
                    sb.Append(tok.Text);
                    if (exponent < 0) sb.Append('-'); else if (tok.Plus) sb.Append('+');
                    sb.Append(Math.Abs(exponent).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(tok.Len, 1), '0'));
                    break;
                case K.Literal: sb.Append(tok.Text); break;
                case K.General: sb.Append(GeneralText(v, nf)); break;
                case K.Slash: sb.Append('/'); break;
            }
        }
        return sb.ToString();
    }

    private static bool OnlyCommasBetween(List<Tok> t, int from, int to)
    {
        for (int i = from + 1; i < to; i++) if (t[i].Kind != K.Comma) return false;
        return true;
    }

    /// <summary>
    /// Rounds half away from zero to <paramref name="decimals"/> places. Uses decimal
    /// arithmetic (which, like Excel, keeps 15 significant digits of a double) when in range.
    /// intDigits is "" for a zero integer part.
    /// </summary>
    private static void SplitRounded(double val, int decimals, out string intDigits, out string fracDigits)
    {
        decimals = Math.Min(decimals, 27);
        string s;
        if (val < 7.9e27)
        {
            decimal d = (decimal)val;
            d = Math.Round(d, decimals, MidpointRounding.AwayFromZero);
            s = d.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
        else
        {
            // Beyond decimal range: Excel shows 15 significant digits then zeros.
            s = val.ToString("E14", CultureInfo.InvariantCulture);
            var mant = s.Substring(0, s.IndexOf('E')).Replace(".", "");
            int exp = int.Parse(s.Substring(s.IndexOf('E') + 1), CultureInfo.InvariantCulture);
            s = mant.PadRight(exp + 1, '0') + (decimals > 0 ? "." + new string('0', decimals) : "");
        }
        int dot = s.IndexOf('.');
        intDigits = dot < 0 ? s : s.Substring(0, dot);
        fracDigits = dot < 0 ? string.Empty : s.Substring(dot + 1);
        if (intDigits == "0") intDigits = string.Empty;
    }

    /// <summary>
    /// Places integer digits into placeholders from the right; any extra leading digits go to
    /// the leftmost placeholder. Unfilled placeholders: 0 → "0", ? → " ", # → "". Group
    /// separators follow the culture's group sizes (e.g. 3;2 for 1,00,000).
    /// </summary>
    private static Dictionary<int, string> AssignInteger(List<Tok> t, List<int> ph, string digits,
        bool grouping, NumberFormatInfo nf)
    {
        var outp = new Dictionary<int, string>();
        int di = digits.Length - 1;
        int pos = 0;   // digit position from the right among emitted digits
        string sep = nf.NumberGroupSeparator;
        int[] sizes = nf.NumberGroupSizes;

        string Emit(char ch)
        {
            string piece = ch.ToString();
            if (grouping && pos > 0 && IsBoundary(pos, sizes)) piece += sep;
            pos++;
            return piece;
        }

        for (int j = ph.Count - 1; j >= 0; j--)
        {
            var kind = t[ph[j]].Kind;
            string piece;
            if (di >= 0) piece = Emit(digits[di--]);
            else piece = kind == K.D0 ? Emit('0') : kind == K.DQ ? " " : "";
            if (j == 0)
            {
                var pre = new StringBuilder();
                while (di >= 0) pre.Insert(0, Emit(digits[di--]));
                piece = pre + piece;
            }
            outp[ph[j]] = piece;
        }
        return outp;
    }

    private static bool IsBoundary(int pos, int[] sizes)
    {
        if (sizes is null || sizes.Length == 0) return false;
        int b = 0;
        for (int k = 0; ; k++)
        {
            int size = sizes[Math.Min(k, sizes.Length - 1)];
            if (size <= 0) return false;
            b += size;
            if (b == pos) return true;
            if (b > pos) return false;
        }
    }

    // ======================================================================================
    // Fractions
    // ======================================================================================

    private static string RenderFraction(Section sec, double v, NumberFormatInfo nf, out bool roundedZero)
    {
        var t = sec.Tokens;
        int slash = t.FindIndex(x => x.Kind == K.Slash);

        // Numerator: digit placeholders immediately before the slash.
        int numStart = slash;
        while (numStart > 0 && t[numStart - 1].Kind is K.D0 or K.DHash or K.DQ) numStart--;
        var numPh = new List<int>();
        for (int i = numStart; i < slash; i++) numPh.Add(i);

        // Whole part: placeholders before the numerator group.
        var wholePh = new List<int>();
        for (int i = 0; i < numStart; i++) if (t[i].Kind is K.D0 or K.DHash or K.DQ) wholePh.Add(i);

        // Denominator: fixed digits (literal 1-9 then digits) or placeholders.
        int denEnd = slash + 1;
        int fixedDen = 0;
        var denPh = new List<int>();
        if (denEnd < t.Count && t[denEnd].Kind == K.Literal && t[denEnd].Text.Length > 0 && char.IsDigit(t[denEnd].Text[0]))
        {
            var sbd = new StringBuilder();
            var lit = t[denEnd].Text;
            int q = 0;
            while (q < lit.Length && char.IsDigit(lit[q])) sbd.Append(lit[q++]);
            int k = denEnd + 1;
            if (q == lit.Length)
                while (k < t.Count && t[k].Kind == K.D0) { sbd.Append('0'); k++; }
            fixedDen = int.Parse(sbd.ToString(), CultureInfo.InvariantCulture);
        }
        else
        {
            while (denEnd < t.Count && t[denEnd].Kind is K.D0 or K.DHash or K.DQ) { denPh.Add(denEnd); denEnd++; }
        }

        double whole = wholePh.Count > 0 ? Math.Floor(v) : 0;
        double frac = v - whole;
        long num, den;
        if (fixedDen > 0)
        {
            den = fixedDen;
            num = (long)Math.Round(frac * den, MidpointRounding.AwayFromZero);
        }
        else
        {
            int maxDen = (int)Math.Min(99999, Math.Pow(10, Math.Max(1, denPh.Count)) - 1);
            BestFraction(frac, maxDen, out num, out den);
        }
        if (wholePh.Count > 0 && num == den && den > 0) { whole += 1; num = 0; }
        roundedZero = whole == 0 && num == 0;

        string wholeDigits = whole == 0 ? "" : whole.ToString("F0", CultureInfo.InvariantCulture);
        var wholeOut = AssignInteger(t, wholePh, wholeDigits, false, nf);
        bool hideFraction = num == 0 && wholePh.Count > 0;
        if (wholePh.Count > 0 && whole == 0 && num == 0)
            wholeOut = AssignInteger(t, wholePh, "0", false, nf);   // a bare zero still shows "0"
        var numOut = AssignInteger(t, numPh, num.ToString(CultureInfo.InvariantCulture), false, nf);

        var sb = new StringBuilder();
        for (int i = 0; i < t.Count; i++)
        {
            var tok = t[i];
            if (wholeOut.TryGetValue(i, out var w)) { sb.Append(w); continue; }
            if (numOut.TryGetValue(i, out var nn)) { sb.Append(hideFraction ? new string(' ', nn.Length) : nn); continue; }
            if (i == slash) { sb.Append(hideFraction ? ' ' : '/'); continue; }
            if (fixedDen > 0 && i == slash + 1)
            {
                var d = fixedDen.ToString(CultureInfo.InvariantCulture);
                sb.Append(hideFraction ? new string(' ', d.Length) : d);
                int k = i + 1;
                // skip the trailing zero placeholders that were part of the fixed denominator
                var lit = tok.Text;
                int q = 0;
                while (q < lit.Length && char.IsDigit(lit[q])) q++;
                if (q < lit.Length) sb.Append(lit.Substring(q));
                else while (k < t.Count && t[k].Kind == K.D0) { k++; }
                i = k - 1;
                continue;
            }
            if (denPh.Count > 0 && i == denPh[0])
            {
                var d = den.ToString(CultureInfo.InvariantCulture);
                var padded = new StringBuilder(d);
                for (int j = d.Length; j < denPh.Count; j++)
                    padded.Append(t[denPh[j]].Kind == K.DQ ? ' ' : t[denPh[j]].Kind == K.D0 ? '0' : '\0');
                var ds = padded.ToString().Replace("\0", "");
                sb.Append(hideFraction ? new string(' ', ds.Length) : ds);
                i = denPh[^1];
                continue;
            }
            switch (tok.Kind)
            {
                case K.Literal: sb.Append(tok.Text); break;
                case K.Percent: sb.Append('%'); break;
                case K.Point: sb.Append(nf.NumberDecimalSeparator); break;
                case K.General: sb.Append(GeneralText(v, nf)); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Closest fraction to x (0 ≤ x &lt; 1) with denominator ≤ maxDen.</summary>
    private static void BestFraction(double x, int maxDen, out long num, out long den)
    {
        num = 0; den = 1;
        double best = Math.Abs(x);
        for (int d = 1; d <= maxDen; d++)
        {
            long n = (long)Math.Round(x * d, MidpointRounding.AwayFromZero);
            double err = Math.Abs(x - (double)n / d);
            if (err < best - 1e-12) { best = err; num = n; den = d; if (err == 0) break; }
        }
    }

    // ======================================================================================
    // General
    // ======================================================================================

    /// <summary>
    /// Excel's General format for a stored-width cell: up to 11 characters, i.e. about ten
    /// significant digits; scientific (1.23457E+11) beyond 1E+11 or below 1E-9.
    /// </summary>
    internal static string GeneralText(double v, NumberFormatInfo nf)
    {
        if (v == 0) return "0";
        bool neg = v < 0;
        double a = Math.Abs(v);
        int e = (int)Math.Floor(Math.Log10(a));
        string body;
        if (e >= 11 || e < -9)
        {
            double m = a / Math.Pow(10, e);
            m = Math.Round(m, 5, MidpointRounding.AwayFromZero);
            if (m >= 10) { m /= 10; e++; }
            var ms = m.ToString("0.#####", CultureInfo.InvariantCulture).Replace(".", nf.NumberDecimalSeparator);
            body = ms + "E" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString("00", CultureInfo.InvariantCulture);
        }
        else
        {
            int decimals = e >= 0 ? Math.Max(0, 9 - e) : 9;
            SplitRounded(a, decimals, out var ip, out var fp);
            fp = fp.TrimEnd('0');
            if (ip.Length == 0) ip = "0";
            body = fp.Length > 0 ? ip + nf.NumberDecimalSeparator + fp : ip;
        }
        return neg ? "-" + body : body;
    }

    // ======================================================================================
    // Dates and times
    // ======================================================================================

    private static string RenderDate(Section sec, double serial, NumberFormatOptions o)
    {
        if (serial < 0 || serial >= 2958466) return new string('#', 8);
        var dtf = o.Culture.DateTimeFormat;
        var t = sec.Tokens;

        int p = 0;
        bool hasAmPm = false;
        foreach (var tok in t)
        {
            if (tok.Kind == K.SubSecond) p = Math.Max(p, Math.Min(3, tok.Len));
            if (tok.Kind == K.AmPm) hasAmPm = true;
        }

        long unitsPerDay = 86400L * Pow10(p);
        long days = (long)Math.Floor(serial);
        long units = (long)Math.Round((serial - days) * unitsPerDay, MidpointRounding.AwayFromZero);
        if (units >= unitsPerDay) { days++; units -= unitsPerDay; }
        long totalSec = units / Pow10(p);
        long subUnits = units % Pow10(p);
        int hour = (int)(totalSec / 3600), minute = (int)(totalSec / 60 % 60), second = (int)(totalSec % 60);

        DateParts(days, o.Date1904, out int year, out int month, out int day, out int weekday);

        var sb = new StringBuilder();
        foreach (var tok in t)
        {
            switch (tok.Kind)
            {
                case K.Year:
                    sb.Append(tok.Len <= 2 ? (year % 100).ToString("00", CultureInfo.InvariantCulture)
                                           : year.ToString("0000", CultureInfo.InvariantCulture));
                    break;
                case K.Month:
                    sb.Append(tok.Len switch
                    {
                        1 => month.ToString(CultureInfo.InvariantCulture),
                        2 => month.ToString("00", CultureInfo.InvariantCulture),
                        3 => dtf.AbbreviatedMonthNames[month - 1],
                        5 => dtf.MonthNames[month - 1].Substring(0, 1),
                        _ => dtf.MonthNames[month - 1],
                    });
                    break;
                case K.Day:
                    sb.Append(tok.Len switch
                    {
                        1 => day.ToString(CultureInfo.InvariantCulture),
                        2 => day.ToString("00", CultureInfo.InvariantCulture),
                        3 => dtf.AbbreviatedDayNames[weekday],
                        _ => dtf.DayNames[weekday],
                    });
                    break;
                case K.Hour:
                {
                    int h = hasAmPm ? (hour % 12 == 0 ? 12 : hour % 12) : hour;
                    sb.Append(tok.Len >= 2 ? h.ToString("00", CultureInfo.InvariantCulture) : h.ToString(CultureInfo.InvariantCulture));
                    break;
                }
                case K.Minute:
                    sb.Append(tok.Len >= 2 ? minute.ToString("00", CultureInfo.InvariantCulture) : minute.ToString(CultureInfo.InvariantCulture));
                    break;
                case K.Second:
                    sb.Append(tok.Len >= 2 ? second.ToString("00", CultureInfo.InvariantCulture) : second.ToString(CultureInfo.InvariantCulture));
                    break;
                case K.SubSecond:
                    sb.Append(o.Culture.NumberFormat.NumberDecimalSeparator);
                    sb.Append(subUnits.ToString(CultureInfo.InvariantCulture).PadLeft(p, '0').Substring(0, Math.Min(tok.Len, p)));
                    break;
                case K.ElapsedH:
                    sb.Append((days * 24 + hour).ToString(CultureInfo.InvariantCulture).PadLeft(tok.Len, '0'));
                    break;
                case K.ElapsedM:
                    sb.Append(((days * 24 + hour) * 60 + minute).ToString(CultureInfo.InvariantCulture).PadLeft(tok.Len, '0'));
                    break;
                case K.ElapsedS:
                    sb.Append((days * 86400 + totalSec).ToString(CultureInfo.InvariantCulture).PadLeft(tok.Len, '0'));
                    break;
                case K.AmPm:
                {
                    bool pm = hour >= 12;
                    if (tok.Text.Length == 5)
                    {
                        var s = pm ? "PM" : "AM";
                        sb.Append(char.IsLower(tok.Text[0]) ? s.ToLowerInvariant() : s);
                    }
                    else
                    {
                        char ch = pm ? tok.Text[2] : tok.Text[0];
                        sb.Append(ch);
                    }
                    break;
                }
                case K.DateSep: sb.Append(dtf.DateSeparator); break;
                case K.TimeSep: sb.Append(dtf.TimeSeparator); break;
                case K.Literal: sb.Append(tok.Text); break;
                case K.General: sb.Append(GeneralText(serial, o.Culture.NumberFormat)); break;
            }
        }
        return sb.ToString();
    }

    private static long Pow10(int p) => p switch { 0 => 1, 1 => 10, 2 => 100, _ => 1000 };

    /// <summary>
    /// Calendar parts for a whole-day serial, reproducing Excel's 1900 system (serial 0 shows
    /// as 1900-01-00, serial 60 as 1900-02-29). weekday: 0 = Sunday.
    /// </summary>
    private static void DateParts(long days, bool date1904, out int y, out int m, out int d, out int weekday)
    {
        if (date1904)
        {
            var dt = new DateTime(1904, 1, 1).AddDays(days);
            y = dt.Year; m = dt.Month; d = dt.Day;
            weekday = (int)(((days + 5) % 7 + 7) % 7);
            return;
        }
        weekday = (int)(((days - 1) % 7 + 7) % 7);
        if (days == 0) { y = 1900; m = 1; d = 0; return; }
        if (days == 60) { y = 1900; m = 2; d = 29; return; }
        var date = days < 60 ? new DateTime(1899, 12, 31).AddDays(days) : new DateTime(1899, 12, 30).AddDays(days);
        y = date.Year; m = date.Month; d = date.Day;
    }
}
