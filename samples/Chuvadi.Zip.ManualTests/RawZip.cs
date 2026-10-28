using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Zip.Internal;

namespace Chuvadi.Zip.ManualTests
{
    /// <summary>Hand-rolled zip builder used to craft malformed / hostile / legacy archives that the real writer refuses to produce.</summary>
    internal sealed class RawEntry
    {
        public byte[] Name = Array.Empty<byte>();
        public byte[]? LocalName;                 // differs from Name to simulate header mismatch
        public byte[] Data = Array.Empty<byte>();  // bytes exactly as stored in the archive
        public ushort Method;
        public uint Crc;
        public long USize;
        public long? DeclaredCSize;
        public ushort Flags;
        public byte[] LocalExtra = Array.Empty<byte>();
        public byte[] CdExtra = Array.Empty<byte>();
        public uint ExtAttr;
        public ushort VersionMade = 20;
        public ushort DosTime = 0x6000;
        public ushort DosDate = 0x5A21;
        public long? OffsetOverride;
        public byte[] Comment = Array.Empty<byte>();
    }

    internal static class RawZip
    {
        public static RawEntry Stored(string name, byte[] data, Encoding? enc = null) => new()
        {
            Name = (enc ?? new UTF8Encoding(false)).GetBytes(name), Data = data, Method = 0, Crc = Crc32.Update(0, data), USize = data.Length,
        };

        public static RawEntry Deflated(string name, byte[] data, long? declaredSize = null)
        {
            using var ms = new MemoryStream();
            using (var d = new DeflateStream(ms, CompressionLevel.Optimal, true)) d.Write(data, 0, data.Length);
            return new RawEntry
            {
                Name = Encoding.UTF8.GetBytes(name), Data = ms.ToArray(), Method = 8, Crc = Crc32.Update(0, data), USize = declaredSize ?? data.Length,
            };
        }

        public static RawEntry ZipCryptoStored(string name, byte[] data, string password)
        {
            uint k0 = 305419896, k1 = 591751049, k2 = 878082192;
            void Upd(byte c)
            {
                k0 = Crc32.UpdateRaw(k0, c);
                k1 = (k1 + (k0 & 0xFF)) * 134775813u + 1;
                k2 = Crc32.UpdateRaw(k2, (byte)(k1 >> 24));
            }
            foreach (var b in Encoding.UTF8.GetBytes(password)) Upd(b);
            uint crc = Crc32.Update(0, data);
            var plain = new byte[12 + data.Length];
            new Random(7).NextBytes(plain.AsSpan(0, 11));
            plain[11] = (byte)(crc >> 24);
            data.CopyTo(plain, 12);
            var enc = new byte[plain.Length];
            for (int i = 0; i < plain.Length; i++)
            {
                uint t = (k2 & 0xFFFF) | 2;
                byte ks = (byte)((t * (t ^ 1)) >> 8);
                enc[i] = (byte)(plain[i] ^ ks);
                Upd(plain[i]);
            }
            return new RawEntry
            {
                Name = Encoding.UTF8.GetBytes(name), Data = enc, Method = 0, Crc = crc, USize = data.Length, Flags = 1,
            };
        }

        public static byte[] Build(IList<RawEntry> entries, byte[]? prefix = null, bool omitLocalSig = false)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            if (prefix is not null) bw.Write(prefix);
            long baseOff = ms.Position;
            var offsets = new List<long>();
            foreach (var e in entries)
            {
                offsets.Add(ms.Position - baseOff);
                var ln = e.LocalName ?? e.Name;
                bw.Write(omitLocalSig ? 0u : 0x04034b50u);
                bw.Write((ushort)20); bw.Write(e.Flags); bw.Write(e.Method); bw.Write(e.DosTime); bw.Write(e.DosDate);
                bw.Write(e.Crc); bw.Write((uint)e.Data.Length); bw.Write((uint)e.USize);
                bw.Write((ushort)ln.Length); bw.Write((ushort)e.LocalExtra.Length);
                bw.Write(ln); bw.Write(e.LocalExtra); bw.Write(e.Data);
            }
            long cdStart = ms.Position - baseOff;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                bw.Write(0x02014b50u); bw.Write(e.VersionMade); bw.Write((ushort)20); bw.Write(e.Flags); bw.Write(e.Method);
                bw.Write(e.DosTime); bw.Write(e.DosDate); bw.Write(e.Crc);
                bw.Write((uint)(e.DeclaredCSize ?? e.Data.Length)); bw.Write((uint)e.USize);
                bw.Write((ushort)e.Name.Length); bw.Write((ushort)e.CdExtra.Length); bw.Write((ushort)e.Comment.Length);
                bw.Write((ushort)0); bw.Write((ushort)0); bw.Write(e.ExtAttr); bw.Write((uint)(e.OffsetOverride ?? offsets[i]));
                bw.Write(e.Name); bw.Write(e.CdExtra); bw.Write(e.Comment);
            }
            long cdSize = ms.Position - baseOff - cdStart;
            bw.Write(0x06054b50u); bw.Write((ushort)0); bw.Write((ushort)0);
            bw.Write((ushort)entries.Count); bw.Write((ushort)entries.Count);
            bw.Write((uint)cdSize); bw.Write((uint)cdStart); bw.Write((ushort)0);
            return ms.ToArray();
        }
    }

    /// <summary>A write-only, non-seekable stream that discards data but counts it.</summary>
    internal sealed class DiscardStream : Stream
    {
        public long Total;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Total; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Total += count;
        public override void Write(ReadOnlySpan<byte> buffer) => Total += buffer.Length;
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>A readable but non-seekable wrapper.</summary>
    internal sealed class ForwardOnlyStream : Stream
    {
        private readonly Stream _s;
        public ForwardOnlyStream(Stream s) { _s = s; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _s.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A write-only non-seekable wrapper that records into memory.</summary>
    internal sealed class ForwardOnlyWriteStream : Stream
    {
        public readonly MemoryStream Inner = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Inner.Position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

namespace Chuvadi.Zip.ManualTests
{
    /// <summary>A write-only non-seekable file stream (used to force the data-descriptor path to disk).</summary>
    internal sealed class ForwardOnlyFileWriter : System.IO.Stream
    {
        private readonly System.IO.FileStream _fs;
        public ForwardOnlyFileWriter(string path) { _fs = new System.IO.FileStream(path, System.IO.FileMode.Create); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new System.NotSupportedException();
        public override long Position { get => _fs.Position; set => throw new System.NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => _fs.Write(buffer, offset, count);
        public override void Flush() => _fs.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new System.NotSupportedException();
        public override void SetLength(long value) => throw new System.NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _fs.Dispose(); base.Dispose(disposing); }
    }
}
