using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Writes and reads <c>w:pPr</c> (paragraph properties) ⇄ <see cref="Paragraph"/> style, list
/// membership and <see cref="ParagraphFormat"/>, in CT_PPrBase schema order, shared by the
/// writer and the loader (and, later, paragraph style definitions).
/// </summary>
internal static class ParagraphProperties
{
    private const string W = DocumentSerializer.W;

    /// <summary>
    /// Writes <c>w:pPr</c> when anything is set. Order (CT_PPr): pStyle, keepNext, keepLines,
    /// pageBreakBefore, widowControl, numPr, suppressLineNumbers, pBdr, shd, tabs,
    /// suppressAutoHyphens, kinsoku, wordWrap, overflowPunct, topLinePunct, autoSpaceDE,
    /// autoSpaceDN, bidi, adjustRightInd, snapToGrid, spacing, ind, contextualSpacing,
    /// mirrorIndents, jc, textAlignment, outlineLvl, rPr.
    /// </summary>
    public static void Write(XmlWriter x, Paragraph p, NumberingXml.Plan? numbering, Action<XmlWriter>? sectPr = null,
        Func<RevisionMark, string>? revisionId = null)
    {
        var f = p.Format;
        var styleId = p.StyleId;
        bool hasStyle = styleId != "Normal";
        var num = numbering?.For(p);
        bool tracked = revisionId is not null && (p.MarkRevision is not null || p.FormatChange is not null || p.MarkFormatChange is not null);
        bool needed = hasStyle || num is not null || !f.IsDefault || sectPr is not null || tracked;
        if (!needed) return;

        x.WriteStartElement("pPr", W);
        if (hasStyle) Val(x, "pStyle", styleId);
        Action<XmlWriter>? markHead = revisionId is not null && p.MarkRevision is { } mr
            ? w => WriteRevisionStart(w, mr.Type == RevisionType.Delete ? "del" : "ins", mr, revisionId(mr), close: true)
            : null;
        Action<XmlWriter>? markTail = revisionId is not null && p.MarkFormatChange is { } mfc
            ? w => WriteRunFormatChange(w, mfc, revisionId(mfc.Mark))
            : null;
        WriteBody(x, f, num, markHead, markTail: markTail);
        sectPr?.Invoke(x); // CT_PPr: … rPr, sectPr, pPrChange
        if (revisionId is not null && p.FormatChange is { } change)
        {
            WriteRevisionStart(x, "pPrChange", change.Mark, revisionId(change.Mark), close: false);
            x.WriteStartElement("pPr", W);
            if (change.OldStyleId is { } os) Val(x, "pStyle", os);
            // The old numbering is written back as loaded; a list that no longer exists is left out.
            (int? NumId, int? Level)? oldNum = change.OldNumbering is var (oid, olvl)
                ? oid is null or 0 ? (oid, olvl)
                  : numbering?.IdOf(change.OldList) is int nid ? (nid, olvl) : null
                : null;
            WriteBody(x, change.OldFormat, oldNum, null, includeMark: false);
            x.WriteEndElement();
            x.WriteEndElement();
        }
        x.WriteEndElement();
    }

    /// <summary>w:rPrChange: the old run properties (CT_RPrOriginal, without a nested change).</summary>
    internal static void WriteRunFormatChange(XmlWriter x, RunFormatChange change, string id)
    {
        WriteRevisionStart(x, "rPrChange", change.Mark, id, close: false);
        x.WriteStartElement("rPr", W);
        RunProperties.WriteChildren(x, change.OldFormat, change.OldCharacterStyleId);
        x.WriteEndElement();
        x.WriteEndElement();
    }

