using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Cell values for <see cref="ConditionalFormatEvaluator"/>: the Workbook model provides one
/// (<see cref="Sheet.EvaluateConditionalFormats"/>); a streaming renderer implements it over
/// the rows it holds. Values are as the readers return them (double, string, bool, DateTime,
/// null for blank; formula cells give their cached result; error results are strings such as
/// "#N/A").
/// </summary>
public interface ICellValueSource
{
    /// <summary>The value of the cell at the 1-based row and column, or null when blank.</summary>
    object? GetValue(int row, int column);

    /// <summary>The non-blank cells inside the box (1-based, inclusive), in any order.</summary>
    IEnumerable<(int Row, int Column, object? Value)> GetValues(int firstRow, int firstColumn, int lastRow, int lastColumn);
}

/// <summary>Options for <see cref="ConditionalFormatEvaluator"/>.</summary>
public sealed class ConditionalFormatOptions
{
    /// <summary>"Today" for time-period rules and TODAY(). Default: <see cref="DateTime.Today"/>.</summary>
    public DateTime? Today { get; set; }

    /// <summary>The workbook uses the 1904 date system (dates compare as 1904 serials). The model sets this from the file.</summary>
    public bool? Date1904 { get; set; }
}

/// <summary>A data bar to draw in a cell (metadata; the renderer draws it).</summary>
public sealed class DataBarResult
{
    internal DataBarResult() { }

    /// <summary>The rule's data bar settings (colours, lengths, axis, direction, gradient, border).</summary>
    public DataBarInfo Bar { get; internal set; } = null!;

    /// <summary>The cell's value.</summary>
    public double Value { get; internal set; }

    /// <summary>The bar's minimum and maximum (resolved thresholds).</summary>
    public double Minimum { get; internal set; }
    public double Maximum { get; internal set; }

    /// <summary>Position of the value between minimum and maximum, 0–1 (clamped).</summary>
    public double Fraction { get; internal set; }

    /// <summary>Bar length in percent of the cell width: MinLength + Fraction × (MaxLength − MinLength).</summary>
    public double LengthPercent { get; internal set; }

    /// <summary>True for a negative value (Excel 2010 bars draw these from the axis, in the negative colour).</summary>
    public bool IsNegative { get; internal set; }

    /// <summary>The colour to fill the bar with: the negative colour for negative values when set, else the bar colour.</summary>
    public string? FillColor { get; internal set; }
}

/// <summary>An icon to draw in a cell (metadata).</summary>
public sealed class IconResult
{
    internal IconResult() { }

    /// <summary>Excel's icon set name, e.g. "3TrafficLights1".</summary>
    public string IconSet { get; internal set; } = string.Empty;

    /// <summary>0-based icon index within the set as Excel orders it (0 = the first icon, e.g. red for traffic lights), after Reverse.</summary>
    public int Index { get; internal set; }

    /// <summary>Number of icons in the set.</summary>
    public int Count { get; internal set; }

    /// <summary>False when only the icon (not the value) is shown.</summary>
    public bool ShowValue { get; internal set; } = true;
}

/// <summary>What the conditional formats change for one cell.</summary>
public sealed class ConditionalFormatResult
{
    internal ConditionalFormatResult() { }

    /// <summary>Solid fill colour from a matching rule or a colour scale, "RRGGBB", or null.</summary>
    public string? FillColor { get; internal set; }

    /// <summary>Pattern fill from a matching rule (with <see cref="PatternColor"/>), or null.</summary>
    public FillPattern? FillPattern { get; internal set; }
    public string? PatternColor { get; internal set; }

    public string? FontColor { get; internal set; }
    public bool? Bold { get; internal set; }
    public bool? Italic { get; internal set; }
    public UnderlineStyle? Underline { get; internal set; }
    public bool? Strikethrough { get; internal set; }

    /// <summary>Number format code from a matching rule, or null.</summary>
    public string? NumberFormat { get; internal set; }

    public Border? BorderTop { get; internal set; }
    public Border? BorderBottom { get; internal set; }
    public Border? BorderLeft { get; internal set; }
    public Border? BorderRight { get; internal set; }

