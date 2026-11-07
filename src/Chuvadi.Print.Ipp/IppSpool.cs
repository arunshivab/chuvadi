using System;
using System.IO;

namespace Chuvadi.Print.Ipp
{
    /// <summary>Where <see cref="IppPrinterTarget"/> holds a PWG Raster job until it is sent.</summary>
    public enum IppSpoolMode
    {
        /// <summary>A temporary file (in <see cref="IppPrinterTarget.TemporaryFolder"/>), deleted when the job ends. The default.</summary>
        TemporaryFile = 0,

        /// <summary>Memory, up to <see cref="IppPrinterTarget.MemorySpoolLimit"/>: for machines without a writable temp folder.</summary>
        Memory = 1
    }

    /// <summary>
    /// A memory stream that refuses to grow past a limit: a write that would take it past the limit throws an
    /// <see cref="IppException"/> and writes nothing.
    /// </summary>
    internal sealed class BoundedMemorySpool : MemoryStream
    {
        public BoundedMemorySpool(long limit)
        {
            if (limit < 1 || limit > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(limit));
            Limit = limit;
        }

        public long Limit { get; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Reserve(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Reserve(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            Reserve(1);
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            if (value > Limit) throw Over(value);
            base.SetLength(value);
        }

        private void Reserve(int count)
        {
            long end = Position + count;
            if (end > Limit) throw Over(end);
        }

        private IppException Over(long needed)
            => new("The PWG Raster job needs more than the memory spool limit of " + Limit + " bytes (MemorySpoolLimit; at least "
                   + needed + " bytes). Nothing was sent to the printer. Raise IppPrinterTarget.MemorySpoolLimit, lower the "
                   + "resolution, or use IppSpoolMode.TemporaryFile.");
    }
}
