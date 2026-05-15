// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1:2008 §F.3 — Item N format (variable-width big-endian)
// PHASE: Phase 1.1.6 — Chuvadi.Pdf.IO linearization
//
// Bit-packed writer for hint stream tables. Items in §F.3 are unsigned
// integers of arbitrary bit width, packed big-endian (most significant
// bit first), with no padding between items inside a single table.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Bit-packed big-endian unsigned integer writer.
/// </summary>
/// <remarks>
/// Used to encode hint stream tables per ISO 32000-1 §F.3. Items are written
/// most-significant-bit first; the underlying byte stream is byte-padded only
/// at the caller's explicit request (via <see cref="AlignToByte"/>).
/// </remarks>
internal sealed class BitWriter
{
    private readonly List<byte> _bytes = new();
    private int _currentByte;
    private int _bitsInCurrent;

    /// <summary>Writes an unsigned integer of the given bit width.</summary>
    public void WriteBits(long value, int bitCount)
    {
        if (bitCount < 0 || bitCount > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        }

        for (int i = bitCount - 1; i >= 0; i--)
        {
            int bit = (int)((value >> i) & 1);
            _currentByte = (_currentByte << 1) | bit;
            _bitsInCurrent++;

            if (_bitsInCurrent == 8)
            {
                _bytes.Add((byte)_currentByte);
                _currentByte = 0;
                _bitsInCurrent = 0;
            }
        }
    }

    /// <summary>Pads the current byte with zeros so the next write starts on a byte boundary.</summary>
    public void AlignToByte()
    {
        if (_bitsInCurrent > 0)
        {
            _currentByte <<= (8 - _bitsInCurrent);
            _bytes.Add((byte)_currentByte);
            _currentByte = 0;
            _bitsInCurrent = 0;
        }
    }

    /// <summary>Returns the accumulated bytes (byte-aligned first).</summary>
    public byte[] ToArray()
    {
        AlignToByte();
        return _bytes.ToArray();
    }
}
