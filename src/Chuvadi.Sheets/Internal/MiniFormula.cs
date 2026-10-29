using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>Thrown when a formula uses something the conditional-format evaluator does not support.</summary>
internal sealed class FormulaNotSupportedException : Exception
{
    public FormulaNotSupportedException(string message) : base(message) { }
}

/// <summary>An Excel error value (#DIV/0!, #N/A, ...).</summary>
internal sealed record FormulaError(string Code)
{
    public static readonly FormulaError Div0 = new("#DIV/0!");
    public static readonly FormulaError Value = new("#VALUE!");
    public static readonly FormulaError Ref = new("#REF!");
    public static readonly FormulaError NA = new("#N/A");
    public static readonly FormulaError Num = new("#NUM!");
    public override string ToString() => Code;

    internal static readonly HashSet<string> Literals = new(StringComparer.Ordinal)
    {
        "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A", "#GETTING_DATA",
        "#SPILL!", "#CALC!", "#FIELD!", "#BLOCKED!", "#CONNECT!", "#BUSY!", "#UNKNOWN!",
    };
}

/// <summary>A rectangular reference with absolute/relative flags, resolved per evaluated cell.</summary>
internal sealed record RefBox(int R1, int C1, int R2, int C2, bool R1Abs, bool C1Abs, bool R2Abs, bool C2Abs, bool WholeColumn, bool WholeRow);

/// <summary>A resolved range value.</summary>
internal sealed record RangeValue(int R1, int C1, int R2, int C2);

/// <summary>
/// A small Excel formula evaluator for conditional formatting: literals, same-sheet cell and
/// range references (relative ones move with the evaluated cell), arithmetic, comparison,
/// concatenation, percent, and a fixed set of functions. Anything else throws
/// <see cref="FormulaNotSupportedException"/>, which the evaluator reports as "not evaluated".
/// </summary>
internal sealed class MiniFormula
{
    private abstract record Node;
    private sealed record Lit(object? Value) : Node;
    private sealed record RefNode(RefBox Box) : Node;
    private sealed record Unary(char Op, Node Arg) : Node;
    private sealed record Percent(Node Arg) : Node;
    private sealed record Binary(string Op, Node Left, Node Right) : Node;
    private sealed record Call(string Name, Node[] Args) : Node;

    private readonly Node _root;

    private MiniFormula(Node root) { _root = root; }

    public static MiniFormula Parse(string formula)
    {
        var p = new Parser(formula.StartsWith("=", StringComparison.Ordinal) ? formula.Substring(1) : formula);
        var n = p.ParseExpression();
        p.ExpectEnd();
        return new MiniFormula(n);
    }

    /// <summary>
    /// Evaluates for the cell (row, col), with relative references moved by
    /// (row - anchorRow, col - anchorCol). Returns a scalar (double, string, bool, FormulaError, null for blank).
    /// </summary>
    public object? Evaluate(EvalContext ctx) => Scalar(Eval(_root, ctx), ctx);

    // ---- Evaluation ----------------------------------------------------------------

    internal sealed class EvalContext
    {
        public required ICellValueSource Source { get; init; }
        public required int Row { get; init; }
        public required int Column { get; init; }
        public required int AnchorRow { get; init; }
        public required int AnchorColumn { get; init; }
        public required DateTime Today { get; init; }
        public bool Date1904 { get; init; }
    }

    private static object? Eval(Node n, EvalContext c)
    {
        switch (n)
        {
            case Lit l: return l.Value;
            case RefNode r: return Resolve(r.Box, c);
            case Unary u:
            {
                var v = Scalar(Eval(u.Arg, c), c);
                if (v is FormulaError) return v;
                var d = ToNumber(v);
                if (d is FormulaError) return d;
                return u.Op == '-' ? -(double)d! : (double)d!;
            }
            case Percent p:
            {
                var d = ToNumber(Scalar(Eval(p.Arg, c), c));
                return d is double x ? x / 100 : d;
            }
            case Binary b: return EvalBinary(b, c);
            case Call f: return Functions.Call(f.Name, f.Args, c);
        }
        throw new FormulaNotSupportedException("Unknown node.");
    }