    /// <summary>A data bar to draw, or null.</summary>
    public DataBarResult? DataBar { get; internal set; }

    /// <summary>An icon to draw, or null.</summary>
    public IconResult? Icon { get; internal set; }

    /// <summary>Rules that matched and contributed, highest priority first.</summary>
    public IReadOnlyList<ConditionalRule> MatchedRules => _matched;
    internal readonly List<ConditionalRule> _matched = new();

    /// <summary>Rules covering the cell that could not be evaluated (unsupported functions, other-sheet references, defined names, ...). Excel might format the cell through them.</summary>
    public IReadOnlyList<ConditionalRule> NotEvaluated => _notEvaluated;
    internal readonly List<ConditionalRule> _notEvaluated = new();

    /// <summary>True if no rule changed anything (no matches; there may still be <see cref="NotEvaluated"/> rules).</summary>
    public bool IsEmpty => _matched.Count == 0;

    /// <summary>The cell's own style with the conditional changes applied (as Excel shows it).</summary>
    public CellStyle ApplyTo(CellStyle baseStyle)
    {
        var s = baseStyle;
        if (FillColor is not null && FillPattern is null)
            s = s with { FillColor = FillColor, FillPattern = Excel.FillPattern.Solid, FillForegroundColor = FillColor };
        if (FillPattern is FillPattern fp)
            s = s with { FillColor = null, FillPattern = fp, FillForegroundColor = PatternColor, FillBackgroundColor = FillColor };
        if (FontColor is not null) s = s with { FontColor = FontColor };
        if (Bold is bool b) s = s with { Bold = b };
        if (Italic is bool i) s = s with { Italic = i };
        if (Underline is UnderlineStyle u) s = s with { Underline = u != UnderlineStyle.None, UnderlineKind = u };
        if (Strikethrough is bool st) s = s with { Strikethrough = st };
        if (NumberFormat is not null) s = s with { NumberFormat = NumberFormat, NumberFormatId = null };
        if (BorderTop is Border bt) s = s with { BorderTop = bt };
        if (BorderBottom is Border bb) s = s with { BorderBottom = bb };
        if (BorderLeft is Border bl) s = s with { BorderLeft = bl };
        if (BorderRight is Border br) s = s with { BorderRight = br };
        return s;
    }
}

/// <summary>
/// Evaluates a sheet's conditional formats per cell, as Excel shows them: rules in priority
/// order, a property set by a higher-priority rule wins, and a matching rule with StopIfTrue
/// stops lower-priority rules. Values come from an <see cref="ICellValueSource"/>.
///
/// Evaluated: CellIs (operands may be numbers, text, cell references or simple expressions);
/// Expression rules (comparisons, arithmetic, &amp;, %, and AND, OR, NOT, IF, ISBLANK,
/// ISNUMBER, ISTEXT, ISNONTEXT, ISLOGICAL, ISERROR, ISERR, ISNA, ISEVEN, ISODD, ROW, COLUMN, MOD,
/// ABS, INT, ROUND, ROUNDDOWN, ROUNDUP, FLOOR, LEN, LEFT, RIGHT, MID, UPPER, LOWER, TRIM, SEARCH,
/// FIND, EXACT, SUM, AVERAGE, MIN, MAX, COUNT, COUNTA, COUNTBLANK, COUNTIF, TODAY, NOW, DATE,
/// YEAR, MONTH, DAY, WEEKDAY, EDATE); 2- and 3-colour scales; data bars and icon sets (as
/// metadata); top/bottom N and N%; above/below average (standard deviations use the sample
/// standard deviation); duplicate/unique values; contains/not contains/begins/ends text;
/// blanks; errors; time periods.
///
/// Not evaluated (reported in <see cref="ConditionalFormatResult.NotEvaluated"/>): formulas with
/// other functions, references to other sheets or workbooks, defined names, structured table
/// references, array formulas; table styles. Errors are recognised from error-literal strings
/// ("#N/A", ...), which is how the readers return them. Thresholds and range statistics are
/// computed once per rule; build a new evaluator when values change.
/// </summary>
public sealed class ConditionalFormatEvaluator
{
    private readonly List<RuleState> _rules = new();
    private readonly ICellValueSource _source;
    private readonly DateTime _today;
    private readonly bool _date1904;

