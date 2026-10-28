using System;
using System.IO;
using System.IO.Compression;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Pre-flight checks on an xlsx zip before it is opened, for the limits in
/// <see cref="XlsxReaderOptions"/>: entry count, declared sizes, compression ratio, and the
/// actual size of the manifest parts ([Content_Types].xml and .rels) that the package layer
/// reads in full. Parts read later (sheets, shared strings, styles) are counted while they
/// inflate by <see cref="ReadBudget"/>.
/// </summary>
internal static class PackageGuard
{
    private const long RatioCheckFloor = 1024 * 1024;   // ratios only matter for big entries

    /// <summary>
    /// Returns a seekable stream positioned at the start of the package (the input itself when
    /// seekable, otherwise a bounded in-memory copy).
    /// </summary>
    public static Stream Check(Stream input, XlsxReaderOptions o, ReadBudget budget)
    {
        var ct = o.CancellationToken;
        Stream s = input;
        if (!input.CanSeek)
        {
            // ZipArchive needs a seekable stream; copy, bounded by the total budget.
            var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                ms.Write(buffer, 0, read);
                if (o.MaxTotalUncompressedBytes is long cap && ms.Length > cap)
                    throw new XlsxLimitExceededException("MaxTotalUncompressedBytes", cap, ms.Length,
                        "the compressed package alone is larger than the limit.");
            }
            ms.Position = 0;
            s = ms;
        }

        long start = s.Position;
        using (var zip = new ZipArchive(s, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (o.MaxPackageEntries is int maxEntries && zip.Entries.Count > maxEntries)
                throw new XlsxLimitExceededException("MaxPackageEntries", maxEntries, zip.Entries.Count,
                    "too many entries in the zip package.");

            long declaredTotal = 0;
            foreach (var e in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                declaredTotal += e.Length;

                if (o.MaxPartBytes is long maxPart && e.Length > maxPart)
                    throw new InvalidDataException(
                        $"Decompressed size limit of {maxPart} bytes exceeded for package part '{e.FullName}' (declared {e.Length}).");

                if (o.MaxCompressionRatio is double maxRatio && e.Length > RatioCheckFloor)
                {
                    double ratio = e.CompressedLength == 0 ? double.PositiveInfinity : (double)e.Length / e.CompressedLength;
                    if (ratio > maxRatio)
                        throw new XlsxLimitExceededException("MaxCompressionRatio", maxRatio, Math.Round(ratio, 1),
                            $"entry '{e.FullName}' looks like a decompression bomb.");
                }

                // Manifest parts are read unconditionally by the package layer: verify their
                // ACTUAL inflated size here, since declared sizes can lie.
                var name = e.FullName;
                if (name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                {
                    using var es = e.Open();
                    using var bs = budget.Wrap(es, name, o.MaxPartBytes);
                    var buf = new byte[16384];
                    while (bs.Read(buf, 0, buf.Length) > 0) { ct.ThrowIfCancellationRequested(); }
                }
            }

            if (o.MaxTotalUncompressedBytes is long total && declaredTotal > total)
                throw new XlsxLimitExceededException("MaxTotalUncompressedBytes", total, declaredTotal,
                    "the zip declares more uncompressed data than allowed.");
        }
        s.Position = start;
        return s;
    }
}

/// <summary>
/// Counts bytes actually inflated across all parts read from one package, enforcing
/// <see cref="XlsxReaderOptions.MaxTotalUncompressedBytes"/> (XlsxLimitExceededException) and,
/// per part, <see cref="XlsxReaderOptions.MaxPartBytes"/> (InvalidDataException, as before).
/// </summary>
internal sealed class ReadBudget
{
    private readonly long? _total;
    private long _used;

    public ReadBudget(long? totalLimit) { _total = totalLimit; }

    public long Used => _used;

    /// <summary>Wraps a part stream so its bytes count against the budget (and the per-part cap).</summary>
    public Stream Wrap(Stream inner, string partName, long? maxPart)
        => _total is null && maxPart is null ? inner : new CountingStream(inner, this, partName, maxPart);

    private void Consume(int n, string partName)
    {
        _used += n;
        if (_total is long t && _used > t)
            throw new XlsxLimitExceededException("MaxTotalUncompressedBytes", t, _used,
                $"inflated data exceeded the limit while reading '{partName}'.");
    }

    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        private readonly ReadBudget _budget;
        private readonly string _name;
        private readonly long? _maxPart;
        private long _read;

        public CountingStream(Stream inner, ReadBudget budget, string name, long? maxPart)
        {
            _inner = inner; _budget = budget; _name = name; _maxPart = maxPart;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            Count(n);
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            int n = _inner.Read(buffer);
            Count(n);
            return n;
        }

        private void Count(int n)
        {
            if (n <= 0) return;
            _read += n;
            if (_maxPart is long m && _read > m)
                throw new InvalidDataException(
                    $"Decompressed size limit of {m} bytes exceeded for package part '{_name}'.");
            _budget.Consume(n, _name);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
