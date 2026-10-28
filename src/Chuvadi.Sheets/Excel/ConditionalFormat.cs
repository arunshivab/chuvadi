using System;
using System.Globalization;

namespace Chuvadi.Sheets.Excel;

public enum ComparisonOp
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    NotBetween,
}

/// <summary>
/// A conditional formatting rule. Construct via the static helpers on
/// <see cref="ConditionalFormat"/>, or build a <see cref="ConditionalRule"/> directly for the
/// full option set. Every rule is a <see cref="ConditionalRule"/>.
/// </summary>
public abstract class ConditionalFormatRule
{
    private protected ConditionalFormatRule() { }
}

/// <summary>
/// Factory for the supported conditional-format rules.
/// </summary>
public static class ConditionalFormat
{
    public static class ColorScale
    {
        /// <summary>Red → white → green (low → mid → high).</summary>
        public static ConditionalFormatRule RedWhiteGreen =>
            Scale3("F8696B", "FFEB84", "63BE7B");

        /// <summary>Green → white → red (low → mid → high).</summary>
        public static ConditionalFormatRule GreenWhiteRed =>
            Scale3("63BE7B", "FFEB84", "F8696B");

        /// <summary>Blue → white → red.</summary>
        public static ConditionalFormatRule BlueWhiteRed =>
            Scale3("5A8AC6", "FCFCFF", "F8696B");

        /// <summary>Custom three-color scale.</summary>
        public static ConditionalFormatRule Custom(string minColor, string midColor, string maxColor)
            => Scale3(CellStyleBuilder.NormalizeHex(minColor)!, CellStyleBuilder.NormalizeHex(midColor)!, CellStyleBuilder.NormalizeHex(maxColor)!);

        /// <summary>Custom two-colour scale (lowest value → highest value).</summary>
        public static ConditionalFormatRule Custom(string minColor, string maxColor)
            => new ConditionalRule(ConditionalRuleType.ColorScale)
            {
                Priority = 1,
                ColorScale = new ColorScaleInfo(
                    new[] { new ConditionalValue(ConditionalValueType.Min), new ConditionalValue(ConditionalValueType.Max) },
                    new[] { CellStyleBuilder.NormalizeHex(minColor), CellStyleBuilder.NormalizeHex(maxColor) }),
            };

        private static ConditionalRule Scale3(string min, string mid, string max) => new(ConditionalRuleType.ColorScale)
        {
            Priority = 1,
            ColorScale = new ColorScaleInfo(
                new[] { new ConditionalValue(ConditionalValueType.Min), new ConditionalValue(ConditionalValueType.Percentile, "50"), new ConditionalValue(ConditionalValueType.Max) },
                new[] { min, mid, max }),
        };
    }

    public static class DataBar
    {
        public static ConditionalFormatRule Blue => Bar("638EC6");
        public static ConditionalFormatRule Green => Bar("63BE7B");
        public static ConditionalFormatRule Red => Bar("F8696B");
        public static ConditionalFormatRule Custom(string color) => Bar(CellStyleBuilder.NormalizeHex(color)!);

        private static ConditionalRule Bar(string color) => new(ConditionalRuleType.DataBar)
        {
            Priority = 1,
            DataBar = new DataBarInfo(new ConditionalValue(ConditionalValueType.Min), new ConditionalValue(ConditionalValueType.Max), color),
        };
    }

    /// <summary>
    /// Apply a style to cells matching a comparison against a fixed threshold. The style's
    /// visible parts (font colour, bold, italic, underline, strikethrough, fill, number format,
    /// borders) are written as a differential format (since 1.6.0; earlier versions wrote none,
    /// so the style did not show).
    ///
    /// <code>
    /// sheet.AddConditionalFormat("B2:B100",
    ///     ConditionalFormat.CellIs(ComparisonOp.GreaterThan, 100,
    ///         new CellStyleBuilder().Bold().Foreground("#C62828").Build()));
    /// </code>
    /// </summary>
    public static ConditionalFormatRule CellIs(ComparisonOp op, double threshold, CellStyle style)
        => CellIsCore(op, style, threshold.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>Compare against a fixed string threshold (use quoted Excel form like "\"Active\"").</summary>
    public static ConditionalFormatRule CellIs(ComparisonOp op, string formula, CellStyle style)
        => CellIsCore(op, style, formula);

    /// <summary>Apply style if value is between min and max (inclusive).</summary>
    public static ConditionalFormatRule Between(double min, double max, CellStyle style)
        => CellIsCore(ComparisonOp.Between, style,
            min.ToString("R", CultureInfo.InvariantCulture), max.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>Apply a style where a formula is true, e.g. <c>"$C2&gt;100"</c> (relative to the range's top-left cell).</summary>
    public static ConditionalFormatRule Expression(string formula, DifferentialStyle style)
    {
        if (string.IsNullOrEmpty(formula)) throw new ArgumentException("Formula required.", nameof(formula));
        var r = new ConditionalRule(ConditionalRuleType.Expression) { Style = style ?? throw new ArgumentNullException(nameof(style)) };
        r.Formulas.Add(formula);
        return r;
    }

    private static ConditionalRule CellIsCore(ComparisonOp op, CellStyle style, string f1, string? f2 = null)
    {
        var r = new ConditionalRule(ConditionalRuleType.CellIs) { Priority = 1, Operator = op, Style = DifferentialStyle.FromCellStyle(style) };
        r.Formulas.Add(f1);
        if (f2 is not null) r.Formulas.Add(f2);
        return r;
    }
}
