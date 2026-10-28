using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Parses the structured worksheet elements the row enumerator hands over as whole subtrees:
/// &lt;sheetViews&gt;, &lt;conditionalFormatting&gt;, &lt;dataValidations&gt; and the worksheet
/// &lt;extLst&gt; (Excel 2010 conditional formats, data-bar details and data validations).
/// </summary>
internal static class SheetExtrasParser
{
    internal const string X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    internal const string XmNs = "http://schemas.microsoft.com/office/excel/2006/main";

    // ---- Sheet view -------------------------------------------------------------------

    public static SheetView? ParseSheetViews(XElement views)
    {
        var ns = views.Name.Namespace;
        var v = views.Element(ns + "sheetView");
        if (v is null) return null;
        var view = new SheetView
        {
            ShowGridLines = Bool(v, "showGridLines", true),
            ShowHeadings = Bool(v, "showRowColHeaders", true),
            ShowZeros = Bool(v, "showZeros", true),
            ShowRuler = Bool(v, "showRuler", true),
            ShowFormulas = Bool(v, "showFormulas", false),
            ShowOutlineSymbols = Bool(v, "showOutlineSymbols", true),
            ShowWhiteSpace = Bool(v, "showWhiteSpace", true),
            RightToLeft = Bool(v, "rightToLeft", false),
            TabSelected = Bool(v, "tabSelected", false),
            ViewMode = (string?)v.Attribute("view") switch
            {
                "pageBreakPreview" => SheetViewMode.PageBreakPreview,
                "pageLayout" => SheetViewMode.PageLayout,
                _ => SheetViewMode.Normal,
            },
            TopLeftCell = (string?)v.Attribute("topLeftCell"),
        };
        view.SetLoadedZoom(Int(v, "zoomScale") ?? 100, Int(v, "zoomScaleNormal"), Int(v, "zoomScalePageLayoutView"), Int(v, "zoomScaleSheetLayoutView"));
        if (v.Element(ns + "pane") is { } p)
        {
            view.Pane = new SheetPane
            {
                State = (string?)p.Attribute("state") switch
                {
                    "frozen" => PaneState.Frozen,
                    "frozenSplit" => PaneState.FrozenSplit,
                    _ => PaneState.Split,
                },
                XSplit = Dbl(p, "xSplit") ?? 0,
                YSplit = Dbl(p, "ySplit") ?? 0,
                TopLeftCell = (string?)p.Attribute("topLeftCell"),
                ActivePane = Pane((string?)p.Attribute("activePane")),
            };
        }
        foreach (var s in v.Elements(ns + "selection"))
            view.Selections.Add(new SheetSelection(Pane((string?)s.Attribute("pane")), (string?)s.Attribute("activeCell"), (string?)s.Attribute("sqref")));
        return view;
    }

    internal static PaneKind Pane(string? s) => s switch
    {
        "topRight" => PaneKind.TopRight,
        "bottomLeft" => PaneKind.BottomLeft,
        "bottomRight" => PaneKind.BottomRight,
        _ => PaneKind.TopLeft,
    };

    // ---- Conditional formatting -------------------------------------------------------

