using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Excel;

/// <summary>Conditional formatting rule types (&lt;cfRule type&gt;).</summary>
public enum ConditionalRuleType
{
    /// <summary>Compare the cell's value with one or two formulas (<see cref="ConditionalRule.Operator"/>).</summary>
    CellIs = 0,
    /// <summary>Apply when a formula is true (relative to the range's top-left cell).</summary>
    Expression,
    ColorScale,
    DataBar,
    IconSet,
    /// <summary>Top or bottom N items or N percent.</summary>
    Top10,
    AboveAverage,
    DuplicateValues,
    UniqueValues,
    ContainsText,
    NotContainsText,
    BeginsWith,
    EndsWith,
    ContainsBlanks,
    NotContainsBlanks,
    ContainsErrors,
    NotContainsErrors,
    TimePeriod,
}

/// <summary>Date ranges for <see cref="ConditionalRuleType.TimePeriod"/> rules.</summary>
public enum ConditionalTimePeriod { Today = 0, Yesterday, Tomorrow, Last7Days, ThisMonth, LastMonth, NextMonth, ThisWeek, LastWeek, NextWeek }

/// <summary>How a colour-scale, data-bar or icon-set threshold is given (&lt;cfvo type&gt;).</summary>
public enum ConditionalValueType { Number = 0, Percent, Percentile, Min, Max, Formula, AutoMin, AutoMax }

/// <summary>A threshold of a colour scale, data bar or icon set (&lt;cfvo&gt;).</summary>
public sealed class ConditionalValue
{
    /// <summary>A threshold of the given type; <paramref name="value"/> is a number, a percent, or a formula, as the type needs.</summary>
    public ConditionalValue(ConditionalValueType type, string? value = null, bool greaterOrEqual = true)
    {
        Type = type; Value = value; GreaterOrEqual = greaterOrEqual;
    }

    public ConditionalValueType Type { get; }

    /// <summary>The number, percent (0–100), percentile (0–100) or formula, as stored; null for Min/Max/AutoMin/AutoMax.</summary>
    public string? Value { get; }

    /// <summary>For icon sets: the threshold is "&gt;=" (true, default) or "&gt;" (false).</summary>
    public bool GreaterOrEqual { get; }

    public override string ToString() => Value is null ? Type.ToString() : $"{Type}:{Value}";
}

/// <summary>A 2- or 3-colour scale (&lt;colorScale&gt;): one colour per threshold, low to high.</summary>
public sealed class ColorScaleInfo
{
    public ColorScaleInfo(IReadOnlyList<ConditionalValue> values, IReadOnlyList<string?> colors)
    {
        if (values is null || colors is null || values.Count != colors.Count || values.Count < 2 || values.Count > 3)
            throw new ArgumentException("A colour scale has 2 or 3 thresholds, each with a colour.");
        Values = values; Colors = colors;
    }

    public IReadOnlyList<ConditionalValue> Values { get; }

    /// <summary>"RRGGBB" per threshold (loaded theme/indexed colours are resolved); null when unresolvable.</summary>
    public IReadOnlyList<string?> Colors { get; }
}

/// <summary>Where a data bar's axis sits for negative values (Excel 2010 extension).</summary>
public enum DataBarAxisPosition { Automatic = 0, Middle, None }

/// <summary>Direction of data bars (Excel 2010 extension).</summary>
public enum DataBarDirection { Context = 0, LeftToRight, RightToLeft }

/// <summary>
/// A data bar (&lt;dataBar&gt;, plus the Excel 2010 &lt;x14:dataBar&gt; details when present).
/// Evaluation reports the bar as metadata (<see cref="DataBarResult"/>); drawing it is the renderer's job.
/// </summary>
public sealed class DataBarInfo
{
    public DataBarInfo(ConditionalValue min, ConditionalValue max, string? color)
    {
        Min = min ?? throw new ArgumentNullException(nameof(min));
        Max = max ?? throw new ArgumentNullException(nameof(max));
        Color = color;
    }

    public ConditionalValue Min { get; set; }
    public ConditionalValue Max { get; set; }

    /// <summary>Bar fill colour, "RRGGBB".</summary>
    public string? Color { get; set; }

    /// <summary>Show the cell value next to the bar (default true).</summary>
    public bool ShowValue { get; set; } = true;

    /// <summary>Shortest bar, percent of the cell width (default 10; Excel 2010 bars often use 0).</summary>
    public int MinLength { get; set; } = 10;