    private static object? EvalBinary(Binary b, EvalContext c)
    {
        var l = Scalar(Eval(b.Left, c), c);
        var r = Scalar(Eval(b.Right, c), c);
        if (l is FormulaError) return l;
        if (r is FormulaError) return r;
        switch (b.Op)
        {
            case "&": return Text(l) + Text(r);
            case "=": return Compare(l, r) == 0;
            case "<>": return Compare(l, r) != 0;
            case "<": return Compare(l, r) < 0;
            case ">": return Compare(l, r) > 0;
            case "<=": return Compare(l, r) <= 0;
            case ">=": return Compare(l, r) >= 0;
        }
        var x = ToNumber(l); if (x is FormulaError) return x;
        var y = ToNumber(r); if (y is FormulaError) return y;
        double a = (double)x!, d = (double)y!;
        return b.Op switch
        {
            "+" => a + d,
            "-" => a - d,
            "*" => a * d,
            "/" => d == 0 ? FormulaError.Div0 : a / d,
            "^" => Math.Pow(a, d),
            _ => throw new FormulaNotSupportedException($"Operator {b.Op}."),
        };
    }

    private static object Resolve(RefBox b, EvalContext c)
    {
        int dr = c.Row - c.AnchorRow, dc = c.Column - c.AnchorColumn;
        int r1 = b.WholeColumn ? 1 : b.R1Abs ? b.R1 : b.R1 + dr;
        int r2 = b.WholeColumn ? 1048576 : b.R2Abs ? b.R2 : b.R2 + dr;
        int c1 = b.WholeRow ? 1 : b.C1Abs ? b.C1 : b.C1 + dc;
        int c2 = b.WholeRow ? 16384 : b.C2Abs ? b.C2 : b.C2 + dc;
        if (r1 < 1 || c1 < 1 || r2 > 1048576 || c2 > 16384 || r2 < 1 || c2 < 1) return FormulaError.Ref;
        return new RangeValue(Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));
    }

    /// <summary>A range used as a single value: a one-cell range, or implicit intersection with the evaluated cell's row/column.</summary>
    internal static object? Scalar(object? v, EvalContext c)
    {
        if (v is not RangeValue rg) return v;
        if (rg.R1 == rg.R2 && rg.C1 == rg.C2) return Normalize(c.Source.GetValue(rg.R1, rg.C1), c);
        if (rg.C1 == rg.C2 && c.Row >= rg.R1 && c.Row <= rg.R2) return Normalize(c.Source.GetValue(c.Row, rg.C1), c);
        if (rg.R1 == rg.R2 && c.Column >= rg.C1 && c.Column <= rg.C2) return Normalize(c.Source.GetValue(rg.R1, c.Column), c);
        return FormulaError.Value;
    }

    /// <summary>A cell value as a formula value: numbers (dates as serials), text, booleans, errors, blank.</summary>
    internal static object? Normalize(object? v, EvalContext c) => Normalize(v, c.Date1904);

    internal static object? Normalize(object? v, bool date1904) => v switch
    {
        null => null,
        string s when FormulaError.Literals.Contains(s) => new FormulaError(s),
        string s => s,
        bool b => b,
        double d => d,
        DateTime dt => dt.ToOADate() - (date1904 ? 1462 : 0),
        DateOnly d => d.ToDateTime(TimeOnly.MinValue).ToOADate() - (date1904 ? 1462 : 0),
        DateTimeOffset dto => dto.DateTime.ToOADate() - (date1904 ? 1462 : 0),
        TimeSpan ts => ts.TotalDays,
        IConvertible ic when v is int or long or short or byte or float or decimal or uint or ulong or sbyte or ushort
            => ic.ToDouble(CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    internal static object ToNumber(object? v) => v switch
    {
        null => 0.0,
        double d => d,
        bool b => b ? 1.0 : 0.0,
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : FormulaError.Value,
        FormulaError e => e,
        _ => FormulaError.Value,
    };

    internal static string Text(object? v) => v switch
    {
        null => string.Empty,
        double d => d.ToString("G15", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        FormulaError e => e.Code,
        _ => v.ToString() ?? string.Empty,
    };

    internal static bool Truthy(object? v) => v switch
    {
        bool b => b,
        double d => d != 0,
        string s => s.Equals("TRUE", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Excel comparison: blank vs anything as 0 / ""; numbers &lt; text &lt; booleans; text case-insensitive.</summary>
    internal static int Compare(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) a = b is string ? string.Empty : b is bool ? false : 0.0;
        if (b is null) b = a is string ? string.Empty : a is bool ? false : 0.0;
        int Rank(object x) => x is double ? 0 : x is string ? 1 : 2;
        int ra = Rank(a), rb = Rank(b);
        if (ra != rb) return ra.CompareTo(rb);
        return a switch
        {
            double x => x.CompareTo((double)b),
            string s => string.Compare(s, (string)b, StringComparison.OrdinalIgnoreCase),
            bool p => p.CompareTo((bool)b),
            _ => 0,
        };
    }

    // ---- Functions -------------------------------------------------------------------

    private static class Functions
    {
        public static object? Call(string name, Node[] args, EvalContext c)
        {
            object? A(int i) => Scalar(Eval(args[i], c), c);
            double N(int i) { var v = ToNumber(A(i)); return v is double d ? d : throw new ErrorSignal((FormulaError)v); }
            string S(int i) { var v = A(i); return v is FormulaError e ? throw new ErrorSignal(e) : Text(v); }
            void Count(int min, int max) { if (args.Length < min || args.Length > max) throw new FormulaNotSupportedException($"{name} with {args.Length} arguments."); }
            try
            {
                switch (name)
                {
                    case "AND": case "OR":
                    {
                        if (args.Length == 0) throw new FormulaNotSupportedException(name);
                        bool and = name == "AND", acc = and;
                        foreach (var v in Values(args, c, flattenRanges: true))
                        {
                            if (v is FormulaError e) return e;
                            if (v is null || v is string) continue;
                            bool t = Truthy(v);
                            acc = and ? acc && t : acc || t;
                        }
                        return acc;
                    }
                    case "NOT": Count(1, 1); { var v = A(0); return v is FormulaError e ? e : !Truthy(v); }
                    case "IF":
                    {
                        Count(2, 3);
                        var cond = A(0);
                        if (cond is FormulaError e) return e;
                        return Truthy(cond) ? A(1) : args.Length > 2 ? A(2) : false;
                    }
                    case "ISBLANK": Count(1, 1); return A(0) is null;
                    case "ISNUMBER": Count(1, 1); return A(0) is double;
                    case "ISTEXT": Count(1, 1); return A(0) is string;
                    case "ISNONTEXT": Count(1, 1); return A(0) is not string;
                    case "ISLOGICAL": Count(1, 1); return A(0) is bool;
                    case "ISERROR": Count(1, 1); return A(0) is FormulaError;
                    case "ISERR": Count(1, 1); return A(0) is FormulaError e2 && e2.Code != "#N/A";
                    case "ISNA": Count(1, 1); return A(0) is FormulaError { Code: "#N/A" };
                    case "ISEVEN": Count(1, 1); return Math.Floor(Math.Abs(N(0))) % 2 == 0;
                    case "ISODD": Count(1, 1); return Math.Floor(Math.Abs(N(0))) % 2 == 1;
                    case "ROW":
                        Count(0, 1);
                        return args.Length == 0 ? (double)c.Row : Eval(args[0], c) is RangeValue rr ? (double)rr.R1 : throw new FormulaNotSupportedException("ROW of a non-reference.");
                    case "COLUMN":
                        Count(0, 1);
                        return args.Length == 0 ? (double)c.Column : Eval(args[0], c) is RangeValue rc ? (double)rc.C1 : throw new FormulaNotSupportedException("COLUMN of a non-reference.");
                    case "MOD":
                    {
                        Count(2, 2);
                        double a = N(0), d = N(1);
                        if (d == 0) return FormulaError.Div0;
                        return a - d * Math.Floor(a / d);
                    }
                    case "ABS": Count(1, 1); return Math.Abs(N(0));
                    case "INT": Count(1, 1); return Math.Floor(N(0));
                    case "ROUND": Count(2, 2); return RoundAway(N(0), (int)N(1));
                    case "ROUNDDOWN": Count(2, 2); { double f = Math.Pow(10, (int)N(1)); return Math.Truncate(N(0) * f) / f; }
                    case "ROUNDUP": Count(2, 2); { double f = Math.Pow(10, (int)N(1)); var x = N(0) * f; return (x < 0 ? Math.Floor(x) : Math.Ceiling(x)) / f; }
                    case "FLOOR": Count(2, 2); { double s = N(1); return s == 0 ? 0.0 : Math.Floor(N(0) / s) * s; }
                    case "LEN": Count(1, 1); return (double)S(0).Length;
                    case "LEFT": Count(1, 2); { var s = S(0); int k = args.Length > 1 ? (int)N(1) : 1; return k < 0 ? FormulaError.Value : s.Substring(0, Math.Min(k, s.Length)); }
                    case "RIGHT": Count(1, 2); { var s = S(0); int k = args.Length > 1 ? (int)N(1) : 1; return k < 0 ? FormulaError.Value : s.Substring(s.Length - Math.Min(k, s.Length)); }
                    case "MID":
                    {
                        Count(3, 3);
                        var s = S(0); int start = (int)N(1), len = (int)N(2);
                        if (start < 1 || len < 0) return FormulaError.Value;
                        if (start > s.Length) return string.Empty;
                        return s.Substring(start - 1, Math.Min(len, s.Length - start + 1));
                    }
                    case "UPPER": Count(1, 1); return S(0).ToUpperInvariant();
                    case "LOWER": Count(1, 1); return S(0).ToLowerInvariant();
                    case "TRIM": Count(1, 1); return string.Join(" ", S(0).Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    case "SEARCH": case "FIND":
                    {
                        Count(2, 3);
                        var find = S(0); var within = S(1); int start = args.Length > 2 ? (int)N(2) : 1;
                        if (start < 1 || start > within.Length + 1) return FormulaError.Value;
                        int idx = name == "FIND"
                            ? within.IndexOf(find, start - 1, StringComparison.Ordinal)
                            : SearchIndex(within, find, start - 1);
                        return idx < 0 ? FormulaError.Value : idx + 1.0;
                    }
                    case "EXACT": Count(2, 2); return string.Equals(S(0), S(1), StringComparison.Ordinal);
                    case "SUM": return Aggregate(args, c, v => v.Sum(), countOnly: false);
                    case "AVERAGE": { var nums = Numbers(args, c).ToList(); return nums.Count == 0 ? FormulaError.Div0 : nums.Average(); }
                    case "MIN": { var nums = Numbers(args, c).ToList(); return nums.Count == 0 ? 0.0 : nums.Min(); }
                    case "MAX": { var nums = Numbers(args, c).ToList(); return nums.Count == 0 ? 0.0 : nums.Max(); }
                    case "COUNT": return (double)Numbers(args, c).Count();
                    case "COUNTA": return (double)Values(args, c, flattenRanges: true).Count(v => v is not null);
                    case "COUNTBLANK":
                    {
                        Count(1, 1);
                        if (Eval(args[0], c) is not RangeValue rg) throw new FormulaNotSupportedException("COUNTBLANK of a non-range.");
                        long cells = (long)(rg.R2 - rg.R1 + 1) * (rg.C2 - rg.C1 + 1);
                        long filled = c.Source.GetValues(rg.R1, rg.C1, rg.R2, rg.C2).Count(t => Normalize(t.Value, c) is not (null or ""));
                        return (double)(cells - filled);
                    }
                    case "COUNTIF":
                    {
                        Count(2, 2);
                        if (Eval(args[0], c) is not RangeValue rg) throw new FormulaNotSupportedException("COUNTIF of a non-range.");
                        var crit = A(1);
                        var match = Criteria(crit);
                        long n = 0;
                        foreach (var t in c.Source.GetValues(rg.R1, rg.C1, rg.R2, rg.C2))
                            if (match(Normalize(t.Value, c))) n++;
                        // Blank cells match criteria that match blank ("" or "=").
                        if (match(null))
                        {
                            long cells = (long)(rg.R2 - rg.R1 + 1) * (rg.C2 - rg.C1 + 1);
                            long present = c.Source.GetValues(rg.R1, rg.C1, rg.R2, rg.C2).LongCount();
                            n += cells - present;
                        }
                        return (double)n;
                    }
                    case "TODAY": Count(0, 0); return Serial(c.Today.Date, c);
                    case "NOW": Count(0, 0); return Serial(DateTime.Now, c);
                    case "DATE":
                    {
                        Count(3, 3);
                        var d = new DateTime((int)N(0), 1, 1).AddMonths((int)N(1) - 1).AddDays((int)N(2) - 1);
                        return Serial(d, c);
                    }
                    case "YEAR": Count(1, 1); return (double)FromSerial(N(0), c).Year;
                    case "MONTH": Count(1, 1); return (double)FromSerial(N(0), c).Month;
                    case "DAY": Count(1, 1); return (double)FromSerial(N(0), c).Day;
                    case "WEEKDAY":
                    {
                        Count(1, 2);
                        int type = args.Length > 1 ? (int)N(1) : 1;
                        int dow = (int)FromSerial(N(0), c).DayOfWeek;   // Sunday = 0
                        return type switch
                        {
                            1 => dow + 1.0,
                            2 => (dow + 6) % 7 + 1.0,
                            3 => (dow + 6) % 7 + 0.0,
                            _ => throw new FormulaNotSupportedException($"WEEKDAY type {type}."),
                        };
                    }
                    case "EDATE":
                    {
                        Count(2, 2);
                        return Serial(FromSerial(N(0), c).Date.AddMonths((int)N(1)), c);
                    }
                    case "TRUE": Count(0, 0); return true;
                    case "FALSE": Count(0, 0); return false;
                }
            }
            catch (ErrorSignal e) { return e.Error; }
            throw new FormulaNotSupportedException($"Function {name} is not evaluated.");
        }

        private sealed class ErrorSignal : Exception
        {
            public ErrorSignal(FormulaError e) { Error = e; }
            public FormulaError Error { get; }
        }

        private static double RoundAway(double v, int digits)
        {
            double f = Math.Pow(10, digits);
            return Math.Round(v * f, MidpointRounding.AwayFromZero) / f;
        }

        private static double Serial(DateTime d, EvalContext c) => d.ToOADate() - (c.Date1904 ? 1462 : 0);

        private static DateTime FromSerial(double s, EvalContext c) => DateTime.FromOADate(s + (c.Date1904 ? 1462 : 0));

        /// <summary>SEARCH with ? and * wildcards (~ escapes), case-insensitive.</summary>
        private static int SearchIndex(string within, string find, int start)
        {
            if (find.IndexOfAny(new[] { '*', '?', '~' }) < 0)
                return within.IndexOf(find, start, StringComparison.OrdinalIgnoreCase);
            var rx = new System.Text.RegularExpressions.Regex(WildcardToRegex(find, anchored: false),
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            var m = rx.Match(within, start);
            return m.Success ? m.Index : -1;
        }

        internal static string WildcardToRegex(string pattern, bool anchored)
        {
            var sb = new StringBuilder(anchored ? "^" : "");
            for (int i = 0; i < pattern.Length; i++)
            {
                char ch = pattern[i];
                if (ch == '~' && i + 1 < pattern.Length) { sb.Append(System.Text.RegularExpressions.Regex.Escape(pattern[++i].ToString())); continue; }
                if (ch == '*') sb.Append(".*");
                else if (ch == '?') sb.Append('.');
                else sb.Append(System.Text.RegularExpressions.Regex.Escape(ch.ToString()));
            }
            if (anchored) sb.Append('$');
            return sb.ToString();
        }

        /// <summary>COUNTIF criteria: a value, or text with a leading operator (">5", "&lt;&gt;x", "=a*").</summary>
        private static Func<object?, bool> Criteria(object? crit)
        {
            if (crit is double d) return v => v is double x ? x == d : v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && y == d;
            if (crit is bool b) return v => v is bool x && x == b;
            var text = Text(crit);
            string op = "=";
            foreach (var o in new[] { "<=", ">=", "<>", "<", ">", "=" })
                if (text.StartsWith(o, StringComparison.Ordinal)) { op = o; text = text.Substring(o.Length); break; }
            bool isNum = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
            if (op == "=" || op == "<>")
            {
                bool neg = op == "<>";
                if (text.Length == 0) return v => (v is null || v is "") != neg;
                if (isNum) return v => (v is double x && x == num) != neg;
                var rx = new System.Text.RegularExpressions.Regex(WildcardToRegex(text, anchored: true),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                return v => (v is string s && rx.IsMatch(s)) != neg;
            }
            if (isNum)
                return v => v is double x && op switch { "<" => x < num, ">" => x > num, "<=" => x <= num, _ => x >= num };
            return v => v is string s && op switch
            {
                "<" => string.Compare(s, text, StringComparison.OrdinalIgnoreCase) < 0,
                ">" => string.Compare(s, text, StringComparison.OrdinalIgnoreCase) > 0,
                "<=" => string.Compare(s, text, StringComparison.OrdinalIgnoreCase) <= 0,
                _ => string.Compare(s, text, StringComparison.OrdinalIgnoreCase) >= 0,
            };
        }

        /// <summary>Argument values; ranges flattened to their stored cells.</summary>
        private static IEnumerable<object?> Values(Node[] args, EvalContext c, bool flattenRanges)
        {
            foreach (var a in args)
            {
                var v = Eval(a, c);
                if (v is RangeValue rg && flattenRanges)
                {
                    foreach (var t in c.Source.GetValues(rg.R1, rg.C1, rg.R2, rg.C2)) yield return Normalize(t.Value, c);
                }
                else yield return Scalar(v, c);
            }
        }

        /// <summary>Numbers for SUM/AVERAGE/MIN/MAX/COUNT: numbers in ranges; direct arguments converted.</summary>
        private static IEnumerable<double> Numbers(Node[] args, EvalContext c)
        {
            foreach (var a in args)
            {
                var v = Eval(a, c);
                if (v is RangeValue rg)
                {
                    foreach (var t in c.Source.GetValues(rg.R1, rg.C1, rg.R2, rg.C2))
                    {
                        var n = Normalize(t.Value, c);
                        if (n is FormulaError e) throw new ErrorSignal(e);
                        if (n is double d) yield return d;
                    }
                }
                else
                {
                    var s = Scalar(v, c);
                    if (s is null) continue;
                    var n = ToNumber(s);
                    if (n is FormulaError e) throw new ErrorSignal(e);
                    yield return (double)n;
                }
            }
        }

        private static object Aggregate(Node[] args, EvalContext c, Func<IEnumerable<double>, double> f, bool countOnly)
            => f(Numbers(args, c).ToList());
    }

    // ---- Parser ----------------------------------------------------------------------

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; }

        private void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Peek(string t) { Ws(); return string.CompareOrdinal(_s, _i, t, 0, t.Length) == 0; }
        private bool Accept(string t) { if (!Peek(t)) return false; _i += t.Length; return true; }

        public void ExpectEnd()
        {
            Ws();
            if (_i != _s.Length) throw new FormulaNotSupportedException($"Unexpected '{_s.Substring(_i)}'.");
        }

        public Node ParseExpression() => Comparison();

        private Node Comparison()
        {
            var left = Concat();
            while (true)
            {
                string? op = Accept("<>") ? "<>" : Accept("<=") ? "<=" : Accept(">=") ? ">=" : Accept("=") ? "=" : Accept("<") ? "<" : Accept(">") ? ">" : null;
                if (op is null) return left;
                left = new Binary(op, left, Concat());
            }
        }

        private Node Concat()
        {
            var left = Additive();
            while (Accept("&")) left = new Binary("&", left, Additive());
            return left;
        }

        private Node Additive()
        {
            var left = Multiplicative();
            while (true)
            {
                if (Accept("+")) left = new Binary("+", left, Multiplicative());
                else if (Accept("-")) left = new Binary("-", left, Multiplicative());
                else return left;
            }
        }

        private Node Multiplicative()
        {
            var left = Power();
            while (true)
            {
                if (Accept("*")) left = new Binary("*", left, Power());
                else if (Accept("/")) left = new Binary("/", left, Power());
                else return left;
            }
        }

        private Node Power()
        {
            var left = UnaryNode();
            while (Accept("^")) left = new Binary("^", left, UnaryNode());
            return left;
        }

        private Node UnaryNode()
        {
            if (Accept("-")) return new Unary('-', UnaryNode());
            if (Accept("+")) return new Unary('+', UnaryNode());
            var n = Primary();
            while (Accept("%")) n = new Percent(n);
            return n;
        }

        private Node Primary()
        {
            Ws();
            if (_i >= _s.Length) throw new FormulaNotSupportedException("Unexpected end of formula.");
            char ch = _s[_i];
            if (ch == '(')
            {
                _i++;
                var e = ParseExpression();
                if (!Accept(")")) throw new FormulaNotSupportedException("Missing ')'.");
                return e;
            }
            if (ch == '"')
            {
                var sb = new StringBuilder();
                _i++;
                while (_i < _s.Length)
                {
                    if (_s[_i] == '"')
                    {
                        if (_i + 1 < _s.Length && _s[_i + 1] == '"') { sb.Append('"'); _i += 2; continue; }
                        _i++;
                        return new Lit(sb.ToString());
                    }
                    sb.Append(_s[_i++]);
                }
                throw new FormulaNotSupportedException("Unterminated string.");
            }
            if (ch == '#')
            {
                foreach (var lit in FormulaError.Literals)
                    if (string.CompareOrdinal(_s, _i, lit, 0, lit.Length) == 0) { _i += lit.Length; return new Lit(new FormulaError(lit)); }
                throw new FormulaNotSupportedException("Unknown error literal.");
            }
            if (char.IsDigit(ch) || ch == '.')
            {
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                // Row range "1:5"
                if (_i < _s.Length && _s[_i] == ':' && !_s.Substring(start, _i - start).Contains('.'))
                    return RangeFrom(start);
                if (_i < _s.Length && (_s[_i] is 'e' or 'E'))
                {
                    int save = _i++;
                    if (_i < _s.Length && _s[_i] is '+' or '-') _i++;
                    if (_i < _s.Length && char.IsDigit(_s[_i])) { while (_i < _s.Length && char.IsDigit(_s[_i])) _i++; }
                    else _i = save;
                }
                return new Lit(double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            if (ch == '\'' || ch == '[') throw new FormulaNotSupportedException("References to other sheets or workbooks are not evaluated.");
            if (char.IsLetter(ch) || ch == '$' || ch == '_')
            {
                int start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] is '$' or '_' or '.')) _i++;
                var token = _s.Substring(start, _i - start);
                Ws();
                if (_i < _s.Length && _s[_i] == '!') throw new FormulaNotSupportedException("References to other sheets are not evaluated.");
                if (_i < _s.Length && _s[_i] == '(')
                {
                    _i++;
                    var name = token.ToUpperInvariant();
                    if (name.StartsWith("_XLFN.", StringComparison.Ordinal)) name = name.Substring(6);
                    var args = new List<Node>();
                    if (!Accept(")"))
                    {
                        do { args.Add(ParseExpression()); } while (Accept(","));
                        if (!Accept(")")) throw new FormulaNotSupportedException("Missing ')'.");
                    }
                    return new Call(name, args.ToArray());
                }
                var upper = token.ToUpperInvariant();
                if (upper == "TRUE") return new Lit(true);
                if (upper == "FALSE") return new Lit(false);
                if (_i < _s.Length && _s[_i] == ':') return RangeFrom(start);
                if (FormulaText.TryCellRef(token, out bool ca, out int col, out bool ra, out int row))
                    return new RefNode(new RefBox(row, col, row, col, ra, ca, ra, ca, false, false));
                throw new FormulaNotSupportedException($"Name '{token}' is not evaluated.");
            }
            throw new FormulaNotSupportedException($"Unexpected '{ch}'.");
        }

        /// <summary>Parses "A1:B5", "A:C" or "1:5" starting at <paramref name="start"/>.</summary>
        private Node RangeFrom(int start)
        {
            int colon = _s.IndexOf(':', start);
            var left = _s.Substring(start, colon - start).Trim();
            _i = colon + 1;
            Ws();
            int rs = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '$')) _i++;
            var right = _s.Substring(rs, _i - rs);
            if (FormulaText.TryCellRef(left, out bool ca1, out int c1, out bool ra1, out int r1)
                && FormulaText.TryCellRef(right, out bool ca2, out int c2, out bool ra2, out int r2))
                return new RefNode(new RefBox(r1, c1, r2, c2, ra1, ca1, ra2, ca2, false, false));
            if (ColumnOnly(left, out var lc, out var lca) && ColumnOnly(right, out var rc, out var rca))
                return new RefNode(new RefBox(1, lc, 1048576, rc, true, lca, true, rca, true, false));
            if (RowOnly(left, out var lr, out var lra) && RowOnly(right, out var rr, out var rra))
                return new RefNode(new RefBox(lr, 1, rr, 16384, lra, true, rra, true, false, true));
            throw new FormulaNotSupportedException($"Range '{left}:{right}' is not evaluated.");
        }

        private static bool ColumnOnly(string t, out int col, out bool abs)
        {
            abs = t.StartsWith("$", StringComparison.Ordinal);
            var s = abs ? t.Substring(1) : t;
            col = 0;
            if (s.Length is 0 or > 3) return false;
            foreach (var ch in s)
            {
                if (!char.IsLetter(ch)) return false;
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return col <= 16384;
        }

        private static bool RowOnly(string t, out int row, out bool abs)
        {
            abs = t.StartsWith("$", StringComparison.Ordinal);
            var s = abs ? t.Substring(1) : t;
            return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out row) && row is >= 1 and <= 1048576;
        }
    }
}
