// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1:2008 §F.3 — Item N format (variable-width big-endian)
// PHASE: Phase 1.1.6 — Chuvadi.Pdf.IO linearization

using System;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Bit-packed big-endian unsigned integer reader. Inverse of <see cref="BitWriter"/>.
/// </summary>
internal sealed class BitReader
{
    private readonly byte[] _data;
    private int _bytePos;
    private int _bitPos;  // 0..7, bits remaining in the current byte from MSB

    public BitReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _bytePos = 0;
        _bitPos = 0;
    }

    /// <summary>Reads an unsigned integer of the given bit width.</summary>
    public long ReadBits(int bitCount)
    {
        if (bitCount < 0 || bitCount > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bitCount));
        }

        long result = 0;
        for (int i = 0; i < bitCount; i++)
        {
            if (_bytePos >= _data.Length)
            {
                return result;  // beyond EOF — yields zeros
            }

            int bit = (_data[_bytePos] >> (7 - _bitPos)) & 1;
            result = (result << 1) | (uint)bit;
            _bitPos++;

            if (_bitPos == 8)
            {
                _bitPos = 0;
                _bytePos++;
            }
        }

        return result;
    }

    /// <summary>Advances the read position to the next byte boundary.</summary>
    public void AlignToByte()
    {
        if (_bitPos != 0)
        {
            _bitPos = 0;
            _bytePos++;
        }
    }
}