    /// <summary>Longest bar, percent of the cell width (default 90; Excel 2010 bars often use 100).</summary>
    public int MaxLength { get; set; } = 90;

    /// <summary>True if the file carries Excel 2010 data bar details (gradient, border, negative colours, axis).</summary>
    public bool HasExtension { get; set; }

    /// <summary>Gradient fill (default true; Excel 2010 solid bars set false).</summary>
    public bool Gradient { get; set; } = true;

    /// <summary>Bar border colour, or null for no border.</summary>
    public string? BorderColor { get; set; }

    /// <summary>Fill colour for negative values, or null (same as positive).</summary>
    public string? NegativeFillColor { get; set; }

    /// <summary>Border colour for negative values, or null.</summary>
    public string? NegativeBorderColor { get; set; }

    /// <summary>Axis colour, or null.</summary>
    public string? AxisColor { get; set; }

    public DataBarAxisPosition AxisPosition { get; set; }
    public DataBarDirection Direction { get; set; }
}

/// <summary>An icon set (&lt;iconSet&gt;): N icons chosen by thresholds.</summary>
public sealed class IconSetInfo
{
    public IconSetInfo(string iconSet, IReadOnlyList<ConditionalValue> values)
    {
        if (string.IsNullOrEmpty(iconSet)) throw new ArgumentException("Icon set name required.", nameof(iconSet));
        IconSet = iconSet;
        Values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>Excel's icon set name, e.g. "3TrafficLights1", "3Arrows", "5Rating" (default "3TrafficLights1").</summary>
    public string IconSet { get; }

    /// <summary>One threshold per icon, lowest first (the first one is the floor).</summary>
    public IReadOnlyList<ConditionalValue> Values { get; }

    /// <summary>Show the cell value next to the icon (default true).</summary>
    public bool ShowValue { get; set; } = true;

    /// <summary>Reverse the icon order.</summary>
    public bool Reverse { get; set; }
}

/// <summary>
/// The formatting a conditional rule applies (a differential format): only the parts that are
/// set override the cell's own style. Colours are "RRGGBB". Used both for loaded rules (resolved
/// from the file's &lt;dxf&gt;) and for rules written in code.
/// </summary>
public sealed class DifferentialStyle
{
    public string? FontColor { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public UnderlineStyle? Underline { get; set; }
    public bool? Strikethrough { get; set; }

    /// <summary>Background colour of a solid fill.</summary>
    public string? FillColor { get; set; }

    /// <summary>Pattern for a patterned fill (null = solid when <see cref="FillColor"/> is set).</summary>
    public FillPattern? FillPattern { get; set; }

    /// <summary>Pattern line colour for patterned fills.</summary>
    public string? PatternColor { get; set; }

    /// <summary>Number format code, or null.</summary>
    public string? NumberFormat { get; set; }

    public Border? BorderTop { get; set; }
    public Border? BorderBottom { get; set; }
    public Border? BorderLeft { get; set; }
    public Border? BorderRight { get; set; }

    internal bool IsEmpty => FontColor is null && Bold is null && Italic is null && Underline is null && Strikethrough is null
        && FillColor is null && FillPattern is null && PatternColor is null && NumberFormat is null
        && BorderTop is null && BorderBottom is null && BorderLeft is null && BorderRight is null;

    /// <summary>A differential style with the visible parts of a cell style (font colour, bold, italic, underline, fill, number format, borders).</summary>
    public static DifferentialStyle FromCellStyle(CellStyle s) => new()
    {
        FontColor = s.FontColor,
        Bold = s.Bold ? true : null,
        Italic = s.Italic ? true : null,
        Underline = s.UnderlineKind != UnderlineStyle.None ? s.UnderlineKind : s.Underline ? UnderlineStyle.Single : null,
        Strikethrough = s.Strikethrough ? true : null,
        FillColor = s.FillColor,
        NumberFormat = s.NumberFormat,
        BorderTop = s.BorderTop.Style != BorderStyle.None ? s.BorderTop : null,
        BorderBottom = s.BorderBottom.Style != BorderStyle.None ? s.BorderBottom : null,
        BorderLeft = s.BorderLeft.Style != BorderStyle.None ? s.BorderLeft : null,
        BorderRight = s.BorderRight.Style != BorderStyle.None ? s.BorderRight : null,
    };
}

/// <summary>
/// One conditional formatting rule (&lt;cfRule&gt;), as loaded or for writing with
/// <see cref="Sheet.AddConditionalFormat"/> / <see cref="SheetWriter.AddConditionalFormat"/>.
/// Which properties matter depends on <see cref="Type"/>.
/// </summary>
public sealed class ConditionalRule : ConditionalFormatRule
{
    public ConditionalRule(ConditionalRuleType type) { Type = type; }

