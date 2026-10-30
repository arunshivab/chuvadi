using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>Footnote / endnote properties (w:footnotePr, w:endnotePr) and note parts.</summary>
internal static class NotesXml
{
    private const string W = DocumentSerializer.W;

    /// <summary>Writes w:footnotePr / w:endnotePr (CT_FtnProps / CT_EdnProps order: pos, numFmt, numStart, numRestart), then <paramref name="extra"/>.</summary>
    public static void Write(XmlWriter x, NoteKind kind, NoteProperties p, Action<XmlWriter>? extra = null)
    {
        if (p.IsEmpty && extra is null) return;
        x.WriteStartElement(kind == NoteKind.Footnote ? "footnotePr" : "endnotePr", W);
        // ST_EdnPos allows only sectEnd and docEnd.
        if (p.Position is NotePosition pos && (kind == NoteKind.Footnote || pos is NotePosition.SectionEnd or NotePosition.DocumentEnd)) Val(x, "pos", pos switch
        {
            NotePosition.BeneathText => "beneathText",
            NotePosition.SectionEnd => "sectEnd",
            NotePosition.DocumentEnd => "docEnd",
            _ => "pageBottom",
        });
        if (p.NumberFormat is NumberFormat nf) Val(x, "numFmt", NumberingXml.FormatToken(nf));
        else if (p.RawFormatToken is { } token)
        {
            x.WriteStartElement("numFmt", W);
            x.WriteAttributeString("val", W, token);
            if (p.RawFormatCustom is { } custom) x.WriteAttributeString("format", W, custom);
            x.WriteEndElement();
        }
        if (p.StartAt is int start && start >= 0) Val(x, "numStart", start.ToString(CultureInfo.InvariantCulture));
        if (p.Restart is NoteRestart rs) Val(x, "numRestart", rs switch
        {
            NoteRestart.EachSection => "eachSect",
            NoteRestart.EachPage => "eachPage",
            _ => "continuous",
        });
        extra?.Invoke(x);
        x.WriteEndElement();
    }

    /// <summary>Reads a w:footnotePr / w:endnotePr subtree (reader on it) into <paramref name="p"/>; separator references are skipped.</summary>
    public static void Read(XmlReader r, NoteProperties p)
    {
        if (!r.Read()) return;
        if (r.IsEmptyElement) return;
        int depth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != depth || r.NamespaceURI != W) continue;
            var v = r.GetAttribute("val", W);
            switch (r.LocalName)
            {
                case "pos":
                    p.Position = v switch
                    {
                        "pageBottom" => NotePosition.PageBottom,
                        "beneathText" => NotePosition.BeneathText,
                        "sectEnd" => NotePosition.SectionEnd,
                        "docEnd" => NotePosition.DocumentEnd,
                        _ => p.Position,
                    };
                    break;
                case "numFmt":
                    if (NumberingXml.ParseFormat(v) is NumberFormat f && r.GetAttribute("format", W) is null) { p.NumberFormat = f; p.RawFormatToken = null; }
                    else if (v is { Length: > 0 and <= 64 })
                    {
                        p.NumberFormat = null;
                        p.RawFormatToken = v;
                        p.RawFormatCustom = r.GetAttribute("format", W) is { Length: <= 256 } c ? c : null;
                    }
                    break;
                case "numStart":
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0) p.StartAt = n;
                    break;
                case "numRestart":
                    p.Restart = v switch
                    {
                        "continuous" => NoteRestart.Continuous,
                        "eachSect" => NoteRestart.EachSection,
                        "eachPage" => NoteRestart.EachPage,
                        _ => p.Restart,
                    };
                    break;
            }
        }
    }

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement(name, W);
        x.WriteAttributeString("val", W, value);
        x.WriteEndElement();
    }
}
