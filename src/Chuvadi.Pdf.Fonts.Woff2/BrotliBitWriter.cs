// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §2 — Bit Stream Conventions
// PHASE: Phase 2.2 — Brotli bit writer

using System.IO;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// LSB-first bit writer for Brotli streams.
/// Brotli packs bits within a byte from the least significant to the most
/// significant; <see cref="WriteBits"/> emits the low-order bits of a value
/// in that order.
/// </summary>
internal sealed class BrotliBitWriter
{
    private readonly Stream _stream;
    private ulong _buffer;
    private int _bitCount;

    internal BrotliBitWriter(Stream output) { _stream = output; }

    /// <summary>Writes <paramref name="count"/> low-order bits of <paramref name="value"/>.</summary>
    internal void WriteBits(ulong value, int count)
    {
        if (count == 0) { return; }
        ulong masked = value & ((1UL << count) - 1);
        _buffer |= masked << _bitCount;
        _bitCount += count;
        while (_bitCount >= 8)
        {
            _stream.WriteByte((byte)(_buffer & 0xFF));
            _buffer >>= 8;
            _bitCount -= 8;
        }
    }

    /// <summary>Pads to the next byte boundary with zero bits and flushes.</summary>
    internal void Flush()
    {
        if (_bitCount > 0)
        {
            _stream.WriteByte((byte)(_buffer & 0xFF));
            _buffer = 0;
            _bitCount = 0;
        }
    }

    /// <summary>Current bit position (for diagnostics).</summary>
    internal long BitPosition => _stream.Position * 8 + _bitCount;
}
