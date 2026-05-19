// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 — Brotli Compressed Data Format §4, §5
// PHASE: Phase 2.2 stage 2 — Brotli code tables

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Mappings between literal length / copy length / distance values and their
/// RFC 7932 code numbers, plus extra-bit counts and offset bases.
/// </summary>
internal static class BrotliCodeTables
{
    // RFC §5: insert length code table — verified directly against the spec.
    internal static readonly (int Extra, int Base)[] InsertLenCodes =
    {
        (0, 0),   (0, 1),   (0, 2),   (0, 3),
        (0, 4),   (0, 5),   (1, 6),   (1, 8),
        (2, 10),  (2, 14),  (3, 18),  (3, 26),
        (4, 34),  (4, 50),  (5, 66),  (5, 98),
        (6, 130), (7, 194), (8, 322), (9, 578),
        (10, 1090), (12, 2114), (14, 6210), (24, 22594),
    };

    // RFC §5: copy length code table. Copy lengths start at 2.
    internal static readonly (int Extra, int Base)[] CopyLenCodes =
    {
        (0, 2),    (0, 3),    (0, 4),    (0, 5),
        (0, 6),    (0, 7),    (0, 8),    (0, 9),
        (1, 10),   (1, 12),   (2, 14),   (2, 18),
        (3, 22),   (3, 30),   (4, 38),   (4, 54),
        (5, 70),   (5, 102),  (6, 134),  (7, 198),
        (8, 326),  (9, 582),  (10, 1094),(24, 2118),
    };

    /// <summary>Find the largest insert length code whose base ≤ length.</summary>
    internal static int InsertLenCode(int length)
    {
        for (int i = InsertLenCodes.Length - 1; i >= 0; i--)
        {
            if (InsertLenCodes[i].Base <= length) { return i; }
        }
        return 0;
    }

    /// <summary>Find the largest copy length code whose base ≤ length.</summary>
    internal static int CopyLenCode(int length)
    {
        for (int i = CopyLenCodes.Length - 1; i >= 0; i--)
        {
            if (CopyLenCodes[i].Base <= length) { return i; }
        }
        return 0;
    }

    /// <summary>
    /// Encodes (insert_len_code, copy_len_code) as a 10-bit insert-and-copy symbol.
    /// </summary>
    /// <remarks>
    /// Per RFC §5 Table 2. Buckets each code into one of three ranges (0..7, 8..15, 16..23)
    /// and selects a 64-entry cell from the 4×3 grid based on (insert bucket, copy bucket)
    /// and whether the command uses an explicit distance. Within the cell, bits 3..5 = insert
    /// offset within bucket, bits 0..2 = copy offset within bucket.
    /// </remarks>
    internal static int InsertAndCopySymbol(int insertLenCode, int copyLenCode, bool useDistance)
    {
        // Cell-base lookup from the §5 table:
        //                       Copy 0..7   Copy 8..15  Copy 16..23
        //   Insert 0..7 dist=0    0          64          —
        //   Insert 0..7           128        192         384
        //   Insert 8..15          256        320         512
        //   Insert 16..23         448        576         640
        int insertBucket = insertLenCode < 8 ? 0 : (insertLenCode < 16 ? 1 : 2);
        int copyBucket = copyLenCode < 8 ? 0 : (copyLenCode < 16 ? 1 : 2);
        int insertOff = insertLenCode - (insertBucket == 0 ? 0 : (insertBucket == 1 ? 8 : 16));
        int copyOff = copyLenCode - (copyBucket == 0 ? 0 : (copyBucket == 1 ? 8 : 16));

        int cellBase;
        if (!useDistance)
        {
            // Only valid for insert bucket 0 (i.e., insert 0..7).
            // Row "Insert 0..7 dist=0": copy bucket 0 → 0, copy bucket 1 → 64. Copy bucket 2
            // has no distance-implicit cell, so we MUST use explicit distance for copy 16+.
            cellBase = copyBucket == 0 ? 0 : 64;
        }
        else
        {
            // Explicit-distance grid (3×3):
            //   insert bucket × copy bucket → cell base
            //   (0,0)=128 (0,1)=192 (0,2)=384
            //   (1,0)=256 (1,1)=320 (1,2)=512
            //   (2,0)=448 (2,1)=576 (2,2)=640
            cellBase = (insertBucket, copyBucket) switch
            {
                (0, 0) => 128,
                (0, 1) => 192,
                (0, 2) => 384,
                (1, 0) => 256,
                (1, 1) => 320,
                (1, 2) => 512,
                (2, 0) => 448,
                (2, 1) => 576,
                (2, 2) => 640,
                _ => 128,
            };
        }
        return cellBase + (insertOff << 3) + copyOff;
    }

    /// <summary>Convert a backward distance to (distance_code, extra_bits, extra_value).</summary>
    /// <remarks>
    /// Assumes NPOSTFIX=0, NDIRECT=0. Uses distance codes ≥16 (explicit) only — never the
    /// "reuse previous" codes 0..15.
    /// </remarks>
    internal static (int Code, int ExtraBits, int ExtraValue) DistanceToCode(int distance)
    {
        // RFC §4 inverse: given distance D ≥ 1, find dcode such that the range covers D.
        //   dcode=16: dist 1..2  (ndistbits=1, hcode=0, offset=0)
        //   dcode=17: dist 3..4  (ndistbits=1, hcode=1, offset=2)
        //   dcode=18: dist 5..8  (ndistbits=2, hcode=2, offset=4)
        //   dcode=19: dist 9..12 (ndistbits=2, hcode=3, offset=8)
        //   dcode=20: dist 13..20
        //   dcode=21: dist 21..28
        //   ...
        // General: distance = offset + extra + 1, offset = ((2 + (hcode&1)) << ndistbits) - 4
        // where ndistbits = 1 + ((dcode-16) >> 1) and hcode = dcode-16.
        if (distance < 1) { throw new System.ArgumentOutOfRangeException(nameof(distance)); }
        for (int dcode = 16; dcode < 16 + 48; dcode++)
        {
            int hcode = dcode - 16;
            int ndistbits = 1 + (hcode >> 1);
            int offset = ((2 + (hcode & 1)) << ndistbits) - 4;
            int low = offset + 1;
            int high = offset + (1 << ndistbits);
            if (distance >= low && distance <= high)
            {
                return (dcode, ndistbits, distance - low);
            }
        }
        throw new System.ArgumentOutOfRangeException(nameof(distance), "Distance too large.");
    }
}