    public ConditionalFormatEvaluator(IEnumerable<ConditionalFormatting> formats, CellStyleTable? styles,
                                      ICellValueSource values, ConditionalFormatOptions? options = null)
    {
        if (formats is null) throw new ArgumentNullException(nameof(formats));
        _source = values ?? throw new ArgumentNullException(nameof(values));
        _today = (options?.Today ?? DateTime.Today).Date;
        _date1904 = options?.Date1904 ?? (values is SheetValueSource m && m.Date1904);
        int order = 0;
        foreach (var f in formats)
        {
            var boxes = f.Ranges.Select(ParseBox).Where(b => b is not null).Select(b => b!.Value).ToArray();
            if (boxes.Length == 0) continue;
            foreach (var r in f.Rules)
                _rules.Add(new RuleState(r, boxes, order++));
        }
        // Priority order; file order breaks ties.
        _rules.Sort((a, b) => a.Rule.Priority != b.Rule.Priority
            ? (a.Rule.Priority == 0 ? int.MaxValue : a.Rule.Priority).CompareTo(b.Rule.Priority == 0 ? int.MaxValue : b.Rule.Priority)
            : a.Order.CompareTo(b.Order));
    }

    /// <summary>
    /// What the conditional formats change for the cell at the 1-based row and column; null
    /// when no rule covers the cell.
    /// </summary>
    public ConditionalFormatResult? Evaluate(int row, int column)
    {
        ConditionalFormatResult? res = null;
        object? value = null;
        bool valueRead = false;
        foreach (var st in _rules)
        {
            if (!st.Covers(row, column)) continue;
            res ??= new ConditionalFormatResult();
            if (!valueRead) { value = MiniFormula.Normalize(_source.GetValue(row, column), _date1904); valueRead = true; }
            bool matched;
            try
            {
                matched = Apply(st, row, column, value, res);
            }
            catch (FormulaNotSupportedException)
            {
                res._notEvaluated.Add(st.Rule);
                continue;
            }
            if (matched)
            {
                res._matched.Add(st.Rule);
                if (st.Rule.StopIfTrue) break;
            }
        }
        return res;
    }

    // ---- Rule application -----------------------------------------------------------------

    private bool Apply(RuleState st, int row, int col, object? v, ConditionalFormatResult res)
    {
        var r = st.Rule;
        switch (r.Type)
        {
            case ConditionalRuleType.ColorScale:
            {
                if (v is not double d || r.ColorScale is null) return false;
                var color = st.ScaleColor(d, this);
                if (color is null) return false;
                if (res.FillColor is null && res.FillPattern is null) res.FillColor = color;
                return true;
            }
            case ConditionalRuleType.DataBar:
            {
                if (v is not double d || r.DataBar is null) return false;
                if (res.DataBar is null) res.DataBar = st.Bar(d, this);
                return true;
            }
            case ConditionalRuleType.IconSet:
            {
                if (v is not double d || r.IconSet is null) return false;
                if (res.Icon is null) res.Icon = st.Icon(d, this);
                return true;
            }
        }

        if (!Matches(st, row, col, v)) return false;
        if (r.Style is { } s) Merge(res, s);
        return true;
    }

    private static void Merge(ConditionalFormatResult res, DifferentialStyle s)
    {
        if (res.FillColor is null && res.FillPattern is null)
        {
            if (s.FillPattern is FillPattern fp && fp != FillPattern.Solid) { res.FillPattern = fp; res.PatternColor = s.PatternColor; res.FillColor = s.FillColor; }
            else if (s.FillColor is not null) res.FillColor = s.FillColor;
        }
        res.FontColor ??= s.FontColor;
        res.Bold ??= s.Bold;
        res.Italic ??= s.Italic;
        res.Underline ??= s.Underline;
        res.Strikethrough ??= s.Strikethrough;
        res.NumberFormat ??= s.NumberFormat;
        res.BorderTop ??= s.BorderTop;
        res.BorderBottom ??= s.BorderBottom;
        res.BorderLeft ??= s.BorderLeft;
        res.BorderRight ??= s.BorderRight;
    }

