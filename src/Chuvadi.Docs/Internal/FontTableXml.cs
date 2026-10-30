using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>word/fontTable.xml and embedded (obfuscated) font parts.</summary>
internal static class FontTableXml
{
    private const string W = DocumentSerializer.W;
    private const string R = DocumentSerializer.R;
    internal const string ObfuscatedFontType = "application/vnd.openxmlformats-officedocument.obfuscatedFont";

    private static readonly (EmbeddedFontStyle Style, string Element)[] Embeds =
    {
        (EmbeddedFontStyle.Regular, "embedRegular"), (EmbeddedFontStyle.Bold, "embedBold"),
        (EmbeddedFontStyle.Italic, "embedItalic"), (EmbeddedFontStyle.BoldItalic, "embedBoldItalic"),
    };

    /// <summary>
    /// ECMA-376 font obfuscation (it is its own inverse): the first 32 bytes are XORed with the
    /// key's 16 bytes, taken from the GUID string's hex pairs in reverse order.
    /// </summary>
    public static byte[] Obfuscate(byte[] data, Guid key)
    {
        var hex = key.ToString("N");
        var g = new byte[16];
        for (int i = 0; i < 16; i++) g[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var result = (byte[])data.Clone();
        for (int i = 0; i < 32 && i < result.Length; i++) result[i] ^= g[15 - (i % 16)];
        return result;
    }

    /// <summary>Reads the font table (reader on its root). <paramref name="readEmbed"/> returns a part's bytes and content type by relationship id.</summary>
    public static void Read(XmlReader r, List<FontInfo> fonts, Func<string, (byte[] Bytes, string ContentType)?> readEmbed)
    {
        if (!r.Read()) return;
        r.MoveToContent();
        int fontDepth = r.Depth + 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != fontDepth || r.NamespaceURI != W || r.LocalName != "font") continue;
            var name = r.GetAttribute("name", W);
            if (string.IsNullOrWhiteSpace(name) || fonts.Count >= 1024)
            {
                if (!r.IsEmptyElement) using (r.ReadSubtree()) { }
                continue;
            }
            var info = new FontInfo(name);
            fonts.Add(info);
            if (r.IsEmptyElement) continue;
            using var f = r.ReadSubtree();
            f.Read();
            int childDepth = f.Depth + 1;
            while (f.Read())
            {
                if (f.NodeType != XmlNodeType.Element || f.Depth != childDepth || f.NamespaceURI != W) continue;
                var v = f.GetAttribute("val", W);
                switch (f.LocalName)
                {
                    case "altName": info.AltName = v; break;
                    case "panose1": info.Panose = v; break;
                    case "charset": info.Charset = v; break;
                    case "family": info.Family = v; break;
                    case "pitch": info.Pitch = v; break;
                    case "notTrueType": info.NotTrueType = RunProperties.OnOff(f) == true; break;
                    case "sig":
                        info.Signature = new FontSignature(f.GetAttribute("usb0", W) ?? "", f.GetAttribute("usb1", W) ?? "",
                            f.GetAttribute("usb2", W) ?? "", f.GetAttribute("usb3", W) ?? "", f.GetAttribute("csb0", W) ?? "", f.GetAttribute("csb1", W) ?? "");
                        break;
                    default:
                        foreach (var (style, element) in Embeds)
                        {
                            if (f.LocalName != element) continue;
                            var id = f.GetAttribute("id", R);
                            if (id is null || readEmbed(id) is not { } part) break;
                            Guid? key = Guid.TryParse(f.GetAttribute("fontKey", W), out var k) ? k : null;
                            var data = part.ContentType.Equals(ObfuscatedFontType, StringComparison.OrdinalIgnoreCase) && key is Guid kk
                                ? Obfuscate(part.Bytes, kk) : part.Bytes;
                            info.Embedded.Add(new EmbeddedFont(style, data) { FontKey = key, Subsetted = RunProperties.OnOff(f, "subsetted") == true });
                            break;
                        }
                        break;
                }
            }
        }
    }

    /// <summary>Writes the font table; <paramref name="addEmbed"/> stores an obfuscated font part and returns its relationship id.</summary>
    public static void Write(XmlWriter x, IReadOnlyList<FontInfo> fonts, Func<byte[], string> addEmbed)
    {
        x.WriteStartDocument(standalone: true);
        x.WriteStartElement("w", "fonts", W);
        x.WriteAttributeString("xmlns", "r", null, R);
        foreach (var info in fonts)
        {
            x.WriteStartElement("font", W);
            x.WriteAttributeString("name", W, info.Name);
            // CT_Font order: altName, panose1, charset, family, notTrueType, pitch, sig, embed*.
            void Val(string name, string? v)
            {
                if (v is null) return;
                x.WriteStartElement(name, W);
                x.WriteAttributeString("val", W, v);
                x.WriteEndElement();
            }
            Val("altName", info.AltName);
            Val("panose1", info.Panose);
            Val("charset", info.Charset);
            Val("family", info.Family);
            if (info.NotTrueType) { x.WriteStartElement("notTrueType", W); x.WriteEndElement(); }
            Val("pitch", info.Pitch);
            if (info.Signature is { } sig)
            {
                x.WriteStartElement("sig", W);
                x.WriteAttributeString("usb0", W, sig.Usb0);
                x.WriteAttributeString("usb1", W, sig.Usb1);
                x.WriteAttributeString("usb2", W, sig.Usb2);
                x.WriteAttributeString("usb3", W, sig.Usb3);
                x.WriteAttributeString("csb0", W, sig.Csb0);
                x.WriteAttributeString("csb1", W, sig.Csb1);
                x.WriteEndElement();
            }
            foreach (var (style, element) in Embeds)
            {
                var e = info.Embedded.Find(m => m.Style == style);
                if (e is null) continue;
                var key = e.FontKey ?? Guid.NewGuid();
                var relId = addEmbed(Obfuscate(e.Data, key));
                x.WriteStartElement(element, W);
                x.WriteAttributeString("id", R, relId);
                x.WriteAttributeString("fontKey", W, key.ToString("B").ToUpperInvariant());
                if (e.Subsetted) x.WriteAttributeString("subsetted", W, "1");
                x.WriteEndElement();
            }
            x.WriteEndElement();
        }
        x.WriteEndElement();
        x.WriteEndDocument();
    }
}
