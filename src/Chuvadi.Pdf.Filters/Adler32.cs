// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 1950 §8 — Adler-32 checksum algorithm
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Adler-32 checksum used in the zlib envelope around FlateDecode streams.

using System;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Computes and verifies Adler-32 checksums as defined in RFC 1950.
/// Used to validate the integrity of FlateDecode (zlib-wrapped DEFLATE) streams.
/// </summary>
/// <remarks>
/// Adler-32 is simpler and faster than CRC-32. The checksum consists of
/// two 16-bit sums (S1 and S2) combined into a 32-bit value.
/// RFC 1950 §8.
/// </remarks>
public static class Adler32
{
    // RFC 1950: modulus for Adler-32 computations.
    private const uint Modulus = 65521;

    // Initial value: S1=1, S2=0.
    private const uint InitialValue = 1;

    /// <summary>
    /// Computes the Adler-32 checksum of the given data.
    /// </summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <returns>The 32-bit Adler-32 checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        return Update(InitialValue, data);
    }

    /// <summary>
    /// Updates a running Adler-32 checksum with additional bytes.
    /// Allows incremental computation over a stream.
    /// </summary>
    /// <param name="checksum">The current checksum (use 1 to start fresh).</param>
    /// <param name="data">The next bytes to incorporate.</param>
    /// <returns>The updated checksum.</returns>
    public static uint Update(uint checksum, ReadOnlySpan<byte> data)
    {
        uint s1 = checksum & 0xFFFF;
        uint s2 = (checksum >> 16) & 0xFFFF;

        // Process in blocks of up to 5552 bytes.
        // 5552 is the largest block size where neither S1 nor S2 can overflow
        // a uint before the modulo reduction — derived from RFC 1950.
        int i = 0;

        while (i < data.Length)
        {
            int blockEnd = Math.Min(i + 5552, data.Length);

            while (i < blockEnd)
            {
                s1 += data[i];
                s2 += s1;
                i++;
            }

            s1 %= Modulus;
            s2 %= Modulus;
        }

        return (s2 << 16) | s1;
    }

    /// <summary>
    /// Verifies that the Adler-32 checksum of <paramref name="data"/> matches
    /// the expected value.
    /// </summary>
    /// <param name="data">The data to verify.</param>
    /// <param name="expected">The expected checksum.</param>
    /// <returns>True if the checksum matches; false otherwise.</returns>
    public static bool Verify(ReadOnlySpan<byte> data, uint expected)
    {
        return Compute(data) == expected;
    }
}