    private bool Matches(RuleState st, int row, int col, object? v)
    {
        var r = st.Rule;
        switch (r.Type)
        {
            case ConditionalRuleType.CellIs:
            {
                if (v is FormulaError || r.Operator is null || r.Formulas.Count == 0) return false;
                var a = st.EvalFormula(0, row, col, this);
                if (a is FormulaError) return false;
                int c1 = MiniFormula.Compare(v, a);
                switch (r.Operator.Value)
                {
                    case ComparisonOp.Equal: return c1 == 0;
                    case ComparisonOp.NotEqual: return c1 != 0;
                    case ComparisonOp.GreaterThan: return c1 > 0;
                    case ComparisonOp.GreaterThanOrEqual: return c1 >= 0;
                    case ComparisonOp.LessThan: return c1 < 0;
                    case ComparisonOp.LessThanOrEqual: return c1 <= 0;
                }
                if (r.Formulas.Count < 2) throw new FormulaNotSupportedException("Between needs two formulas.");
                var b = st.EvalFormula(1, row, col, this);
                if (b is FormulaError) return false;
                // Excel orders the bounds itself.
                if (MiniFormula.Compare(a, b) > 0) (a, b) = (b, a);
                bool inside = MiniFormula.Compare(v, a) >= 0 && MiniFormula.Compare(v, b) <= 0;
                return r.Operator == ComparisonOp.Between ? inside : !inside;
            }
            case ConditionalRuleType.Expression:
            {
                if (r.Formulas.Count == 0) return false;
                var x = st.EvalFormula(0, row, col, this);
                return x is not FormulaError && MiniFormula.Truthy(x);
            }
            case ConditionalRuleType.Top10:
                return v is double d && st.InTop(d, this);
            case ConditionalRuleType.AboveAverage:
                return v is double d2 && st.AboveAverage(d2, this);
            case ConditionalRuleType.DuplicateValues:
                return v is not null && v is not FormulaError && !(v is string es && es.Length == 0) && st.Occurrences(v, this) > 1;
            case ConditionalRuleType.UniqueValues:
                return v is not null && v is not FormulaError && !(v is string es2 && es2.Length == 0) && st.Occurrences(v, this) == 1;
            case ConditionalRuleType.ContainsText:
                return v is not FormulaError && MiniFormula.Text(v).IndexOf(r.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
            case ConditionalRuleType.NotContainsText:
                return v is not FormulaError && MiniFormula.Text(v).IndexOf(r.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase) < 0;
            case ConditionalRuleType.BeginsWith:
                return v is not FormulaError && MiniFormula.Text(v).StartsWith(r.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case ConditionalRuleType.EndsWith:
                return v is not FormulaError && MiniFormula.Text(v).EndsWith(r.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case ConditionalRuleType.ContainsBlanks:
                return v is null || (v is string s && s.Trim().Length == 0);
            case ConditionalRuleType.NotContainsBlanks:
                return !(v is null || (v is string s2 && s2.Trim().Length == 0));
            case ConditionalRuleType.ContainsErrors:
                return v is FormulaError;
            case ConditionalRuleType.NotContainsErrors:
                return v is not FormulaError;
            case ConditionalRuleType.TimePeriod:
                return v is double serial && r.TimePeriod is ConditionalTimePeriod p && InPeriod(serial, p);
        }
        return false;
    }

    private bool InPeriod(double serial, ConditionalTimePeriod p)
    {
        DateTime d;
        try { d = DateTime.FromOADate(serial + (_date1904 ? 1462 : 0)).Date; }
        catch (ArgumentException) { return false; }
        var t = _today;
        DateTime WeekStart(DateTime x) => x.AddDays(-(int)x.DayOfWeek);   // weeks start on Sunday
        switch (p)
        {
            case ConditionalTimePeriod.Today: return d == t;
            case ConditionalTimePeriod.Yesterday: return d == t.AddDays(-1);
            case ConditionalTimePeriod.Tomorrow: return d == t.AddDays(1);
            case ConditionalTimePeriod.Last7Days: return d <= t && d > t.AddDays(-7);
            case ConditionalTimePeriod.ThisMonth: return d.Year == t.Year && d.Month == t.Month;
            case ConditionalTimePeriod.LastMonth: { var m = t.AddMonths(-1); return d.Year == m.Year && d.Month == m.Month; }
            case ConditionalTimePeriod.NextMonth: { var m = t.AddMonths(1); return d.Year == m.Year && d.Month == m.Month; }
            case ConditionalTimePeriod.ThisWeek: return WeekStart(d) == WeekStart(t);
            case ConditionalTimePeriod.LastWeek: return WeekStart(d) == WeekStart(t).AddDays(-7);
            case ConditionalTimePeriod.NextWeek: return WeekStart(d) == WeekStart(t).AddDays(7);
        }
        return false;
    }

    internal MiniFormula.EvalContext Context(int row, int col, int anchorRow, int anchorCol) => new()
    {
        Source = _source, Row = row, Column = col, AnchorRow = anchorRow, AnchorColumn = anchorCol,
        Today = _today, Date1904 = _date1904,
    };

    internal IEnumerable<object?> RangeValues(Box[] boxes)
    {
        var seen = boxes.Length > 1 ? new HashSet<(int, int)>() : null;
        foreach (var b in boxes)
            foreach (var t in _source.GetValues(b.R1, b.C1, b.R2, b.C2))
            {
                if (seen is not null && !seen.Add((t.Row, t.Column))) continue;
                yield return MiniFormula.Normalize(t.Value, _date1904);
            }
    }

    // ---- Ranges ----------------------------------------------------------------------------

    internal readonly record struct Box(int R1, int C1, int R2, int C2)
    {
        public bool Contains(int r, int c) => r >= R1 && r <= R2 && c >= C1 && c <= C2;
    }

    internal static Box? ParseBox(string range)
    {
        var t = range.Replace("$", "").Trim().ToUpperInvariant();
        var parts = t.Split(':');
        static (int R, int C)? Cell(string s) => FormulaText.TryCellRef(s, out _, out var c, out _, out var r) ? (r, c) : null;
        static int? Col(string s)
        {
            if (s.Length is 0 or > 3) return null;
            int c = 0;
            foreach (var ch in s) { if (ch < 'A' || ch > 'Z') return null; c = c * 26 + (ch - 'A' + 1); }
            return c;
        }
        if (parts.Length == 1) return Cell(parts[0]) is { } one ? new Box(one.R, one.C, one.R, one.C) : null;
        if (parts.Length != 2) return null;
        if (Cell(parts[0]) is { } a && Cell(parts[1]) is { } b)
            return new Box(Math.Min(a.R, b.R), Math.Min(a.C, b.C), Math.Max(a.R, b.R), Math.Max(a.C, b.C));
        if (Col(parts[0]) is int c1 && Col(parts[1]) is int c2) return new Box(1, Math.Min(c1, c2), 1048576, Math.Max(c1, c2));
        if (int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r1)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var r2))
            return new Box(Math.Min(r1, r2), 1, Math.Max(r1, r2), 16384);
        return null;
    }

    // ---- Per-rule state -----------------------------------------------------------------

    private sealed class RuleState
    {
        public RuleState(ConditionalRule rule, Box[] boxes, int order)
        {
            Rule = rule; Boxes = boxes; Order = order;
            AnchorRow = boxes[0].R1; AnchorCol = boxes[0].C1;
        }

        public ConditionalRule Rule { get; }
        public Box[] Boxes { get; }
        public int Order { get; }
        public int AnchorRow { get; }
        public int AnchorCol { get; }

        private MiniFormula?[]? _parsed;
        private FormulaNotSupportedException? _parseError;
        private double[]? _numbers;      // numeric values in the ranges, ascending
        private Dictionary<string, int>? _counts;

        public bool Covers(int r, int c)
        {
            foreach (var b in Boxes) if (b.Contains(r, c)) return true;
            return false;
        }

        public object? EvalFormula(int index, int row, int col, ConditionalFormatEvaluator ev)
        {
            if (_parseError is not null) throw _parseError;
            if (_parsed is null)
            {
                try
                {
                    if (FormulaText.HasBracketReference(string.Join(",", Rule.Formulas)))
                        throw new FormulaNotSupportedException("Table or external references are not evaluated.");
                    _parsed = Rule.Formulas.Select(MiniFormula.Parse).ToArray();
                }
                catch (FormulaNotSupportedException ex) { _parseError = ex; throw; }
            }
            if (index >= _parsed.Length) throw new FormulaNotSupportedException("Missing formula.");
            return _parsed[index]!.Evaluate(ev.Context(row, col, AnchorRow, AnchorCol));
        }

        private double[] Numbers(ConditionalFormatEvaluator ev)
        {
            if (_numbers is not null) return _numbers;
            var list = new List<double>();
            foreach (var v in ev.RangeValues(Boxes)) if (v is double d) list.Add(d);
            list.Sort();
            return _numbers = list.ToArray();
        }

        public int Occurrences(object v, ConditionalFormatEvaluator ev)
        {
            if (_counts is null)
            {
                _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var x in ev.RangeValues(Boxes))
                {
                    if (x is null || x is FormulaError || x is string { Length: 0 }) continue;
                    var k = Key(x);
                    _counts[k] = _counts.TryGetValue(k, out var n) ? n + 1 : 1;
                }
            }
            return _counts.TryGetValue(Key(v), out var c) ? c : 0;
        }

        private static string Key(object v) => v switch
        {
            double d => "n:" + d.ToString("R", CultureInfo.InvariantCulture),
            bool b => "b:" + b,
            _ => "s:" + MiniFormula.Text(v),
        };

        public bool InTop(double d, ConditionalFormatEvaluator ev)
        {
            var nums = Numbers(ev);
            if (nums.Length == 0) return false;
            int rank = Math.Max(1, Rule.Rank ?? 10);
            int n = Rule.Percent ? Math.Max(1, (int)Math.Floor(nums.Length * rank / 100.0)) : Math.Min(rank, nums.Length);
            return Rule.Bottom ? d <= nums[n - 1] : d >= nums[nums.Length - n];
        }

        public bool AboveAverage(double d, ConditionalFormatEvaluator ev)
        {
            var nums = Numbers(ev);
            if (nums.Length == 0) return false;
            double avg = nums.Average();
            double limit = avg;
            if (Rule.StdDev is int k && k > 0)
            {
                if (nums.Length < 2) return false;
                double sd = Math.Sqrt(nums.Sum(x => (x - avg) * (x - avg)) / (nums.Length - 1));
                limit = Rule.AboveAverage ? avg + k * sd : avg - k * sd;
            }
            if (Rule.AboveAverage) return Rule.EqualAverage ? d >= limit : d > limit;
            return Rule.EqualAverage ? d <= limit : d < limit;
        }

        /// <summary>Resolves a threshold over the rule's numeric values (null when it cannot be resolved).</summary>
        private double? Threshold(ConditionalValue v, ConditionalFormatEvaluator ev)
        {
            var nums = Numbers(ev);
            if (nums.Length == 0) return null;
            double min = nums[0], max = nums[^1];
            switch (v.Type)
            {
                case ConditionalValueType.Min: return min;
                case ConditionalValueType.Max: return max;
                case ConditionalValueType.AutoMin: return Math.Min(0, min);
                case ConditionalValueType.AutoMax: return Math.Max(0, max);
                case ConditionalValueType.Percent:
                    return min + (max - min) * (ParseOrEval(v.Value, ev) ?? 0) / 100.0;
                case ConditionalValueType.Percentile:
                {
                    double p = Math.Clamp((ParseOrEval(v.Value, ev) ?? 0) / 100.0, 0, 1);
                    double pos = p * (nums.Length - 1);
                    int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
                    return nums[lo] + (nums[hi] - nums[lo]) * (pos - lo);
                }
                default:
                    return ParseOrEval(v.Value, ev);
            }
        }

        private double? ParseOrEval(string? s, ConditionalFormatEvaluator ev)
        {
            if (s is null) return null;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            var x = MiniFormula.Parse(s).Evaluate(ev.Context(AnchorRow, AnchorCol, AnchorRow, AnchorCol));
            return x is double r ? r : null;
        }

        private double?[]? _scale;

        public string? ScaleColor(double d, ConditionalFormatEvaluator ev)
        {
            var cs = Rule.ColorScale!;
            _scale ??= cs.Values.Select(v => Threshold(v, ev)).ToArray();
            if (_scale.Any(x => x is null)) return null;
            var t = _scale.Select(x => x!.Value).ToArray();
            if (d <= t[0]) return cs.Colors[0];
            if (d >= t[^1]) return cs.Colors[^1];
            for (int i = 0; i < t.Length - 1; i++)
            {
                if (d > t[i + 1]) continue;
                double span = t[i + 1] - t[i];
                double f = span <= 0 ? 1 : (d - t[i]) / span;
                return Lerp(cs.Colors[i], cs.Colors[i + 1], f);
            }
            return cs.Colors[^1];
        }

        private static string? Lerp(string? a, string? b, double f)
        {
            if (a is null || b is null) return a ?? b;
            int C(string h, int i) => Convert.ToInt32(h.Substring(i, 2), 16);
            string Ch(int i) => ((int)Math.Round(C(a, i) + (C(b, i) - C(a, i)) * f)).ToString("X2");
            return Ch(0) + Ch(2) + Ch(4);
        }

        public DataBarResult Bar(double d, ConditionalFormatEvaluator ev)
        {
            var bar = Rule.DataBar!;
            double min = Threshold(bar.Min, ev) ?? 0, max = Threshold(bar.Max, ev) ?? 0;
            double frac = max > min ? Math.Clamp((d - min) / (max - min), 0, 1) : 1;
            return new DataBarResult
            {
                Bar = bar,
                Value = d,
                Minimum = min,
                Maximum = max,
                Fraction = frac,
                LengthPercent = bar.MinLength + frac * (bar.MaxLength - bar.MinLength),
                IsNegative = d < 0,
                FillColor = d < 0 && bar.NegativeFillColor is not null ? bar.NegativeFillColor : bar.Color,
            };
        }

        public IconResult Icon(double d, ConditionalFormatEvaluator ev)
        {
            var ic = Rule.IconSet!;
            int count = ic.Values.Count > 0 ? ic.Values.Count : int.TryParse(ic.IconSet.Substring(0, 1), out var n) ? n : 3;
            var nums = Numbers(ev);
            double min = nums.Length > 0 ? nums[0] : 0, max = nums.Length > 0 ? nums[^1] : 0;
            int idx = 0;
            for (int i = 1; i < ic.Values.Count; i++)
            {
                var v = ic.Values[i];
                double? thr = v.Type switch
                {
                    ConditionalValueType.Percent => min + (max - min) * (ParseOrEval(v.Value, ev) ?? 0) / 100.0,
                    _ => Threshold(v, ev),
                };
                if (thr is null) continue;
                if (v.GreaterOrEqual ? d >= thr : d > thr) idx = i;
            }
            if (ic.Reverse) idx = count - 1 - idx;
            return new IconResult { IconSet = ic.IconSet, Index = idx, Count = count, ShowValue = ic.ShowValue };
        }
    }
}

/// <summary>Cell values of a <see cref="Sheet"/> for the evaluator.</summary>
internal sealed class SheetValueSource : ICellValueSource
{
    private readonly Sheet _sheet;
    public SheetValueSource(Sheet sheet) { _sheet = sheet; }

    public bool Date1904 => _sheet.Owner?.Date1904 ?? false;

    public object? GetValue(int row, int column)
        => _sheet.CellMap.TryGetValue((row, column), out var c) ? c.Value : null;

    public IEnumerable<(int Row, int Column, object? Value)> GetValues(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        foreach (var kv in _sheet.CellMap)
        {
            var (r, c) = kv.Key;
            if (r < firstRow || r > lastRow || c < firstColumn || c > lastColumn) continue;
            var v = kv.Value.Value;
            if (v is null) continue;
            yield return (r, c, v);
        }
    }
}
