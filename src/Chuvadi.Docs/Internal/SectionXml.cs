using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// w:sectPr ⇄ <see cref="Section"/> in CT_SectPr order: headerReference/footerReference*,
/// footnotePr, endnotePr, type, pgSz, pgMar, paperSrc, pgBorders, lnNumType, pgNumType, cols,
/// formProt, vAlign, noEndnote, titlePg, textDirection, bidi, rtlGutter, docGrid.
/// </summary>
internal static class SectionXml
{
    private const string W = DocumentSerializer.W;
    private const string R = DocumentSerializer.R;

    /// <summary>A header/footer reference: kind, type (default / first / even) and relationship id.</summary>
    internal readonly record struct Reference(bool IsHeader, string Type, string RelId);

    // =====================================================================================
    // Writer
    // =====================================================================================

    public static void Write(XmlWriter x, Section s, IEnumerable<Reference> references)
    {
        x.WriteStartElement("sectPr", W);
        foreach (var rf in references)
        {
            x.WriteStartElement(rf.IsHeader ? "headerReference" : "footerReference", W);
            x.WriteAttributeString("type", W, rf.Type);
            x.WriteAttributeString("id", R, rf.RelId);
            x.WriteEndElement();
        }
        foreach (var raw in s.RawNoteProperties) StylesXml.WriteFragment(x, raw);
        if (s.Start is SectionStart st) Val(x, "type", st switch
        {
            SectionStart.NextPage => "nextPage",
            SectionStart.Continuous => "continuous",
            SectionStart.EvenPage => "evenPage",
            SectionStart.OddPage => "oddPage",
            SectionStart.NextColumn => "nextColumn",
            _ => throw new ArgumentOutOfRangeException(nameof(s), st, "Undefined SectionStart value."),
        });

        var page = s.Page;
        if (!Enum.IsDefined(page.Size)) throw new ArgumentOutOfRangeException(nameof(s), page.Size, "Undefined PageSize value.");
        var (pw, ph) = page.PortraitTwips;
        bool landscape = page.Orientation == PageOrientation.Landscape;
        // A loaded section that stated no size keeps saying nothing (Word applies its default).
        if (page.SizeStated || landscape || page.PaperCode is not null)
        {
            x.WriteStartElement("pgSz", W);
            x.WriteAttributeString("w", W, Int(landscape ? ph : pw));
            x.WriteAttributeString("h", W, Int(landscape ? pw : ph));
            if (landscape) x.WriteAttributeString("orient", W, "landscape");
            if (page.PaperCode is int code) x.WriteAttributeString("code", W, Int(code));
            x.WriteEndElement();
        }

        x.WriteStartElement("pgMar", W);
        x.WriteAttributeString("top", W, RunProperties.Twips(page.TopMarginPt));
        x.WriteAttributeString("right", W, RunProperties.Twips(page.RightMarginPt));
        x.WriteAttributeString("bottom", W, RunProperties.Twips(page.BottomMarginPt));
        x.WriteAttributeString("left", W, RunProperties.Twips(page.LeftMarginPt));
        x.WriteAttributeString("header", W, RunProperties.Twips(page.HeaderDistancePt));
        x.WriteAttributeString("footer", W, RunProperties.Twips(page.FooterDistancePt));
        x.WriteAttributeString("gutter", W, RunProperties.Twips(page.GutterPt));
        x.WriteEndElement();

        if (page.PaperSourceFirstPage is not null || page.PaperSourceOtherPages is not null)
        {
            x.WriteStartElement("paperSrc", W);
            if (page.PaperSourceFirstPage is int f) x.WriteAttributeString("first", W, Int(f));
            if (page.PaperSourceOtherPages is int o) x.WriteAttributeString("other", W, Int(o));
            x.WriteEndElement();
        }

        if (s.PageBorders is { } pb)
        {
            x.WriteStartElement("pgBorders", W);
            if (pb.InFront is bool front) x.WriteAttributeString("zOrder", W, front ? "front" : "back");
            if (pb.Display is PageBorderDisplay d) x.WriteAttributeString("display", W, d switch
            {
                PageBorderDisplay.AllPages => "allPages",
                PageBorderDisplay.FirstPage => "firstPage",
                PageBorderDisplay.NotFirstPage => "notFirstPage",
                _ => throw new ArgumentOutOfRangeException(nameof(s), d, "Undefined PageBorderDisplay value."),
            });
            if (pb.OffsetFrom is PageBorderOffset of) x.WriteAttributeString("offsetFrom", W, of == PageBorderOffset.Page ? "page" : "text");
            if (pb.Top is { } t) FormattingXml.WriteBorder(x, "top", t);
            if (pb.Left is { } l) FormattingXml.WriteBorder(x, "left", l);
            if (pb.Bottom is { } b) FormattingXml.WriteBorder(x, "bottom", b);
            if (pb.Right is { } r) FormattingXml.WriteBorder(x, "right", r);
            x.WriteEndElement();
        }

        if (s.LineNumbering is { } ln)
        {
            x.WriteStartElement("lnNumType", W);
            x.WriteAttributeString("countBy", W, Int(Math.Max(1, ln.CountBy)));
            if (ln.Start is int lst) x.WriteAttributeString("start", W, Int(lst));
            if (ln.DistancePt is double dist) x.WriteAttributeString("distance", W, RunProperties.Twips(dist));
            if (ln.Restart is LineNumberRestart rs) x.WriteAttributeString("restart", W, rs switch
            {
                LineNumberRestart.EachPage => "newPage",
                LineNumberRestart.EachSection => "newSection",
                LineNumberRestart.Continuous => "continuous",
                _ => throw new ArgumentOutOfRangeException(nameof(s), rs, "Undefined LineNumberRestart value."),
            });
            x.WriteEndElement();
        }

        var pn = s.PageNumbering;
        if (!pn.IsDefault)
        {
            x.WriteStartElement("pgNumType", W);
            if (pn.Format is NumberFormat nf) x.WriteAttributeString("fmt", W, NumberingXml.FormatToken(nf));
            if (pn.Start is int ps) x.WriteAttributeString("start", W, Int(ps));
            if (pn.ChapterStyle is int cs) x.WriteAttributeString("chapStyle", W, Int(Math.Clamp(cs, 1, 9)));
            if (pn.ChapterSeparator is ChapterSeparator sep) x.WriteAttributeString("chapSep", W, sep switch
            {
                ChapterSeparator.Hyphen => "hyphen",
                ChapterSeparator.Period => "period",
                ChapterSeparator.Colon => "colon",
                ChapterSeparator.EmDash => "emDash",
                ChapterSeparator.EnDash => "enDash",
                _ => throw new ArgumentOutOfRangeException(nameof(s), sep, "Undefined ChapterSeparator value."),
            });
            x.WriteEndElement();
        }

        var cols = s.Columns;
        if (!cols.IsDefault)
        {
            x.WriteStartElement("cols", W);
            bool? equal = cols.EqualWidth ?? (cols.Columns.Count > 0 ? false : null); // w:col children only count with equalWidth="0"
            if (equal is bool eq) x.WriteAttributeString("equalWidth", W, eq ? "1" : "0");
            if (cols.SpacingPt is double sp) x.WriteAttributeString("space", W, RunProperties.Twips(sp));
            int? count = cols.Count ?? (cols.Columns.Count > 0 ? cols.Columns.Count : null);
            if (count is int n) x.WriteAttributeString("num", W, Int(Math.Clamp(n, 1, 45)));
            if (cols.Separator is bool sepLine) x.WriteAttributeString("sep", W, sepLine ? "1" : "0");
            foreach (var c in cols.Columns)
            {
                x.WriteStartElement("col", W);
                x.WriteAttributeString("w", W, RunProperties.Twips(c.WidthPt));
                x.WriteAttributeString("space", W, RunProperties.Twips(c.SpaceAfterPt));
                x.WriteEndElement();
            }
            x.WriteEndElement();
        }

        Toggle(x, "formProt", s.FormProtection);
        if (s.VerticalAlignment is SectionVerticalAlignment va) Val(x, "vAlign", va switch
        {
            SectionVerticalAlignment.Top => "top",
            SectionVerticalAlignment.Center => "center",
            SectionVerticalAlignment.Justified => "both",
            SectionVerticalAlignment.Bottom => "bottom",
            _ => throw new ArgumentOutOfRangeException(nameof(s), va, "Undefined SectionVerticalAlignment value."),
        });
        Toggle(x, "noEndnote", s.NoEndnotes);
        if (s.DifferentFirstPage) Toggle(x, "titlePg", true); // false is the absence of the element
        if (s.TextDirection is CellTextDirection td) Val(x, "textDirection", TableXml.TextDirectionToken(td));
        Toggle(x, "bidi", s.RightToLeft);
        Toggle(x, "rtlGutter", s.RightToLeftGutter);
        if (s.Grid is { } g)
        {
            x.WriteStartElement("docGrid", W);
            if (g.Type is DocumentGridType gt) x.WriteAttributeString("type", W, gt switch
            {
                DocumentGridType.Default => "default",
                DocumentGridType.Lines => "lines",
                DocumentGridType.LinesAndChars => "linesAndChars",
                DocumentGridType.SnapToChars => "snapToChars",
                _ => throw new ArgumentOutOfRangeException(nameof(s), gt, "Undefined DocumentGridType value."),
            });
            if (g.LinePitchPt is double lp) x.WriteAttributeString("linePitch", W, RunProperties.Twips(lp));
            if (g.CharacterSpace is int chs) x.WriteAttributeString("charSpace", W, Int(chs));
            x.WriteEndElement();
        }
        x.WriteEndElement(); // sectPr
    }

