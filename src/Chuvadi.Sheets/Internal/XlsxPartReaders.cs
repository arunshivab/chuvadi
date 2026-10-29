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
    public static string[] Read(Stream stream, System.Threading.CancellationToken cancellation = default)
        => Read(stream, cancellation, out _);

    /// <summary>
    /// Reads the table. <paramref name="rich"/> receives the runs of rich items (&lt;si&gt; with
    /// &lt;r&gt; runs), parallel to the result, or null when no item is rich. The plain text of
    /// an item is its &lt;t&gt; text or its runs' text joined; phonetic runs (&lt;rPh&gt;) are not part of it.
    /// </summary>
    public static string[] Read(Stream stream, System.Threading.CancellationToken cancellation, out RichText?[]? rich)
    {
        using var r = XmlReader.Create(stream, new XmlReaderSettings
        {
            IgnoreWhitespace = false,  // <t xml:space="preserve"> matters
            IgnoreComments = true,
            CloseInput = false,
        });

        var result = new List<string>();
        List<RichText?>? runs = null;
        var sb = new StringBuilder();

        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.LocalName != "si") continue;

            if ((result.Count & 255) == 0) cancellation.ThrowIfCancellationRequested();
            sb.Clear();
            var rt = RichTextXml.ReadItem(r, sb);
            if (rt is not null)
            {
                if (runs is null) { runs = new List<RichText?>(result.Count + 16); for (int i = 0; i < result.Count; i++) runs.Add(null); }
                runs.Add(rt);
            }
            else runs?.Add(null);
            result.Add(sb.ToString());
        }

        rich = runs?.ToArray();
        return result.ToArray();
    }
}