    /// <summary>Starts a tracked-change element (w:ins, w:del, w:pPrChange, w:rPrChange…) with id, author and date.</summary>
    internal static void WriteRevisionStart(XmlWriter x, string element, RevisionMark mark, string id, bool close)
    {
        x.WriteStartElement(element, W);
        x.WriteAttributeString("id", W, id);
        x.WriteAttributeString("author", W, mark.Author);
        if (mark.Date is DateTime d)
            x.WriteAttributeString("date", W, (d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime())
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        if (close) x.WriteEndElement();
    }

    /// <summary>Writes the pPr children for a format (no pStyle) — reusable by style definitions and list levels.</summary>
    internal static void WriteBody(XmlWriter x, ParagraphFormat f, (int? NumId, int? Level)? numbering = null,
        Action<XmlWriter>? markHead = null, bool includeMark = true, Action<XmlWriter>? markTail = null)
    {
        Toggle(x, "keepNext", f.KeepWithNext);
        Toggle(x, "keepLines", f.KeepLinesTogether);
        Toggle(x, "pageBreakBefore", f.StartOnNewPage);
        Toggle(x, "widowControl", f.WidowControl);

        if (numbering is var (numId, level))
        {
            x.WriteStartElement("numPr", W);
            if (level is int l) Val(x, "ilvl", l.ToString(CultureInfo.InvariantCulture));
            if (numId is int n) Val(x, "numId", n.ToString(CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }

        Toggle(x, "suppressLineNumbers", f.SuppressLineNumbers);

        if (f.Borders is { IsEmpty: false } b)
        {
            x.WriteStartElement("pBdr", W);
            if (b.Top is not null) FormattingXml.WriteBorder(x, "top", b.Top);
            if (b.Left is not null) FormattingXml.WriteBorder(x, "left", b.Left);
            if (b.Bottom is not null) FormattingXml.WriteBorder(x, "bottom", b.Bottom);
            if (b.Right is not null) FormattingXml.WriteBorder(x, "right", b.Right);
            if (b.Between is not null) FormattingXml.WriteBorder(x, "between", b.Between);
            if (b.Bar is not null) FormattingXml.WriteBorder(x, "bar", b.Bar);
            x.WriteEndElement();
        }

        if (f.Shading is not null) FormattingXml.WriteShading(x, f.Shading);

        if (f.TabStops.Count > 0)
        {
            x.WriteStartElement("tabs", W);
            foreach (var t in f.TabStops.OrderBy(t => t.PositionPt))
            {
                x.WriteStartElement("tab", W);
                x.WriteAttributeString("val", W, TabToken(t.Alignment));
                if (t.Leader != TabLeader.None) x.WriteAttributeString("leader", W, LeaderToken(t.Leader));
                x.WriteAttributeString("pos", W, RunProperties.Twips(t.PositionPt));
                x.WriteEndElement();
            }
            x.WriteEndElement();
        }

        Toggle(x, "suppressAutoHyphens", f.SuppressAutoHyphens);
        Toggle(x, "kinsoku", f.Kinsoku);
        Toggle(x, "wordWrap", f.WordWrap);
        Toggle(x, "overflowPunct", f.OverflowPunctuation);
        Toggle(x, "topLinePunct", f.TopLinePunctuation);
        Toggle(x, "autoSpaceDE", f.AutoSpaceDE);
        Toggle(x, "autoSpaceDN", f.AutoSpaceDN);
        Toggle(x, "bidi", f.RightToLeft);
        Toggle(x, "adjustRightInd", f.AdjustRightIndent);
        Toggle(x, "snapToGrid", f.SnapToGrid);

        if (f.SpaceBeforePt is not null || f.SpaceAfterPt is not null || f.SpaceBeforeAuto is not null
            || f.SpaceAfterAuto is not null || f.LineSpacing is not null)
        {
            x.WriteStartElement("spacing", W);
            if (f.SpaceBeforePt is double sb) x.WriteAttributeString("before", W, RunProperties.Twips(sb));
            if (f.SpaceBeforeAuto is bool ba) x.WriteAttributeString("beforeAutospacing", W, ba ? "1" : "0");
            if (f.SpaceAfterPt is double sa) x.WriteAttributeString("after", W, RunProperties.Twips(sa));
            if (f.SpaceAfterAuto is bool aa) x.WriteAttributeString("afterAutospacing", W, aa ? "1" : "0");
            if (f.LineSpacing is LineSpacing ls)
            {
                if (ls.Rule == LineSpacingRule.Multiple)
                {
                    x.WriteAttributeString("line", W, ((int)Math.Round(ls.Value * 240, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture));
                    x.WriteAttributeString("lineRule", W, "auto");
                }
                else
                {
                    x.WriteAttributeString("line", W, RunProperties.Twips(ls.Value));
                    x.WriteAttributeString("lineRule", W, ls.Rule == LineSpacingRule.Exactly ? "exact" : "atLeast");
                }
            }
            x.WriteEndElement();
        }

        if (f.IndentLeftPt is not null || f.IndentRightPt is not null || f.FirstLineIndentPt is not null || f.HangingIndentPt is not null)
        {
            x.WriteStartElement("ind", W);
            if (f.IndentLeftPt is double il) x.WriteAttributeString("left", W, RunProperties.Twips(il));
            if (f.IndentRightPt is double ir) x.WriteAttributeString("right", W, RunProperties.Twips(ir));
            if (f.HangingIndentPt is double h) x.WriteAttributeString("hanging", W, RunProperties.Twips(h));
            else if (f.FirstLineIndentPt is double fl) x.WriteAttributeString("firstLine", W, RunProperties.Twips(fl));
            x.WriteEndElement();
        }

        Toggle(x, "contextualSpacing", f.ContextualSpacing);
        Toggle(x, "mirrorIndents", f.MirrorIndents);

        if (f.Alignment is ParagraphAlignment a) Val(x, "jc", AlignToken(a));
        if (f.FontAlignment is FontAlignment fa) Val(x, "textAlignment", fa switch
        {
            Word.FontAlignment.Auto => "auto",
            Word.FontAlignment.Top => "top",
            Word.FontAlignment.Center => "center",
            Word.FontAlignment.Baseline => "baseline",
            Word.FontAlignment.Bottom => "bottom",
            _ => throw new ArgumentOutOfRangeException(nameof(ParagraphFormat.FontAlignment), fa, "Undefined FontAlignment value."),
        });
        if (f.OutlineLevel is int ol)
        {
            if (ol is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(ParagraphFormat.OutlineLevel), ol, "Outline level must be 0–9.");
            Val(x, "outlineLvl", ol.ToString(CultureInfo.InvariantCulture));
        }

        if (includeMark && (f.MarkFormat is { IsDefault: false } || markHead is not null || markTail is not null))
            RunProperties.Write(x, f.MarkFormat ?? TextFormat.None, null, markHead, markTail);
    }

    /// <summary>
    /// Reads a <c>w:pPr</c> subtree (reader on the pPr element) into <paramref name="p"/>.
    /// Tracked-change history (w:pPrChange), section properties and frames are skipped.
    /// </summary>
    public static void Read(XmlReader sub, Paragraph p)
    {
        var f = p.Format;
        if (!sub.Read()) return;
        int childDepth = sub.Depth + 1;

        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.Depth != childDepth) continue;
            if (sub.NamespaceURI != W)
            {
                if (!sub.IsEmptyElement) using (sub.ReadSubtree()) { }
                continue;
            }
            switch (sub.LocalName)
            {
                case "pStyle": if (Attr(sub, "val") is { Length: > 0 } sid) p.StyleId = sid; break;
                case "keepNext": f.KeepWithNext = RunProperties.OnOff(sub); break;
                case "keepLines": f.KeepLinesTogether = RunProperties.OnOff(sub); break;
                case "pageBreakBefore": f.StartOnNewPage = RunProperties.OnOff(sub); break;
                case "widowControl": f.WidowControl = RunProperties.OnOff(sub); break;
                case "suppressLineNumbers": f.SuppressLineNumbers = RunProperties.OnOff(sub); break;
                case "suppressAutoHyphens": f.SuppressAutoHyphens = RunProperties.OnOff(sub); break;
                case "kinsoku": f.Kinsoku = RunProperties.OnOff(sub); break;
                case "wordWrap": f.WordWrap = RunProperties.OnOff(sub); break;
                case "overflowPunct": f.OverflowPunctuation = RunProperties.OnOff(sub); break;
                case "topLinePunct": f.TopLinePunctuation = RunProperties.OnOff(sub); break;
                case "autoSpaceDE": f.AutoSpaceDE = RunProperties.OnOff(sub); break;
                case "autoSpaceDN": f.AutoSpaceDN = RunProperties.OnOff(sub); break;
                case "bidi": f.RightToLeft = RunProperties.OnOff(sub); break;
                case "adjustRightInd": f.AdjustRightIndent = RunProperties.OnOff(sub); break;
                case "snapToGrid": f.SnapToGrid = RunProperties.OnOff(sub); break;
                case "contextualSpacing": f.ContextualSpacing = RunProperties.OnOff(sub); break;
                case "mirrorIndents": f.MirrorIndents = RunProperties.OnOff(sub); break;
                case "jc": f.Alignment = ParseAlign(Attr(sub, "val")); break;
                case "textAlignment":
                    f.FontAlignment = Attr(sub, "val") switch
                    {
                        "auto" => Word.FontAlignment.Auto,
                        "top" => Word.FontAlignment.Top,
                        "center" => Word.FontAlignment.Center,
                        "baseline" => Word.FontAlignment.Baseline,
                        "bottom" => Word.FontAlignment.Bottom,
                        _ => null,
                    };
                    break;
                case "outlineLvl":
                    f.OutlineLevel = int.TryParse(Attr(sub, "val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ol)
                        && ol is >= 0 and <= 9 ? ol : null;
                    break;
                case "shd": f.Shading = FormattingXml.ReadShading(sub); break;
                case "spacing":
                    f.SpaceBeforePt = RunProperties.FromTwips(Attr(sub, "before")) ?? f.SpaceBeforePt;
                    f.SpaceAfterPt = RunProperties.FromTwips(Attr(sub, "after")) ?? f.SpaceAfterPt;
                    if (Attr(sub, "beforeAutospacing") is { } bas) f.SpaceBeforeAuto = bas is "1" or "true" or "on";
                    if (Attr(sub, "afterAutospacing") is { } aas) f.SpaceAfterAuto = aas is "1" or "true" or "on";
                    if (RunProperties.TryMeasure(Attr(sub, "line"), out var line))
                    {
                        // Hostile values (≤ 0, NaN, ±Inf, underflow) are ignored rather than thrown.
                        var rule = Attr(sub, "lineRule");
                        double value = rule is "exact" or "atLeast" ? line / 20.0 : line / 240.0;
                        if (double.IsFinite(value) && value > 0)
                        {
                            f.LineSpacing = rule switch
                            {
                                "exact" => LineSpacing.Exactly(value),
                                "atLeast" => LineSpacing.AtLeast(value),
                                _ => LineSpacing.Multiple(value),
                            };
                        }
                    }
                    break;
                case "ind":
                    f.IndentLeftPt = RunProperties.FromTwips(Attr(sub, "left") ?? Attr(sub, "start")) ?? f.IndentLeftPt;
                    f.IndentRightPt = RunProperties.FromTwips(Attr(sub, "right") ?? Attr(sub, "end")) ?? f.IndentRightPt;
                    f.FirstLineIndentPt = RunProperties.FromTwips(Attr(sub, "firstLine")) ?? f.FirstLineIndentPt;
                    f.HangingIndentPt = RunProperties.FromTwips(Attr(sub, "hanging")) ?? f.HangingIndentPt;
                    break;
                case "numPr":
                    using (var np = sub.ReadSubtree()) ReadNumbering(np, p);
                    break;
                case "pBdr":
                    using (var bd = sub.ReadSubtree()) f.Borders = ReadBorders(bd);
                    break;
                case "tabs":
                    using (var tb = sub.ReadSubtree()) ReadTabs(tb, f);
                    break;
                case "rPr":
                {
                    TextFormat mark;
                    RevisionMark? markRevision;
                    RunFormatChange? markChange;
                    using (var rp = sub.ReadSubtree()) mark = RunProperties.Read(rp, out _, out markChange, out markRevision);
                    f.MarkFormat = mark.IsDefault ? null : mark;
                    if (markRevision is not null) p.MarkRevision = markRevision;
                    if (markChange is not null) p.MarkFormatChange = markChange;
                    break;
                }
                case "pPrChange":
                {
                    var changeMark = RunProperties.ReadRevisionMark(sub, RevisionType.FormatChange);
                    var old = new Paragraph();
                    bool sawStyle = false;
                    if (!sub.IsEmptyElement)
                        using (var ch = sub.ReadSubtree())
                        {
                            ch.Read();
                            while (ch.Read())
                                if (ch.NodeType == XmlNodeType.Element && ch.NamespaceURI == W && ch.LocalName == "pPr")
                                {
                                    using (var inner = ch.ReadSubtree()) Read(inner, old);
                                    break;
                                }
                        }
                    sawStyle = old.StyleId != "Normal";
                    p.FormatChange = new ParagraphFormatChange(changeMark, old.Format, sawStyle ? old.StyleId : null)
                    {
                        OldNumbering = old.LoadedNumId is not null || old.LoadedListLevelExplicit
                            ? (old.LoadedNumId, old.LoadedListLevelExplicit ? old.ListLevel : null)
                            : null,
                    };
                    break;
                }
                case "sectPr":
                    using (var sp = sub.ReadSubtree()) p.LoadedSection = SectionXml.Read(sp);
                    break;
                default:
                    // framePr, cnfStyle, divId…: not part of the model.
                    if (!sub.IsEmptyElement) using (sub.ReadSubtree()) { }
                    break;
            }
        }
    }

    private static void ReadNumbering(XmlReader sub, Paragraph p)
    {
        string? numId = null;
        int? ilvl = null;
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI != W) continue;
            if (sub.LocalName == "numId") numId = Attr(sub, "val");
            else if (sub.LocalName == "ilvl" && int.TryParse(Attr(sub, "val"), out var l)) ilvl = l;
        }
        // Resolved against numbering.xml once it is read (see DocumentLoader); numId 0 = explicitly none.
        p.ListLevel = ilvl is int level ? Math.Clamp(level, 0, 8) : 0;
        p.LoadedNumId = int.TryParse(numId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
        p.LoadedListLevelExplicit = ilvl is not null;
    }

    private static ParagraphBorders ReadBorders(XmlReader sub)
    {
        var b = new ParagraphBorders();
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI != W) continue;
            switch (sub.LocalName)
            {
                case "top": b.Top = FormattingXml.ReadBorder(sub); break;
                case "left": case "start": b.Left = FormattingXml.ReadBorder(sub); break;
                case "bottom": b.Bottom = FormattingXml.ReadBorder(sub); break;
                case "right": case "end": b.Right = FormattingXml.ReadBorder(sub); break;
                case "between": b.Between = FormattingXml.ReadBorder(sub); break;
                case "bar": b.Bar = FormattingXml.ReadBorder(sub); break;
            }
        }
        return b;
    }

    private static void ReadTabs(XmlReader sub, ParagraphFormat f)
    {
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI != W || sub.LocalName != "tab") continue;
            var pos = RunProperties.FromTwips(Attr(sub, "pos")) ?? 0;
            var align = Attr(sub, "val") switch
            {
                "center" => TabAlignment.Center,
                "right" or "end" => TabAlignment.Right,
                "decimal" => TabAlignment.Decimal,
                "bar" => TabAlignment.Bar,
                "clear" => TabAlignment.Clear,
                "num" => TabAlignment.Number,
                _ => TabAlignment.Left,
            };
            var leader = Attr(sub, "leader") switch
            {
                "dot" => TabLeader.Dot,
                "hyphen" => TabLeader.Hyphen,
                "underscore" => TabLeader.Underscore,
                "heavy" => TabLeader.Heavy,
                "middleDot" => TabLeader.MiddleDot,
                _ => TabLeader.None,
            };
            f.TabStops.Add(new TabStop(pos, align, leader));
        }
    }

    // ---- Tokens ---------------------------------------------------------------------------

    internal static string AlignToken(ParagraphAlignment a) => a switch
    {
        ParagraphAlignment.Center => "center",
        ParagraphAlignment.Right => "right",
        ParagraphAlignment.Justify => "both",
        ParagraphAlignment.Distribute => "distribute",
        ParagraphAlignment.LowKashida => "lowKashida",
        ParagraphAlignment.MediumKashida => "mediumKashida",
        ParagraphAlignment.HighKashida => "highKashida",
        ParagraphAlignment.ThaiDistribute => "thaiDistribute",
        _ => "left",
    };

    internal static ParagraphAlignment? ParseAlign(string? v) => v switch
    {
        "left" or "start" => ParagraphAlignment.Left,
        "center" => ParagraphAlignment.Center,
        "right" or "end" => ParagraphAlignment.Right,
        "both" => ParagraphAlignment.Justify,
        "distribute" => ParagraphAlignment.Distribute,
        "lowKashida" => ParagraphAlignment.LowKashida,
        "mediumKashida" => ParagraphAlignment.MediumKashida,
        "highKashida" => ParagraphAlignment.HighKashida,
        "thaiDistribute" => ParagraphAlignment.ThaiDistribute,
        _ => null,
    };

    private static string TabToken(TabAlignment a) => a switch
    {
        TabAlignment.Center => "center",
        TabAlignment.Right => "right",
        TabAlignment.Decimal => "decimal",
        TabAlignment.Bar => "bar",
        TabAlignment.Clear => "clear",
        TabAlignment.Number => "num",
        _ => "left",
    };

    private static string LeaderToken(TabLeader l) => l switch
    {
        TabLeader.Dot => "dot",
        TabLeader.Hyphen => "hyphen",
        TabLeader.Underscore => "underscore",
        TabLeader.Heavy => "heavy",
        TabLeader.MiddleDot => "middleDot",
        _ => "none",
    };

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement(name, W);
        x.WriteAttributeString("val", W, value);
        x.WriteEndElement();
    }

    private static void Toggle(XmlWriter x, string name, bool? value)
    {
        if (value is null) return;
        x.WriteStartElement(name, W);
        if (value == false) x.WriteAttributeString("val", W, "0");
        x.WriteEndElement();
    }

    private static string? Attr(XmlReader r, string name) => r.GetAttribute(name, W);
}