    // =====================================================================================
    // Reader
    // =====================================================================================

    /// <summary>Reads a w:sectPr subtree (reader on it) into a section and its header/footer references.</summary>
    public static (Section Section, List<Reference> References) Read(XmlReader r)
    {
        var s = new Section();
        s.Page.SizeStated = false; // until a valid w:pgSz is read
        var refs = new List<Reference>();
        if (!r.Read()) return (s, refs);
        int childDepth = r.Depth + 1;
        bool sawTitlePg = false;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth) continue;
            if (r.NamespaceURI != W) { if (!r.IsEmptyElement) using (r.ReadSubtree()) { } continue; }
            switch (r.LocalName)
            {
                case "headerReference":
                case "footerReference":
                {
                    var id = r.GetAttribute("id", R);
                    var type = r.GetAttribute("type", W) ?? "default";
                    if (id is not null && type is "default" or "first" or "even" && refs.Count < 6)
                        refs.Add(new Reference(r.LocalName == "headerReference", type, id));
                    break;
                }
                case "footnotePr":
                case "endnotePr":
                    using (var sub = r.ReadSubtree()) { sub.Read(); s.RawNoteProperties.Add(sub.ReadOuterXml()); }
                    break;
                case "type":
                    s.Start = Attr(r) switch
                    {
                        "nextPage" => SectionStart.NextPage,
                        "continuous" => SectionStart.Continuous,
                        "evenPage" => SectionStart.EvenPage,
                        "oddPage" => SectionStart.OddPage,
                        "nextColumn" => SectionStart.NextColumn,
                        _ => null,
                    };
                    break;
                case "pgSz": ReadPageSize(r, s.Page); break;
                case "pgMar":
                {
                    var p = s.Page;
                    p.TopMarginPt = RunProperties.FromTwips(r.GetAttribute("top", W)) ?? p.TopMarginPt;
                    p.RightMarginPt = RunProperties.FromTwips(r.GetAttribute("right", W) ?? r.GetAttribute("end", W)) ?? p.RightMarginPt;
                    p.BottomMarginPt = RunProperties.FromTwips(r.GetAttribute("bottom", W)) ?? p.BottomMarginPt;
                    p.LeftMarginPt = RunProperties.FromTwips(r.GetAttribute("left", W) ?? r.GetAttribute("start", W)) ?? p.LeftMarginPt;
                    // header/footer/gutter are unsigned in the schema.
                    p.HeaderDistancePt = NonNegative(RunProperties.FromTwips(r.GetAttribute("header", W))) ?? p.HeaderDistancePt;
                    p.FooterDistancePt = NonNegative(RunProperties.FromTwips(r.GetAttribute("footer", W))) ?? p.FooterDistancePt;
                    p.GutterPt = NonNegative(RunProperties.FromTwips(r.GetAttribute("gutter", W))) ?? p.GutterPt;
                    break;
                }
                case "paperSrc":
                    s.Page.PaperSourceFirstPage = ParseInt(r.GetAttribute("first", W));
                    s.Page.PaperSourceOtherPages = ParseInt(r.GetAttribute("other", W));
                    break;
                case "pgBorders":
                {
                    var pb = new PageBorders
                    {
                        InFront = r.GetAttribute("zOrder", W) switch { "front" => true, "back" => false, _ => null },
                        Display = r.GetAttribute("display", W) switch
                        {
                            "allPages" => PageBorderDisplay.AllPages,
                            "firstPage" => PageBorderDisplay.FirstPage,
                            "notFirstPage" => PageBorderDisplay.NotFirstPage,
                            _ => null,
                        },
                        OffsetFrom = r.GetAttribute("offsetFrom", W) switch { "page" => PageBorderOffset.Page, "text" => PageBorderOffset.Text, _ => null },
                    };
                    using (var sub = r.ReadSubtree())
                    {
                        sub.Read();
                        int d = sub.Depth + 1;
                        while (sub.Read())
                        {
                            if (sub.NodeType != XmlNodeType.Element || sub.Depth != d || sub.NamespaceURI != W) continue;
                            switch (sub.LocalName)
                            {
                                case "top": pb.Top = FormattingXml.ReadBorder(sub); break;
                                case "left": case "start": pb.Left = FormattingXml.ReadBorder(sub); break;
                                case "bottom": pb.Bottom = FormattingXml.ReadBorder(sub); break;
                                case "right": case "end": pb.Right = FormattingXml.ReadBorder(sub); break;
                            }
                        }
                    }
                    s.PageBorders = pb;
                    break;
                }
                case "lnNumType":
                    s.LineNumbering = new LineNumbering
                    {
                        CountBy = Math.Clamp(ParseInt(r.GetAttribute("countBy", W)) ?? 1, 1, 100),
                        Start = ParseInt(r.GetAttribute("start", W)) is int ls && ls >= 0 ? ls : null,
                        DistancePt = NonNegative(RunProperties.FromTwips(r.GetAttribute("distance", W))),
                        Restart = r.GetAttribute("restart", W) switch
                        {
                            "newPage" => LineNumberRestart.EachPage,
                            "newSection" => LineNumberRestart.EachSection,
                            "continuous" => LineNumberRestart.Continuous,
                            _ => null,
                        },
                    };
                    break;
                case "pgNumType":
                {
                    var pn = s.PageNumbering;
                    pn.Format = NumberingXml.ParseFormat(r.GetAttribute("fmt", W));
                    pn.Start = ParseInt(r.GetAttribute("start", W)) is int st && st >= 0 ? st : null;
                    pn.ChapterStyle = ParseInt(r.GetAttribute("chapStyle", W)) is int cs ? Math.Clamp(cs, 1, 9) : null;
                    pn.ChapterSeparator = r.GetAttribute("chapSep", W) switch
                    {
                        "hyphen" => ChapterSeparator.Hyphen,
                        "period" => ChapterSeparator.Period,
                        "colon" => ChapterSeparator.Colon,
                        "emDash" => ChapterSeparator.EmDash,
                        "enDash" => ChapterSeparator.EnDash,
                        _ => null,
                    };
                    break;
                }
                case "cols":
                {
                    var c = s.Columns;
                    var eq = r.GetAttribute("equalWidth", W);
                    c.EqualWidth = eq is null ? null : eq is "1" or "true" or "on";
                    c.SpacingPt = NonNegative(RunProperties.FromTwips(r.GetAttribute("space", W)));
                    c.Count = ParseInt(r.GetAttribute("num", W)) is int n ? Math.Clamp(n, 1, 45) : null;
                    var sep = r.GetAttribute("sep", W);
                    c.Separator = sep is null ? null : sep is "1" or "true" or "on";
                    using (var sub = r.ReadSubtree())
                    {
                        sub.Read();
                        int d = sub.Depth + 1;
                        while (sub.Read())
                        {
                            if (sub.NodeType != XmlNodeType.Element || sub.Depth != d || sub.NamespaceURI != W || sub.LocalName != "col") continue;
                            if (c.Columns.Count >= 45) continue;
                            c.Columns.Add(new ColumnDefinition(
                                NonNegative(RunProperties.FromTwips(sub.GetAttribute("w", W))) ?? 0,
                                NonNegative(RunProperties.FromTwips(sub.GetAttribute("space", W))) ?? 0));
                        }
                    }
                    break;
                }
                case "formProt": s.FormProtection = RunProperties.OnOff(r); break;
                case "vAlign":
                    s.VerticalAlignment = Attr(r) switch
                    {
                        "top" => SectionVerticalAlignment.Top,
                        "center" => SectionVerticalAlignment.Center,
                        "both" => SectionVerticalAlignment.Justified,
                        "bottom" => SectionVerticalAlignment.Bottom,
                        _ => null,
                    };
                    break;
                case "noEndnote": s.NoEndnotes = RunProperties.OnOff(r); break;
                case "titlePg": s.TitlePage = RunProperties.OnOff(r); sawTitlePg = true; break;
                case "textDirection": s.TextDirection = TableXml.ParseTextDirection(Attr(r)); break;
                case "bidi": s.RightToLeft = RunProperties.OnOff(r); break;
                case "rtlGutter": s.RightToLeftGutter = RunProperties.OnOff(r); break;
                case "docGrid":
                    s.Grid = new DocumentGrid
                    {
                        Type = r.GetAttribute("type", W) switch
                        {
                            "default" => DocumentGridType.Default,
                            "lines" => DocumentGridType.Lines,
                            "linesAndChars" => DocumentGridType.LinesAndChars,
                            "snapToChars" => DocumentGridType.SnapToChars,
                            _ => null,
                        },
                        LinePitchPt = RunProperties.FromTwips(r.GetAttribute("linePitch", W)),
                        CharacterSpace = ParseInt(r.GetAttribute("charSpace", W)),
                    };
                    break;
                default:
                    // printerSettings (a binary part), sectPrChange (history): not kept.
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        // Absent / val="0" titlePg: decided by the loader (false when the section has first-page parts).
        if (s.TitlePage == false || !sawTitlePg) s.TitlePage = sawTitlePg ? false : null;
        return (s, refs);
    }

