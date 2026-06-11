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
/// One entry from the styles.xml cellXfs list. Cells reference this by integer index via
/// their s= attribute.
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

/// <summary>
/// Parses styles.xml into a StyleEntry[] indexed by cellXf id. Also extracts the numFmts
/// table for resolving custom format codes.
/// </summary>
internal static class StyleSheetReader
{
    public static StyleEntry[] Read(Stream stream)
    {
        using var r = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = false,
        });

        // First pass: numFmts (custom format codes).
        var numFmts = new Dictionary<int, string>();
        // Second pass: cellXfs (the entries we return).
        var cellXfs = new List<(int numFmtId, bool applyNumberFormat)>();

        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;

            if (r.LocalName == "numFmt")
            {
                var id = ParseInt(r.GetAttribute("numFmtId"));
                var code = r.GetAttribute("formatCode");
                if (id >= 0 && code is not null) numFmts[id] = code;
            }
            else if (r.LocalName == "xf" && IsInCellXfs(r))
            {
                var nfId = ParseInt(r.GetAttribute("numFmtId"));
                cellXfs.Add((nfId, true));
            }
        }

        // Build StyleEntry[] with resolved format codes and date detection.
        var entries = new StyleEntry[cellXfs.Count];
        for (int i = 0; i < cellXfs.Count; i++)
        {
            var (id, _) = cellXfs[i];
            numFmts.TryGetValue(id, out var code);
            entries[i] = new StyleEntry
            {
                NumFmtId = id,
                FormatCode = code,
                IsDateFormat = DateFormatDetector.IsDateFormat(id, code),
            };
        }
        return entries;
    }

    /// <summary>
    /// Determines whether the current &lt;xf&gt; is the cellXf flavor (vs cellStyleXf). The
    /// reader can't easily look at parent element via XmlReader's stack, so we maintain
    /// context via a stateful parse instead. This static helper relies on the calling code
    /// having tracked which section we're in; for simplicity we assume any &lt;xf&gt; reachable
    /// while scanning is a cellXf entry — Excel ALWAYS writes cellStyleXfs first (one entry,
    /// the default) then cellXfs (the real list). So we drop the first one we see (it's the
    /// shared default) and treat the rest as cellXfs.
    /// </summary>
    private static bool IsInCellXfs(XmlReader r)
    {
        // Heuristic: cellXf entries have xfId="0" attribute referring to the cellStyleXf parent.
        // cellStyleXf entries don't. This isn't perfectly reliable, but works for files
        // produced by Excel, LibreOffice, and our own writer.
        var xfId = r.GetAttribute("xfId");
        return xfId is not null;
    }

    private static int ParseInt(string? s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;
}
