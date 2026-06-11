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
/// <see cref="ConditionalFormat"/>.
/// </summary>
public abstract class ConditionalFormatRule { }

internal sealed class ColorScaleRule : ConditionalFormatRule
{
    public required string MinColor { get; init; }
    public required string MidColor { get; init; }
    public required string MaxColor { get; init; }
}

internal sealed class DataBarRule : ConditionalFormatRule
{
    public required string Color { get; init; }
}

internal sealed class CellIsRule : ConditionalFormatRule
{
    public required ComparisonOp Operator { get; init; }
    public required string Formula1 { get; init; }
    public string? Formula2 { get; init; }
    public required CellStyle Style { get; init; }
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
            new ColorScaleRule { MinColor = "F8696B", MidColor = "FFEB84", MaxColor = "63BE7B" };

        /// <summary>Green → white → red (low → mid → high).</summary>
        public static ConditionalFormatRule GreenWhiteRed =>
            new ColorScaleRule { MinColor = "63BE7B", MidColor = "FFEB84", MaxColor = "F8696B" };

        /// <summary>Blue → white → red.</summary>
        public static ConditionalFormatRule BlueWhiteRed =>
            new ColorScaleRule { MinColor = "5A8AC6", MidColor = "FCFCFF", MaxColor = "F8696B" };

        /// <summary>Custom three-color scale.</summary>
        public static ConditionalFormatRule Custom(string minColor, string midColor, string maxColor)
            => new ColorScaleRule
            {
                MinColor = CellStyleBuilder.NormalizeHex(minColor)!,
                MidColor = CellStyleBuilder.NormalizeHex(midColor)!,
                MaxColor = CellStyleBuilder.NormalizeHex(maxColor)!,
            };
    }

    public static class DataBar
    {
        public static ConditionalFormatRule Blue => new DataBarRule { Color = "638EC6" };
        public static ConditionalFormatRule Green => new DataBarRule { Color = "63BE7B" };
        public static ConditionalFormatRule Red => new DataBarRule { Color = "F8696B" };
        public static ConditionalFormatRule Custom(string color)
            => new DataBarRule { Color = CellStyleBuilder.NormalizeHex(color)! };
    }

    /// <summary>
    /// Apply a style to cells matching a comparison against a fixed threshold.
    ///
    /// LIMITATION: the rule is written schema-valid and evaluates in Excel, but because this
    /// library does not emit dxf (differential formatting) records, the supplied
    /// <paramref name="style"/> will not render visibly through this rule in Excel.
    /// ColorScale and DataBar rules render fully.
    ///
    /// <code>
    /// sheet.AddConditionalFormat("B2:B100",
    ///     ConditionalFormat.CellIs(ComparisonOp.GreaterThan, 100,
    ///         new CellStyleBuilder().Bold().Foreground("#C62828").Build()));
    /// </code>
    /// </summary>
    public static ConditionalFormatRule CellIs(ComparisonOp op, double threshold, CellStyle style)
        => new CellIsRule
        {
            Operator = op,
            Formula1 = threshold.ToString("R", CultureInfo.InvariantCulture),
            Style = style,
        };

    /// <summary>Compare against a fixed string threshold (use quoted Excel form like "\"Active\"").</summary>
    public static ConditionalFormatRule CellIs(ComparisonOp op, string formula, CellStyle style)
        => new CellIsRule { Operator = op, Formula1 = formula, Style = style };

    /// <summary>Apply style if value is between min and max (inclusive).</summary>
    public static ConditionalFormatRule Between(double min, double max, CellStyle style)
        => new CellIsRule
        {
            Operator = ComparisonOp.Between,
            Formula1 = min.ToString("R", CultureInfo.InvariantCulture),
            Formula2 = max.ToString("R", CultureInfo.InvariantCulture),
            Style = style,
        };
}
