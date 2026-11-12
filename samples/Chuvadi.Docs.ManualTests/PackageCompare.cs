using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Compares two saved packages (D-321, option B). Two saves of one document differ in two clocks only:
/// <list type="bullet">
/// <item>each zip entry's last-modified time (the time of the save, 2-second resolution);</item>
/// <item><c>dcterms:created</c> and <c>dcterms:modified</c> in <c>docProps/core.xml</c>, which every save sets
/// to the current time to the second (as Word does). A second boundary can fall between two saves.</item>
/// </list>
/// Everything else must match byte for byte: entry order and names, every header field (flags such as the
/// data-descriptor bit, method, CRC, sizes), the compressed data of every part, data descriptors, the central
/// directory and the end record. The one exception is <c>docProps/core.xml</c>: its data is inflated and
/// compared as text with only the two dates masked, and its CRC and size fields (and the offsets of the
/// entries after it) may differ only because of that. The entries are found through the central directory,
/// not by searching.
/// </summary>
internal static class PackageCompare
{
    private const string CorePart = "docProps/core.xml";

    private static readonly Regex CoreDates = new(
        @"(<dcterms:(created|modified)\b[^>]*>)[^<]*(</dcterms:\2>)", RegexOptions.CultureInvariant);

    private sealed class Entry
    {
        public string Name = "";
        public int Central;      // offset of the central directory record
        public int CentralLength;
        public int Local;        // offset of the local header
        public int LocalLength;  // 30 + name + extra
        public int Method;
        public int CompressedSize;
        public int End;          // where the next local header (or the central directory) starts
    }

    private sealed class Layout
    {
        public List<Entry> Entries = new();
        public int CentralStart;
        public int CentralEnd;
        public int Eocd;
    }

    /// <summary>
    /// Null when the two packages are the same apart from the save clocks; otherwise where they first differ,
    /// for example "differs in word/document.xml at byte 1,234 of its data".
    /// </summary>
    public static string? Difference(byte[] a, byte[] b)
    {
        var la = Parse(a, out var ea);
        if (la is null) return "the first package is not a zip this comparison reads: " + ea;
        var lb = Parse(b, out var eb);
        if (lb is null) return "the second package is not a zip this comparison reads: " + eb;

        if (la.Entries.Count != lb.Entries.Count)
            return $"{la.Entries.Count:N0} entries against {lb.Entries.Count:N0}";
        int firstA = la.Entries.Count > 0 ? la.Entries[0].Local : la.CentralStart;
        int firstB = lb.Entries.Count > 0 ? lb.Entries[0].Local : lb.CentralStart;
        if (Diff(a, 0, firstA, b, 0, firstB, null) is int p0)
            return $"differs before the first entry at byte {p0:N0}";

        for (int i = 0; i < la.Entries.Count; i++)
        {
            var x = la.Entries[i];
            var y = lb.Entries[i];
            if (x.Name != y.Name) return $"entry {i + 1} is {x.Name} against {y.Name}";
            bool core = x.Name == CorePart;
            var name = x.Name;

            // Local header: the time (bytes 10-13) is masked; for core.xml also its CRC and sizes (14-25).
            var localMask = core ? new[] { (10, 4), (14, 12) } : new[] { (10, 4) };
            if (Diff(a, x.Local, x.LocalLength, b, y.Local, y.LocalLength, localMask) is int p1)
                return $"differs in the local header of {name} at byte {p1:N0}";

            int dataA = x.Local + x.LocalLength, dataB = y.Local + y.LocalLength;
            if (!core)
            {
                if (Diff(a, dataA, x.CompressedSize, b, dataB, y.CompressedSize, null) is int p2)
                    return $"differs in {name} at byte {p2:N0} of its data";
                if (Diff(a, dataA + x.CompressedSize, x.End - dataA - x.CompressedSize, b, dataB + y.CompressedSize, y.End - dataB - y.CompressedSize, null) is int p3)
                    return $"differs after the data of {name} (data descriptor) at byte {p3:N0}";
            }
            else
            {
                string ta, tb;
                try
                {
                    ta = Inflate(a, dataA, x.CompressedSize, x.Method);
                    tb = Inflate(b, dataB, y.CompressedSize, y.Method);
                }
                catch (Exception e) when (e is InvalidDataException or NotSupportedException or DecoderFallbackException)
                {
                    return $"differs in {name}: its data does not inflate ({e.Message})";
                }
                var ma = CoreDates.Replace(ta, "$1*$3");
                var mb = CoreDates.Replace(tb, "$1*$3");
                if (ma != mb)
                {
                    int k = 0;
                    while (k < ma.Length && k < mb.Length && ma[k] == mb[k]) k++;
                    return $"differs in {name} (inflated, the two dates masked) at character {k:N0}";
                }
                // A data descriptor holds only the CRC and sizes; it must be there in both or in neither.
                if (x.End - dataA - x.CompressedSize != y.End - dataB - y.CompressedSize)
                    return $"differs after the data of {name} (data descriptor)";
            }

            // Central record: the time (12-15) and the local header offset (42-45) are masked; for core.xml
            // also its CRC and sizes (16-27). The offsets themselves are checked through the layout above.
            var centralMask = core ? new[] { (12, 4), (16, 12), (42, 4) } : new[] { (12, 4), (42, 4) };
            if (Diff(a, x.Central, x.CentralLength, b, y.Central, y.CentralLength, centralMask) is int p4)
                return $"differs in the central directory record of {name} at byte {p4:N0}";
        }

        // Anything between the central directory and the end record, then the end record itself with the
        // central directory offset (16-19) masked.
        if (Diff(a, la.CentralEnd, la.Eocd - la.CentralEnd, b, lb.CentralEnd, lb.Eocd - lb.CentralEnd, null) is int p5)
            return $"differs after the central directory at byte {p5:N0}";
        if (Diff(a, la.Eocd, a.Length - la.Eocd, b, lb.Eocd, b.Length - lb.Eocd, new[] { (16, 4) }) is int p6)
            return $"differs in the end of central directory record at byte {p6:N0}";
        return null;
    }

