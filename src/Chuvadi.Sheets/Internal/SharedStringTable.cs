using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Accumulates the workbook's shared string table during writes. xlsx stores strings in
/// a central deduplicated table (sharedStrings.xml) and references them from cells by
/// integer index. This keeps file size down when strings repeat (which they typically do
/// in tabular data — column headers, status values, category names, etc.).
///
/// Usage during write:
///   var sst = new SharedStringTable();
///   int id = sst.GetOrAdd("Hello");   // returns 0 the first time, same id for repeats
///   ...
///   using (var stream = ...) sst.WriteTo(stream);
///
/// Thread-safety: NOT thread-safe. The streaming writer is single-threaded by design.
/// </summary>
internal sealed class SharedStringTable
{
    // Ordinal comparison is critical: Excel and OOXML treat shared strings as byte-for-byte
    // equal, not culturally equal. "Straße" and "Strasse" are different strings in xlsx.
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly List<string> _ordered = new();

    /// <summary>
    /// Number of unique strings currently in the table.
    /// </summary>
    public int Count => _ordered.Count;

    /// <summary>
    /// Total number of times <see cref="GetOrAdd"/> has been called (including repeats).
    /// xlsx's sharedStrings.xml records this as the &lt;sst count="..."&gt; attribute and
    /// it must match the actual count of string-cell references in the workbook.
    /// </summary>
    public int TotalReferences { get; private set; }

    /// <summary>
    /// Returns the existing index for a string, or adds it and returns the new index.
    /// </summary>
    public int GetOrAdd(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        TotalReferences++;

        if (_index.TryGetValue(value, out var id))
            return id;

        id = _ordered.Count;
        _ordered.Add(value);
        _index[value] = id;
        return id;
    }

    /// <summary>
    /// Reads back a string by index. Used by the reader path; not called during write.
    /// </summary>
    public string GetAt(int index) => _ordered[index];

    /// <summary>
    /// Writes a complete sharedStrings.xml document to the given stream. The stream is
    /// flushed and left open; the caller owns its lifetime.
    /// </summary>
    public void WriteTo(Stream output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));

        using var writer = XmlWriter.Create(output, MakeSettings());

        writer.WriteStartDocument(standalone: true);
        writer.WriteStartElement("sst", SsNs);

        // Both attributes are required by OOXML. "count" is total references (sum of usages),
        // "uniqueCount" is the number of distinct strings (== _ordered.Count).
        writer.WriteAttributeString("count", TotalReferences.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteAttributeString("uniqueCount", _ordered.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var s in _ordered)
        {
            writer.WriteStartElement("si", SsNs);
            writer.WriteStartElement("t", SsNs);

            // Preserve leading/trailing whitespace, which Excel otherwise strips.
            // xml:space="preserve" is the OOXML-canonical way.
            if (HasSignificantWhitespace(s))
                writer.WriteAttributeString("xml", "space", null, "preserve");

            writer.WriteString(s);
            writer.WriteEndElement(); // </t>
            writer.WriteEndElement(); // </si>
        }

        writer.WriteEndElement(); // </sst>
        writer.WriteEndDocument();
        writer.Flush();
    }

    // ---- Constants -----------------------------------------------------------------

    /// <summary>SpreadsheetML namespace, same as used by sheet XML and workbook.xml.</summary>
    private const string SsNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---- Helpers -------------------------------------------------------------------

    private static XmlWriterSettings MakeSettings() => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = false,
    };

    /// <summary>
    /// True if the string has leading/trailing whitespace or starts/ends in a way that XML
    /// would otherwise normalize away. We add xml:space="preserve" in those cases.
    /// </summary>
    private static bool HasSignificantWhitespace(string s)
    {
        if (s.Length == 0) return false;
        return char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[s.Length - 1]);
    }
}