    /// <summary>Parses a main-namespace &lt;conditionalFormatting&gt;; rules carrying an x14 id are recorded for the extension pass.</summary>
    public static ConditionalFormatting ParseConditionalFormatting(XElement cf, CellStyleTable styles, Dictionary<string, ConditionalRule> byExtId)
    {
        var ns = cf.Name.Namespace;
        var rules = new List<ConditionalRule>();
        foreach (var r in cf.Elements(ns + "cfRule"))
        {
            var rule = ParseRule(r, ns, styles, inlineDxf: null);
            if (rule is null) continue;
            var extId = r.Descendants(XName.Get("id", X14Ns)).FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(extId)) byExtId[extId!] = rule;
            rules.Add(rule);
        }
        var fmt = new ConditionalFormatting(SplitRanges((string?)cf.Attribute("sqref")), rules)
        {
            Pivot = Bool(cf, "pivot", false),
        };
        return fmt;
    }

    private static ConditionalRule? ParseRule(XElement r, XNamespace ns, CellStyleTable styles, DifferentialFormat? inlineDxf)
    {
        var type = RuleType((string?)r.Attribute("type"));
        if (type is null) return null;
        var rule = new ConditionalRule(type.Value)
        {
            Priority = Int(r, "priority") ?? 0,
            StopIfTrue = Bool(r, "stopIfTrue", false),
            Operator = Op((string?)r.Attribute("operator")),
            Text = (string?)r.Attribute("text"),
            TimePeriod = Period((string?)r.Attribute("timePeriod")),
            Rank = Int(r, "rank"),
            Percent = Bool(r, "percent", false),
            Bottom = Bool(r, "bottom", false),
            AboveAverage = Bool(r, "aboveAverage", true),
            EqualAverage = Bool(r, "equalAverage", false),
            StdDev = Int(r, "stdDev"),
        };
        if (rule.Operator is null) rule.RawOperator = (string?)r.Attribute("operator");
        // Formulas: <formula> in the main namespace, <xm:f> in the extension.
        foreach (var f in r.Elements().Where(e => e.Name.LocalName is "formula" or "f"))
            rule.Formulas.Add(f.Value);

        DifferentialFormat? dxf = inlineDxf;
        if (dxf is null && Int(r, "dxfId") is int id)
        {
            rule.DxfId = id;
            if (id >= 0 && id < styles.DifferentialFormats.Count) dxf = styles.DifferentialFormats[id];
        }
        if (dxf is not null)
        {
            rule.LoadedFormat = dxf;
            rule.Style = ToStyle(dxf, styles);
        }

        var anyNs = r.Elements();
        if (anyNs.FirstOrDefault(e => e.Name.LocalName == "colorScale") is { } cs)
        {
            var vals = cs.Elements().Where(e => e.Name.LocalName == "cfvo").Select(Cfvo).ToList();
            var cols = cs.Elements().Where(e => e.Name.LocalName == "color").Select(c => styles.ResolveColor(StyleTableParser.ParseColor(c))).ToList();
            if (vals.Count is >= 2 and <= 3 && vals.Count == cols.Count) rule.ColorScale = new ColorScaleInfo(vals, cols);
        }
        if (anyNs.FirstOrDefault(e => e.Name.LocalName == "dataBar") is { } db)
        {
            var vals = db.Elements().Where(e => e.Name.LocalName == "cfvo").Select(Cfvo).ToList();
            var color = db.Elements().FirstOrDefault(e => e.Name.LocalName is "color" or "fillColor");
            rule.DataBar = new DataBarInfo(
                vals.Count > 0 ? vals[0] : new ConditionalValue(ConditionalValueType.Min),
                vals.Count > 1 ? vals[1] : new ConditionalValue(ConditionalValueType.Max),
                color is null ? null : styles.ResolveColor(StyleTableParser.ParseColor(color)))
            {
                ShowValue = Bool(db, "showValue", true),
                MinLength = Int(db, "minLength") ?? 10,
                MaxLength = Int(db, "maxLength") ?? 90,
            };
            if (db.Name.NamespaceName == X14Ns) ApplyDataBarExtension(rule.DataBar, db, styles);
        }
        if (anyNs.FirstOrDefault(e => e.Name.LocalName == "iconSet") is { } ic)
        {
            var vals = ic.Elements().Where(e => e.Name.LocalName == "cfvo").Select(Cfvo).ToList();
            rule.IconSet = new IconSetInfo((string?)ic.Attribute("iconSet") ?? "3TrafficLights1", vals)
            {
                ShowValue = Bool(ic, "showValue", true),
                Reverse = Bool(ic, "reverse", false),
            };
        }
        return rule;
    }

    private static void ApplyDataBarExtension(DataBarInfo bar, XElement db, CellStyleTable styles)
    {
        bar.HasExtension = true;
        bar.MinLength = Int(db, "minLength") ?? 10;
        bar.MaxLength = Int(db, "maxLength") ?? 90;
        bar.Gradient = Bool(db, "gradient", true);
        bar.AxisPosition = (string?)db.Attribute("axisPosition") switch { "middle" => DataBarAxisPosition.Middle, "none" => DataBarAxisPosition.None, _ => DataBarAxisPosition.Automatic };
        bar.Direction = (string?)db.Attribute("direction") switch { "leftToRight" => DataBarDirection.LeftToRight, "rightToLeft" => DataBarDirection.RightToLeft, _ => DataBarDirection.Context };
        bool border = Bool(db, "border", false);
        bool negSame = Bool(db, "negativeBarColorSameAsPositive", false);
        bool negBorderSame = Bool(db, "negativeBarBorderColorSameAsPositive", true);
        string? C(string local) => db.Elements().FirstOrDefault(e => e.Name.LocalName == local) is { } c ? styles.ResolveColor(StyleTableParser.ParseColor(c)) : null;
        if (C("fillColor") is { } fill) bar.Color = fill;
        bar.BorderColor = border ? C("borderColor") : null;
        bar.NegativeFillColor = negSame ? null : C("negativeFillColor");
        bar.NegativeBorderColor = border && !negBorderSame ? C("negativeBorderColor") : null;
        bar.AxisColor = C("axisColor");
        var vals = db.Elements().Where(e => e.Name.LocalName == "cfvo").Select(Cfvo).ToList();
        if (vals.Count > 0) bar.Min = vals[0];
        if (vals.Count > 1) bar.Max = vals[1];
    }

    private static ConditionalValue Cfvo(XElement c)
    {
        var t = (string?)c.Attribute("type") switch
        {
            "num" => ConditionalValueType.Number,
            "percent" => ConditionalValueType.Percent,
            "percentile" => ConditionalValueType.Percentile,
            "min" => ConditionalValueType.Min,
            "max" => ConditionalValueType.Max,
            "formula" => ConditionalValueType.Formula,
            "autoMin" => ConditionalValueType.AutoMin,
            "autoMax" => ConditionalValueType.AutoMax,
            _ => ConditionalValueType.Number,
        };
        // Excel 2010 cfvos carry the value in <xm:f> instead of val=.
        var val = (string?)c.Attribute("val") ?? c.Elements().FirstOrDefault(e => e.Name.LocalName == "f")?.Value;
        return new ConditionalValue(t, val, Bool(c, "gte", true));
    }

    /// <summary>A differential format with its colours resolved to RGB.</summary>
    public static DifferentialStyle ToStyle(DifferentialFormat dxf, CellStyleTable styles)
    {
        var s = new DifferentialStyle();
        if (dxf.Font is { } f)
        {
            s.FontColor = styles.ResolveColor(f.Color);
            if (f.Bold) s.Bold = true;
            if (f.Italic) s.Italic = true;
            if (f.Underline != UnderlineStyle.None) s.Underline = f.Underline;
            if (f.Strikethrough) s.Strikethrough = true;
        }
        if (dxf.Fill is { } fill)
        {
            // In a dxf, a solid fill's colour is in bgColor (an absent patternType means solid).
            if (fill.Pattern is null or FillPattern.Solid)
                s.FillColor = styles.ResolveColor(fill.BackgroundColor) ?? styles.ResolveColor(fill.ForegroundColor);
            else if (fill.Pattern != FillPattern.None)
            {
                s.FillPattern = fill.Pattern;
                s.FillColor = styles.ResolveColor(fill.BackgroundColor);
                s.PatternColor = styles.ResolveColor(fill.ForegroundColor);
            }
        }
        if (dxf.NumberFormatCode is { } code) s.NumberFormat = code;
        else if (dxf.NumberFormatId is int nid) s.NumberFormat = styles.GetNumberFormatCode(nid);
        if (dxf.Border is { } b)
        {
            Border? E(BorderEdge e) => e.Style == BorderStyle.None ? null : new Border(e.Style, styles.ResolveColor(e.Color));
            s.BorderTop = E(b.Top); s.BorderBottom = E(b.Bottom); s.BorderLeft = E(b.Left); s.BorderRight = E(b.Right);
        }
        return s;
    }

    // ---- Data validation ---------------------------------------------------------------

    public static IEnumerable<(string Range, DataValidation Validation)> ParseDataValidations(XElement dvs)
    {
        foreach (var d in dvs.Elements().Where(e => e.Name.LocalName == "dataValidation"))
        {
            string? F(string local)
            {
                var e = d.Elements().FirstOrDefault(x => x.Name.LocalName == local);
                if (e is null) return null;
                var f = e.Elements().FirstOrDefault(x => x.Name.LocalName == "f");   // x14: <x14:formula1><xm:f>
                return f?.Value ?? e.Value;
            }
            var sqref = (string?)d.Attribute("sqref") ?? d.Elements().FirstOrDefault(x => x.Name.LocalName == "sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref)) continue;
            var v = DataValidation.Loaded((string?)d.Attribute("type") ?? "none", (string?)d.Attribute("operator"), F("formula1"), F("formula2"));
            v.AllowBlank = Bool(d, "allowBlank", false);
            v.ShowDropdown = !Bool(d, "showDropDown", false);   // stored inverted: 1 = hide the arrow
            v.ShowErrorMessage = Bool(d, "showErrorMessage", false);
            v.ShowInputMessage = Bool(d, "showInputMessage", false);
            v.ErrorTitle = (string?)d.Attribute("errorTitle");
            v.ErrorMessage = (string?)d.Attribute("error");
            v.PromptTitle = (string?)d.Attribute("promptTitle");
            v.PromptMessage = (string?)d.Attribute("prompt");
            v.ImeMode = (string?)d.Attribute("imeMode");
            v.ErrorStyle = (string?)d.Attribute("errorStyle") switch
            {
                "warning" => DataValidationErrorStyle.Warning,
                "information" => DataValidationErrorStyle.Information,
                _ => DataValidationErrorStyle.Stop,
            };
            yield return (sqref!.Trim(), v);
        }
    }

    // ---- Worksheet extension list ----------------------------------------------------

    /// <summary>
    /// Reads the worksheet &lt;extLst&gt;: x14 conditional formats (data-bar details merged into the
    /// main rule they extend; rules found only here become <see cref="ConditionalRule.FromExtension"/>)
    /// and x14 data validations.
    /// </summary>
    public static void ParseExtLst(XElement extLst, CellStyleTable styles, Dictionary<string, ConditionalRule> byExtId,
                                   List<ConditionalFormatting> formats, List<(string, DataValidation)> validations)
    {
        XNamespace x14 = X14Ns, main = extLst.Name.Namespace;
        foreach (var cf in extLst.Descendants(x14 + "conditionalFormatting"))
        {
            var sqref = cf.Elements().FirstOrDefault(e => e.Name.LocalName == "sqref")?.Value;
            var extOnly = new List<ConditionalRule>();
            foreach (var r in cf.Elements(x14 + "cfRule"))
            {
                var id = (string?)r.Attribute("id");
                if (id is not null && byExtId.TryGetValue(id, out var mainRule))
                {
                    // Extends a main rule: today only data bars carry extra details.
                    if (mainRule.DataBar is not null && r.Element(x14 + "dataBar") is { } xdb)
                        ApplyDataBarExtension(mainRule.DataBar, xdb, styles);
                    continue;
                }
                DifferentialFormat? inline = r.Element(x14 + "dxf") is { } dx ? StyleTableParser.ParseDxf(dx, main) : null;
                var rule = ParseRule(r, main, styles, inline);
                if (rule is null) continue;
                rule.FromExtension = true;
                extOnly.Add(rule);
            }
            if (extOnly.Count > 0 && !string.IsNullOrWhiteSpace(sqref))
                formats.Add(new ConditionalFormatting(SplitRanges(sqref), extOnly) { Pivot = Bool(cf, "pivot", false) });
        }
        foreach (var dvs in extLst.Descendants(x14 + "dataValidations"))
            validations.AddRange(ParseDataValidations(dvs));
    }

    // ---- Helpers ------------------------------------------------------------------------

    internal static List<string> SplitRanges(string? sqref)
        => (sqref ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    internal static ConditionalRuleType? RuleType(string? t) => t switch
    {
        "cellIs" => ConditionalRuleType.CellIs,
        "expression" => ConditionalRuleType.Expression,
        "colorScale" => ConditionalRuleType.ColorScale,
        "dataBar" => ConditionalRuleType.DataBar,
        "iconSet" => ConditionalRuleType.IconSet,
        "top10" => ConditionalRuleType.Top10,
        "aboveAverage" => ConditionalRuleType.AboveAverage,
        "duplicateValues" => ConditionalRuleType.DuplicateValues,
        "uniqueValues" => ConditionalRuleType.UniqueValues,
        "containsText" => ConditionalRuleType.ContainsText,
        "notContainsText" => ConditionalRuleType.NotContainsText,
        "beginsWith" => ConditionalRuleType.BeginsWith,
        "endsWith" => ConditionalRuleType.EndsWith,
        "containsBlanks" => ConditionalRuleType.ContainsBlanks,
        "notContainsBlanks" => ConditionalRuleType.NotContainsBlanks,
        "containsErrors" => ConditionalRuleType.ContainsErrors,
        "notContainsErrors" => ConditionalRuleType.NotContainsErrors,
        "timePeriod" => ConditionalRuleType.TimePeriod,
        _ => null,
    };

    internal static string RuleTypeName(ConditionalRuleType t) => t switch
    {
        ConditionalRuleType.CellIs => "cellIs",
        ConditionalRuleType.Expression => "expression",
        ConditionalRuleType.ColorScale => "colorScale",
        ConditionalRuleType.DataBar => "dataBar",
        ConditionalRuleType.IconSet => "iconSet",
        ConditionalRuleType.Top10 => "top10",
        ConditionalRuleType.AboveAverage => "aboveAverage",
        ConditionalRuleType.DuplicateValues => "duplicateValues",
        ConditionalRuleType.UniqueValues => "uniqueValues",
        ConditionalRuleType.ContainsText => "containsText",
        ConditionalRuleType.NotContainsText => "notContainsText",
        ConditionalRuleType.BeginsWith => "beginsWith",
        ConditionalRuleType.EndsWith => "endsWith",
        ConditionalRuleType.ContainsBlanks => "containsBlanks",
        ConditionalRuleType.NotContainsBlanks => "notContainsBlanks",
        ConditionalRuleType.ContainsErrors => "containsErrors",
        ConditionalRuleType.NotContainsErrors => "notContainsErrors",
        _ => "timePeriod",
    };

    internal static ComparisonOp? Op(string? s) => s switch
    {
        "equal" => ComparisonOp.Equal,
        "notEqual" => ComparisonOp.NotEqual,
        "greaterThan" => ComparisonOp.GreaterThan,
        "greaterThanOrEqual" => ComparisonOp.GreaterThanOrEqual,
        "lessThan" => ComparisonOp.LessThan,
        "lessThanOrEqual" => ComparisonOp.LessThanOrEqual,
        "between" => ComparisonOp.Between,
        "notBetween" => ComparisonOp.NotBetween,
        _ => null,
    };

    internal static ConditionalTimePeriod? Period(string? s) => s switch
    {
        "today" => ConditionalTimePeriod.Today,
        "yesterday" => ConditionalTimePeriod.Yesterday,
        "tomorrow" => ConditionalTimePeriod.Tomorrow,
        "last7Days" => ConditionalTimePeriod.Last7Days,
        "thisMonth" => ConditionalTimePeriod.ThisMonth,
        "lastMonth" => ConditionalTimePeriod.LastMonth,
        "nextMonth" => ConditionalTimePeriod.NextMonth,
        "thisWeek" => ConditionalTimePeriod.ThisWeek,
        "lastWeek" => ConditionalTimePeriod.LastWeek,
        "nextWeek" => ConditionalTimePeriod.NextWeek,
        _ => null,
    };

    internal static string PeriodName(ConditionalTimePeriod p)
    {
        var n = p.ToString();
        return char.ToLowerInvariant(n[0]) + n.Substring(1);
    }

    internal static bool Bool(XElement e, string name, bool fallback)
        => (string?)e.Attribute(name) switch { "1" or "true" => true, "0" or "false" => false, _ => fallback };

    internal static int? Int(XElement e, string name)
        => int.TryParse((string?)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    internal static double? Dbl(XElement e, string name)
        => double.TryParse((string?)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