    private static void ReadPageSize(XmlReader r, PageSetup p)
    {
        var wTw = ParseInt(r.GetAttribute("w", W));
        var hTw = ParseInt(r.GetAttribute("h", W));
        bool landscape = r.GetAttribute("orient", W) == "landscape";
        // An unusable size states nothing: orientation and paper code are taken only with a valid size.
        if (wTw is not int w || hTw is not int h || w <= 0 || h <= 0 || w > 31680 || h > 31680) return;
        p.PaperCode = ParseInt(r.GetAttribute("code", W));
        p.Orientation = landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
        // Portrait dimensions as the model holds them.
        var (pw, ph) = landscape ? (h, w) : (w, h);
        var code = p.PaperCode;
        foreach (var size in new[] { PageSize.A4, PageSize.Letter, PageSize.Legal, PageSize.A3, PageSize.A5, PageSize.B5, PageSize.Executive, PageSize.Tabloid })
        {
            var probe = new PageSetup { Size = size };
            if (probe.PortraitTwips == (pw, ph)) { p.Size = size; p.PaperCode = code; p.SizeStated = true; return; }
        }
        p.Size = PageSize.Custom;
        p.CustomWidthPt = pw / 20.0;
        p.CustomHeightPt = ph / 20.0;
        p.PaperCode = code;
        p.SizeStated = true;
    }

    private static double? NonNegative(double? v) => v is double d && d >= 0 ? d : null;

    private static int? ParseInt(string? v)
        => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static string? Attr(XmlReader r) => r.GetAttribute("val", W);

    private static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement(name, W);
        x.WriteAttributeString("val", W, value);
        x.WriteEndElement();
    }

    private static void Toggle(XmlWriter x, string name, bool? value)
    {
        if (value is not bool b) return;
        x.WriteStartElement(name, W);
        if (!b) x.WriteAttributeString("val", W, "0");
        x.WriteEndElement();
    }
}
