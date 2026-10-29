using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// styles.xml ⇄ <see cref="Document.Styles"/> + <see cref="Document.Defaults"/>, and the
/// built-in style set a new document starts with.
/// </summary>
internal static class StylesXml
{
    private const string W = DocumentSerializer.W;

    // ---- Built-ins -------------------------------------------------------------------------

    /// <summary>
    /// Fills <paramref name="styles"/> with Chuvadi's built-in set: the same look as before, plus
    /// Word's built-in behaviour — based on / next = Normal, and headings keep with the next
    /// paragraph and keep their lines together.
    /// </summary>
    public static void AddBuiltIns(StyleCollection styles)
    {
        StyleDefinition P(string id, string name, Action<ParagraphFormat>? pf, TextFormat? rf, bool isDefault = false)
        {
            var s = new StyleDefinition(id, StyleType.Paragraph)
            {
                Name = name, IsDefault = isDefault, QuickFormat = true,
                BasedOn = isDefault ? null : "Normal",
                Next = isDefault ? null : "Normal",
                RunFormat = rf,
            };
            pf?.Invoke(s.ParagraphFormat);
            return styles.Add(s);
        }

        P("Normal", "Normal", null, null, isDefault: true);
        P("Title", "Title", f => { f.SpaceBeforePt = 0; f.SpaceAfterPt = 6; },
            new TextFormat { SizePt = 28, ColorHex = "1F3864" });
        P("Heading1", "heading 1", f => { f.SpaceBeforePt = 12; f.SpaceAfterPt = 6; f.OutlineLevel = 0; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { Bold = true, SizePt = 16, ColorHex = "2E74B5" });
        P("Heading2", "heading 2", f => { f.SpaceBeforePt = 10; f.SpaceAfterPt = 6; f.OutlineLevel = 1; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { Bold = true, SizePt = 13, ColorHex = "2E74B5" });
        P("Heading3", "heading 3", f => { f.SpaceBeforePt = 8; f.SpaceAfterPt = 6; f.OutlineLevel = 2; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { Bold = true, SizePt = 12, ColorHex = "1F4D78" });
        P("Heading4", "heading 4", f => { f.SpaceBeforePt = 6; f.SpaceAfterPt = 6; f.OutlineLevel = 3; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { Bold = true, Italic = true, SizePt = 11, ColorHex = "2E74B5" });
        P("Heading5", "heading 5", f => { f.SpaceBeforePt = 6; f.SpaceAfterPt = 6; f.OutlineLevel = 4; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { SizePt = 11, ColorHex = "2E74B5" });
        P("Heading6", "heading 6", f => { f.SpaceBeforePt = 6; f.SpaceAfterPt = 6; f.OutlineLevel = 5; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { SizePt = 11, ColorHex = "1F4D78" });
        P("Heading7", "heading 7", f => { f.SpaceBeforePt = 6; f.SpaceAfterPt = 6; f.OutlineLevel = 6; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { Italic = true, SizePt = 11, ColorHex = "1F4D78" });
        P("Heading8", "heading 8", f => { f.SpaceBeforePt = 6; f.SpaceAfterPt = 6; f.OutlineLevel = 7; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { SizePt = 10.5, ColorHex = "404040" });
        P("Heading9", "heading 9", f => { f.SpaceBeforePt = 6; f.SpaceAfterPt = 6; f.OutlineLevel = 8; f.KeepWithNext = true; f.KeepLinesTogether = true; },
            new TextFormat { Italic = true, SizePt = 10.5, ColorHex = "404040" });
        P("Quote", "Quote", f => { f.IndentLeftPt = 36; f.IndentRightPt = 36; },
            new TextFormat { Italic = true, ColorHex = "404040" });
        P("ListParagraph", "List Paragraph", f => { f.IndentLeftPt = 36; f.ContextualSpacing = true; }, null);

        styles.Add(new StyleDefinition("Hyperlink", StyleType.Character)
        {
            Name = "Hyperlink", QuickFormat = true, UiPriority = 99, UnhideWhenUsed = true,
            RunFormat = new TextFormat { ColorHex = "0563C1", Underline = true },
        });
    }

    // ---- Write -----------------------------------------------------------------------------

    public static void Write(XmlWriter x, Document doc, IEnumerable<string>? referencedStyleIds = null)
    {
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", "styles", W);

        x.WriteStartElement("docDefaults", W);
        x.WriteStartElement("rPrDefault", W);
        if (doc.Defaults.RunFormat is { } rd) RunProperties.Write(x, rd);
        x.WriteEndElement();
        x.WriteStartElement("pPrDefault", W);
        if (!doc.Defaults.ParagraphFormat.IsDefault)
        {
            x.WriteStartElement("pPr", W);
            ParagraphProperties.WriteBody(x, doc.Defaults.ParagraphFormat);
            x.WriteEndElement();
        }
        x.WriteEndElement();
        x.WriteEndElement(); // docDefaults

        foreach (var s in doc.Styles) WriteStyle(x, s);

        // Built-in styles the content refers to but the collection lacks (typical after loading a
        // Word file, whose styles.xml lists only styles in use) are added to the OUTPUT so every
        // reference resolves; the document's own collection is not changed.
        if (referencedStyleIds is not null)
        {
            var builtIns = new StyleCollection();
            AddBuiltIns(builtIns);
            var pending = new Queue<string>(referencedStyleIds);
            var written = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var id = pending.Dequeue();
                if (doc.Styles.Contains(id) || !written.Add(id) || !builtIns.TryGet(id, out var b)) continue;
                WriteStyle(x, b);
                if (b.BasedOn is not null) pending.Enqueue(b.BasedOn);
                if (b.Next is not null) pending.Enqueue(b.Next);
            }
        }

        x.WriteEndElement();
        x.WriteEndDocument();
    }

    /// <summary>CT_Style child order: name, aliases, basedOn, next, link, autoRedefine, hidden, uiPriority, semiHidden, unhideWhenUsed, qFormat, locked, pPr, rPr.</summary>
    private static void WriteStyle(XmlWriter x, StyleDefinition s)
    {
        x.WriteStartElement("style", W);
        x.WriteAttributeString("type", W, s.Type switch
        {
            StyleType.Character => "character",
            StyleType.Table => "table",
            StyleType.Numbering => "numbering",
            _ => "paragraph",
        });
        if (s.IsDefault) x.WriteAttributeString("default", W, "1");
        if (s.IsCustom) x.WriteAttributeString("customStyle", W, "1");
        x.WriteAttributeString("styleId", W, s.StyleId);

        Val(x, "name", s.Name ?? s.StyleId);
        if (s.Aliases is not null) Val(x, "aliases", s.Aliases);
        if (s.BasedOn is not null) Val(x, "basedOn", s.BasedOn);
        if (s.Next is not null) Val(x, "next", s.Next);
        if (s.Link is not null) Val(x, "link", s.Link);
        if (s.AutoRedefine) Empty(x, "autoRedefine");
        if (s.Hidden) Empty(x, "hidden");
        if (s.UiPriority is int ui) Val(x, "uiPriority", ui.ToString(CultureInfo.InvariantCulture));
        if (s.SemiHidden) Empty(x, "semiHidden");
        if (s.UnhideWhenUsed) Empty(x, "unhideWhenUsed");
        if (s.QuickFormat) Empty(x, "qFormat");
        if (s.Locked) Empty(x, "locked");

        if (s.Type != StyleType.Character && (!s.ParagraphFormat.IsDefault || s.RawNumberingXml is not null))
        {
            x.WriteStartElement("pPr", W);
            ParagraphProperties.WriteBody(x, s.ParagraphFormat, rawNumberingXml: s.RawNumberingXml);
            x.WriteEndElement();
        }
        if (s.RunFormat is { } rf) RunProperties.Write(x, rf);
        if (s.Type == StyleType.Table)
        {
            // CT_Style tail: tblPr, trPr, tcPr, tblStylePr* (in ST_TblStyleOverrideType order).
            TableXml.WriteStyleTablePr(x, s.TableFormat);
            TableXml.WriteStyleRowPr(x, s.RowFormat);
            TableXml.WriteStyleCellPr(x, s.CellFormat);
            foreach (var (region, _) in RegionTokens)
            {
                if (!s.ConditionalFormats.TryGetValue(region, out var cf)) continue;
                x.WriteStartElement("tblStylePr", W);
                x.WriteAttributeString("type", W, RegionToken(region));
                if (!cf.ParagraphFormat.IsDefault)
                {
                    x.WriteStartElement("pPr", W);
                    ParagraphProperties.WriteBody(x, cf.ParagraphFormat);
                    x.WriteEndElement();
                }
                if (cf.RunFormat is { } crf) RunProperties.Write(x, crf);
                TableXml.WriteStyleTablePr(x, cf.TableFormat);
                TableXml.WriteStyleRowPr(x, cf.RowFormat);
                TableXml.WriteStyleCellPr(x, cf.CellFormat);
                x.WriteEndElement();
            }
        }
        x.WriteEndElement();
    }

    // ---- Read ------------------------------------------------------------------------------

    /// <summary>Replaces <paramref name="doc"/>'s styles and defaults with those in a styles.xml part.</summary>
    public static void Read(XmlReader r, Document doc)
    {
        doc.Styles.Clear();
        doc.Defaults.RunFormat = null;
        if (!r.Read()) return; // <w:styles>
        int childDepth = r.Depth + 1;

        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "docDefaults":
                    using (var dd = r.ReadSubtree()) ReadDefaults(dd, doc.Defaults);
                    break;
                case "style":
                    using (var st = r.ReadSubtree())
                    {
                        // First definition of an id wins (later duplicates are ignored) — linear time
                        // even for hostile files with many duplicate ids.
                        var style = ReadStyle(st);
                        if (style is not null && !doc.Styles.Contains(style.StyleId)) doc.Styles.Add(style);
                    }
                    break;
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { } // latentStyles etc.
                    break;
            }
        }
    }

    private static void ReadDefaults(XmlReader r, DocumentDefaults defaults)
    {
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != W) continue;
            if (r.LocalName == "rPr")
            {
                using var rp = r.ReadSubtree();
                defaults.RunFormat = RunProperties.Read(rp, out _);
            }
            else if (r.LocalName == "pPr")
            {
                using var pp = r.ReadSubtree();
                var scratch = new Paragraph();
                ParagraphProperties.Read(pp, scratch);
                defaults.ParagraphFormat.ApplyFrom(scratch.Format);
            }
        }
    }

    private static StyleDefinition? ReadStyle(XmlReader r)
    {
        if (!r.Read()) return null; // <w:style>
        var id = r.GetAttribute("styleId", W);
        if (string.IsNullOrWhiteSpace(id)) return null;
        var type = r.GetAttribute("type", W) switch
        {
            "character" => StyleType.Character,
            "table" => StyleType.Table,
            "numbering" => StyleType.Numbering,
            _ => StyleType.Paragraph,
        };
        var s = new StyleDefinition(id, type)
        {
            IsDefault = IsOn(r.GetAttribute("default", W)),
            IsCustom = IsOn(r.GetAttribute("customStyle", W)),
        };
        int childDepth = r.Depth + 1;

        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "name": s.Name = Attr(r); break;
                case "aliases": s.Aliases = Attr(r); break;
                case "basedOn": s.BasedOn = Attr(r); break;
                case "next": s.Next = Attr(r); break;
                case "link": s.Link = Attr(r); break;
                case "autoRedefine": s.AutoRedefine = RunProperties.OnOff(r) == true; break;
                case "hidden": s.Hidden = RunProperties.OnOff(r) == true; break;
                case "uiPriority":
                    s.UiPriority = int.TryParse(Attr(r), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ui) ? ui : null;
                    break;
                case "semiHidden": s.SemiHidden = RunProperties.OnOff(r) == true; break;
                case "unhideWhenUsed": s.UnhideWhenUsed = RunProperties.OnOff(r) == true; break;
                case "qFormat": s.QuickFormat = RunProperties.OnOff(r) == true; break;
                case "locked": s.Locked = RunProperties.OnOff(r) == true; break;
                case "pPr":
                    string pPrXml;
                    using (var pp = r.ReadSubtree()) { pp.Read(); pPrXml = pp.ReadOuterXml(); }
                    {
                        using var parse = Reparse(pPrXml);
                        var scratch = new Paragraph();
                        ParagraphProperties.Read(parse, scratch);
                        s.ParagraphFormat.ApplyFrom(scratch.Format);
                    }
                    s.RawNumberingXml = ExtractChild(pPrXml, "numPr");
                    break;
                case "rPr":
                    using (var rp = r.ReadSubtree()) s.RunFormat = RunProperties.Read(rp, out _);
                    break;
                case "tblPr":
                    using (var tp = r.ReadSubtree()) TableXml.CopyTableFormat(TableXml.ReadStyleTablePr(tp), s.TableFormat);
                    break;
                case "trPr":
                    using (var tr = r.ReadSubtree()) TableXml.CopyRowFormat(TableXml.ReadStyleRowPr(tr), s.RowFormat);
                    break;
                case "tcPr":
                    using (var tc = r.ReadSubtree()) s.CellFormat.ApplyFrom(TableXml.ReadStyleCellPr(tc));
                    break;
                case "tblStylePr":
                    var region = ParseRegion(r.GetAttribute("type", W));
                    using (var cp = r.ReadSubtree())
                    {
                        if (region is TableRegion reg) ReadConditional(cp, s.Conditional(reg));
                    }
                    break;
                default:
                    // rsid, personal…: not part of the model.
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
        return s;
    }

    // ---- Table-style conditional formats -------------------------------------------------------

    private static void ReadConditional(XmlReader r, TableConditionalFormat cf)
    {
        if (!r.Read()) return;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != childDepth || r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "pPr":
                    using (var pp = r.ReadSubtree())
                    {
                        var scratch = new Paragraph();
                        ParagraphProperties.Read(pp, scratch);
                        cf.ParagraphFormat.ApplyFrom(scratch.Format);
                    }
                    break;
                case "rPr":
                    using (var rp = r.ReadSubtree()) cf.RunFormat = RunProperties.Read(rp, out _);
                    break;
                case "tblPr":
                    using (var tp = r.ReadSubtree()) TableXml.CopyTableFormat(TableXml.ReadStyleTablePr(tp), cf.TableFormat);
                    break;
                case "trPr":
                    using (var tr = r.ReadSubtree()) TableXml.CopyRowFormat(TableXml.ReadStyleRowPr(tr), cf.RowFormat);
                    break;
                case "tcPr":
                    using (var tc = r.ReadSubtree()) cf.CellFormat.ApplyFrom(TableXml.ReadStyleCellPr(tc));
                    break;
                default:
                    if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                    break;
            }
        }
    }

    /// <summary>Region tokens in ST_TblStyleOverrideType order — the order Word writes tblStylePr elements.</summary>
    private static readonly (TableRegion Region, string Token)[] RegionTokens =
    {
        (TableRegion.WholeTable, "wholeTable"), (TableRegion.FirstRow, "firstRow"), (TableRegion.LastRow, "lastRow"),
        (TableRegion.FirstColumn, "firstCol"), (TableRegion.LastColumn, "lastCol"), (TableRegion.OddColumnBand, "band1Vert"),
        (TableRegion.EvenColumnBand, "band2Vert"), (TableRegion.OddRowBand, "band1Horz"), (TableRegion.EvenRowBand, "band2Horz"),
        (TableRegion.TopRightCell, "neCell"), (TableRegion.TopLeftCell, "nwCell"), (TableRegion.BottomRightCell, "seCell"),
        (TableRegion.BottomLeftCell, "swCell"),
    };

    private static string RegionToken(TableRegion r)
    {
        foreach (var (region, token) in RegionTokens) if (region == r) return token;
        throw new ArgumentOutOfRangeException(nameof(r), r, "Undefined TableRegion value.");
    }

    private static TableRegion? ParseRegion(string? v)
    {
        foreach (var (region, token) in RegionTokens) if (token == v) return region;
        return null;
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>Parses an XML fragment captured from an already-guarded part (DTDs prohibited).</summary>
    private static XmlReader Reparse(string xml)
        => XmlReader.Create(new System.IO.StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            IgnoreComments = true,
        });

    /// <summary>The outer XML of the first direct w:<paramref name="localName"/> child of a fragment, or null.</summary>
    private static string? ExtractChild(string xml, string localName)
    {
        using var r = Reparse(xml);
        if (!r.Read()) return null;
        int childDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.Depth == childDepth && r.NamespaceURI == W && r.LocalName == localName)
                return r.ReadOuterXml();
        }
        return null;
    }

    /// <summary>Writes a verbatim XML fragment at the current position.</summary>
    internal static void WriteFragment(XmlWriter x, string xml)
    {
        using var r = Reparse(xml);
        r.MoveToContent();
        x.WriteNode(r, defattr: true);
    }

    private static string? Attr(XmlReader r) => r.GetAttribute("val", W);

    private static bool IsOn(string? v) => v is "1" or "true" or "on";

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement(name, W);
        x.WriteAttributeString("val", W, value);
        x.WriteEndElement();
    }

    private static void Empty(XmlWriter x, string name)
    {
        x.WriteStartElement(name, W);
        x.WriteEndElement();
    }
}