    public ConditionalRuleType Type { get; }

    /// <summary>Priority (1 = evaluated first). 0 = assigned in order when saving.</summary>
    public int Priority { get; set; }

    /// <summary>When this rule applies, rules of lower priority are not applied to the cell.</summary>
    public bool StopIfTrue { get; set; }

    /// <summary>For <see cref="ConditionalRuleType.CellIs"/>: the comparison.</summary>
    public ComparisonOp? Operator { get; set; }

    /// <summary>
    /// The rule's formulas without '=' (CellIs: one, or two for Between/NotBetween; Expression:
    /// one). Relative references are relative to the top-left cell of the first range.
    /// </summary>
    public IList<string> Formulas { get; } = new List<string>();

    /// <summary>For the text rules: the text to look for.</summary>
    public string? Text { get; set; }

    /// <summary>For <see cref="ConditionalRuleType.TimePeriod"/>: the period.</summary>
    public ConditionalTimePeriod? TimePeriod { get; set; }

    /// <summary>For Top10: the number of items (or percent when <see cref="Percent"/>).</summary>
    public int? Rank { get; set; }

    /// <summary>For Top10: <see cref="Rank"/> is a percentage.</summary>
    public bool Percent { get; set; }

    /// <summary>For Top10: bottom instead of top.</summary>
    public bool Bottom { get; set; }

    /// <summary>For AboveAverage: above (true, default) or below the average.</summary>
    public bool AboveAverage { get; set; } = true;

    /// <summary>For AboveAverage: include values equal to the average.</summary>
    public bool EqualAverage { get; set; }

    /// <summary>For AboveAverage: standard deviations from the average (1–3), or null.</summary>
    public int? StdDev { get; set; }

    /// <summary>The formatting applied when the rule matches (CellIs, Expression, Top10, AboveAverage, text, blank, error, time and duplicate rules).</summary>
    public DifferentialStyle? Style { get; set; }

    public ColorScaleInfo? ColorScale { get; set; }
    public DataBarInfo? DataBar { get; set; }
    public IconSetInfo? IconSet { get; set; }

    /// <summary>The &lt;dxf&gt; index as loaded, or null. Index into <see cref="CellStyleTable.DifferentialFormats"/>.</summary>
    public int? DxfId { get; internal set; }

    /// <summary>The differential format exactly as loaded (theme colours unresolved), or null. <see cref="Style"/> is its resolved form.</summary>
    public DifferentialFormat? LoadedFormat { get; internal set; }

    /// <summary>The operator attribute as loaded when it is not a <see cref="ComparisonOp"/> (text rules: containsText, notContains, beginsWith, endsWith).</summary>
    internal string? RawOperator { get; set; }

    /// <summary>True if the rule was stored only in the Excel 2010 extension (&lt;x14:conditionalFormatting&gt;), e.g. one referring to another sheet; it is written back there.</summary>
    public bool FromExtension { get; set; }
}

/// <summary>A &lt;conditionalFormatting&gt; block: one or more ranges sharing a list of rules.</summary>
public sealed class ConditionalFormatting
{
    public ConditionalFormatting(IEnumerable<string> ranges, IEnumerable<ConditionalRule> rules)
    {
        if (ranges is null) throw new ArgumentNullException(nameof(ranges));
        if (rules is null) throw new ArgumentNullException(nameof(rules));
        Ranges = new List<string>(ranges);
        Rules = new List<ConditionalRule>(rules);
        if (Ranges.Count == 0) throw new ArgumentException("At least one range is required.", nameof(ranges));
    }

    /// <summary>The ranges the rules apply to ("A1:A10", "C2"), in stored order.</summary>
    public IReadOnlyList<string> Ranges { get; }

    /// <summary>The rules, in stored order (evaluation uses <see cref="ConditionalRule.Priority"/>).</summary>
    public IReadOnlyList<ConditionalRule> Rules { get; }

    /// <summary>True if the formatting belongs to a PivotTable.</summary>
    public bool Pivot { get; set; }

    /// <summary>"A1:A10 C2" — the ranges as Excel's sqref.</summary>
    public string SqRef => string.Join(" ", Ranges);
}
