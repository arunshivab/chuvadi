using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

using Chuvadi.Internal;

/// <summary>
/// Date-detection data for one entry of the styles.xml cellXfs list, derived from the parsed
/// <see cref="CellStyleTable"/>. Cells reference this by integer index via their s= attribute.
/// </summary>
internal sealed class StyleEntry
{
    public int NumFmtId { get; init; }
    public string? FormatCode { get; init; }  // resolved from numFmts or null for built-ins we didn't store
    public bool IsDateFormat { get; init; }
}

/// <summary>Parses sharedStrings.xml into a string array indexed by SST id.</summary>
internal static class SharedStringReader
{
    public static string[] Read(Stream stream)
    {
        using var r = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = false,  // <t xml:space="preserve"> matters
            IgnoreComments = true,
            CloseInput = false,
        });

        var result = new List<string>();
        var sb = new StringBuilder();

        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.LocalName != "si") continue;

            sb.Clear();
            ExtractStringFromSi(r, sb);
            result.Add(sb.ToString());
        }

        return result.ToArray();
    }

    /// <summary>
    /// Extracts the text content of an &lt;si&gt; element. Handles both simple form
    /// (&lt;si&gt;&lt;t&gt;text&lt;/t&gt;&lt;/si&gt;) and rich-text form (multiple &lt;r&gt;
    /// runs each containing &lt;t&gt;). Ignores phonetic-run children (&lt;rPh&gt;).
    /// </summary>
    private static void ExtractStringFromSi(XmlReader r, StringBuilder sb)
    {
        // r is positioned at the <si> element. Read into it.
        if (r.IsEmptyElement) return;

        int depth = r.Depth;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth && r.LocalName == "si")
                return;

            // Skip phonetic-run content (Japanese furigana).
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "rPh")
            {
                if (!r.IsEmptyElement) r.Skip();
                continue;
            }

            // Top-level <t> (simple form) — collect its text content.
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "t")
            {
                CollectTextContent(r, sb);
                continue;
            }

            // <r> rich-text run — its <t> child holds text.
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "r")
            {
                ReadRunContent(r, sb);
                continue;
            }
        }
    }

    private static void ReadRunContent(XmlReader r, StringBuilder sb)
    {
        if (r.IsEmptyElement) return;
        int depth = r.Depth;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth && r.LocalName == "r")
                return;
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "t")
                CollectTextContent(r, sb);
        }
    }

    private static void CollectTextContent(XmlReader r, StringBuilder sb)
    {
        if (r.IsEmptyElement) return;
        int depth = r.Depth;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth)
                return;
            if (r.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                sb.Append(r.Value);
        }
    }
}

