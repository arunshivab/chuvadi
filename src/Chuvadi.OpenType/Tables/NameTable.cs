using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>One record of the 'name' table.</summary>
/// <param name="PlatformId">Platform ID (0 Unicode, 1 Macintosh, 3 Windows, ...).</param>
/// <param name="EncodingId">Platform-specific encoding ID.</param>
/// <param name="LanguageId">Language ID; 0x8000 and above refer to <see cref="NameTable.LanguageTags"/>.</param>
/// <param name="NameId">Name ID (1 family, 2 subfamily, 4 full name, 6 PostScript name, ...).</param>
/// <param name="Value">The decoded string, or null when the encoding is not one this reader decodes.</param>
/// <param name="RawBytes">The stored bytes of the string.</param>
public sealed record NameRecord(int PlatformId, int EncodingId, int LanguageId, int NameId, string? Value, ReadOnlyMemory<byte> RawBytes);

/// <summary>
/// The 'name' (naming) table, versions 0 and 1. Strings are decoded for the
/// Unicode platform, the Windows platform (UTF-16BE) and Macintosh Roman;
/// other encodings keep only their raw bytes.
/// </summary>
public sealed class NameTable
{
    private readonly NameRecord[] _records;
    private readonly string[] _languageTags;

    // TRACE R-OT-0014
    internal NameTable(ReadOnlySpan<byte> s)
    {
        Version = BigEndian.U16(s, 0);
        int count = BigEndian.U16(s, 2);
        int storage = BigEndian.U16(s, 4);
        List<NameRecord> records = new List<NameRecord>(count);
        for (int i = 0; i < count; i++)
        {
            int r = 6 + (12 * i);
            if (!BigEndian.Has(s, r, 12))
            {
                break;
            }

            int platform = BigEndian.U16(s, r);
            int encoding = BigEndian.U16(s, r + 2);
            int language = BigEndian.U16(s, r + 4);
            int nameId = BigEndian.U16(s, r + 6);
            int length = BigEndian.U16(s, r + 8);
            int offset = BigEndian.U16(s, r + 10);
            ReadOnlySpan<byte> bytes = BigEndian.SliceClamped(s, storage + offset, length);
            if (bytes.Length != length)
            {
                continue;
            }

            records.Add(new NameRecord(platform, encoding, language, nameId, Decode(platform, encoding, bytes), bytes.ToArray()));
        }

        _records = records.ToArray();

        List<string> tags = new List<string>();
        if (Version >= 1)
        {
            int afterRecords = 6 + (12 * count);
            int tagCount = BigEndian.U16(s, afterRecords);
            for (int i = 0; i < tagCount; i++)
            {
                int r = afterRecords + 2 + (4 * i);
                if (!BigEndian.Has(s, r, 4))
                {
                    break;
                }

                int length = BigEndian.U16(s, r);
                int offset = BigEndian.U16(s, r + 2);
                ReadOnlySpan<byte> bytes = BigEndian.SliceClamped(s, storage + offset, length);
                tags.Add(Encoding.BigEndianUnicode.GetString(bytes));
            }
        }

        _languageTags = tags.ToArray();
    }

    /// <summary>Gets the table version (0 or 1).</summary>
    public int Version { get; }

    /// <summary>Gets the name records in stored order. Records whose string lies outside the table are left out.</summary>
    public IReadOnlyList<NameRecord> Records => _records;

    /// <summary>Gets the version 1 language-tag strings (BCP 47); record language ID 0x8000 + i refers to entry i.</summary>
    public IReadOnlyList<string> LanguageTags => _languageTags;

    /// <summary>
    /// Gets a name by ID, preferring in order: Windows Unicode US English (3, 1 or 10, 0x0409), any Windows
    /// Unicode record, any Unicode-platform record, Macintosh Roman English (1, 0, 0), then any decoded record.
    /// </summary>
    /// <param name="nameId">The name ID.</param>
    /// <returns>The string, or null when no decoded record has that ID.</returns>
    public string? GetName(int nameId)
    {
        // TRACE R-OT-0014
        string? best = null;
        int bestRank = int.MaxValue;
        foreach (NameRecord r in _records)
        {
            if (r.NameId != nameId || r.Value is null)
            {
                continue;
            }

            int rank;
            if (r.PlatformId == 3 && (r.EncodingId == 1 || r.EncodingId == 10) && r.LanguageId == 0x0409)
            {
                rank = 0;
            }
            else if (r.PlatformId == 3 && (r.EncodingId == 1 || r.EncodingId == 10))
            {
                rank = 1;
            }
            else if (r.PlatformId == 0)
            {
                rank = 2;
            }
            else if (r.PlatformId == 1 && r.EncodingId == 0 && r.LanguageId == 0)
            {
                rank = 3;
            }
            else
            {
                rank = 4;
            }

            if (rank < bestRank)
            {
                bestRank = rank;
                best = r.Value;
            }
        }

        return best;
    }

    private static string? Decode(int platform, int encoding, ReadOnlySpan<byte> bytes)
    {
        if (platform == 0 || (platform == 3 && (encoding == 0 || encoding == 1 || encoding == 10)))
        {
            return Encoding.BigEndianUnicode.GetString(bytes);
        }

        if (platform == 1 && encoding == 0)
        {
            return MacRoman.Decode(bytes);
        }

        return null;
    }
}