    /// <summary>The first differing byte of two ranges (relative to their starts), ignoring the masked ranges; null when equal.</summary>
    private static int? Diff(byte[] a, int sa, int na, byte[] b, int sb, int nb, (int Start, int Length)[]? mask)
    {
        int n = Math.Min(na, nb);
        for (int k = 0; k < n; k++)
        {
            if (a[sa + k] == b[sb + k]) continue;
            if (mask is not null && mask.Any(m => k >= m.Start && k < m.Start + m.Length)) continue;
            return k;
        }
        return na == nb ? null : n;
    }

    private static string Inflate(byte[] zip, int start, int length, int method)
    {
        var raw = new MemoryStream(zip, start, length, writable: false);
        Stream data = method switch
        {
            0 => raw,
            8 => new DeflateStream(raw, CompressionMode.Decompress),
            _ => throw new NotSupportedException("compression method " + method),
        };
        using (data)
        {
            var output = new MemoryStream();
            data.CopyTo(output);
            return new UTF8Encoding(false, true).GetString(output.ToArray());
        }
    }

    private static Layout? Parse(byte[] zip, out string error)
    {
        error = "";
        int eocd = -1;
        for (int i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65535); i--)
            if (BitConverter.ToUInt32(zip, i) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) { error = "no end of central directory record"; return null; }
        int count = BitConverter.ToUInt16(zip, eocd + 10);
        uint cdSize = BitConverter.ToUInt32(zip, eocd + 12);
        uint cdStart = BitConverter.ToUInt32(zip, eocd + 16);
        if (count == 0xFFFF || cdSize == 0xFFFFFFFF || cdStart == 0xFFFFFFFF) { error = "zip64"; return null; }

        var layout = new Layout { CentralStart = (int)cdStart, Eocd = eocd };
        int cd = (int)cdStart;
        for (int n = 0; n < count; n++)
        {
            if (cd + 46 > eocd || BitConverter.ToUInt32(zip, cd) != 0x02014b50) { error = $"central record {n + 1}"; return null; }
            int nameLength = BitConverter.ToUInt16(zip, cd + 28);
            int length = 46 + nameLength + BitConverter.ToUInt16(zip, cd + 30) + BitConverter.ToUInt16(zip, cd + 32);
            uint compressed = BitConverter.ToUInt32(zip, cd + 20);
            uint local = BitConverter.ToUInt32(zip, cd + 42);
            if (compressed == 0xFFFFFFFF || local == 0xFFFFFFFF) { error = "zip64"; return null; }
            if (local + 30 > cdStart || BitConverter.ToUInt32(zip, (int)local) != 0x04034b50) { error = $"local header {n + 1}"; return null; }
            var e = new Entry
            {
                Name = Encoding.UTF8.GetString(zip, cd + 46, nameLength),
                Central = cd,
                CentralLength = length,
                Local = (int)local,
                LocalLength = 30 + BitConverter.ToUInt16(zip, (int)local + 26) + BitConverter.ToUInt16(zip, (int)local + 28),
                Method = BitConverter.ToUInt16(zip, cd + 10),
                CompressedSize = (int)compressed,
            };
            layout.Entries.Add(e);
            cd += length;
        }
        layout.CentralEnd = cd;
        if (cd > eocd) { error = "central directory overruns the end record"; return null; }

        // The entries must be laid out in central directory order, back to back, so every byte belongs to
        // exactly one compared range.
        for (int n = 0; n < layout.Entries.Count; n++)
        {
            var e = layout.Entries[n];
            e.End = n + 1 < layout.Entries.Count ? layout.Entries[n + 1].Local : layout.CentralStart;
            if (e.End < e.Local + e.LocalLength + e.CompressedSize) { error = $"{e.Name} is not followed by the next entry"; return null; }
        }
        return layout;
    }
}
