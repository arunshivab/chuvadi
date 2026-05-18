// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §3.4 + §3.5
// PHASE: Phase 2.2 stage 3

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// A Brotli prefix code that automatically selects RFC §3.4 simple or §3.5 complex
/// emission depending on the size of the symbol set.
/// </summary>
/// <remarks>
/// <para>
/// For 1..4 distinct symbols the simple form is used (verified working in stage 2). For
/// 5+ distinct symbols the complex form is used (stage 3). Callers don't care: they ask
/// for <see cref="GetCode"/> and <see cref="EmitDeclaration"/> and the right form is
/// chosen internally.
/// </para>
/// </remarks>
internal sealed class BrotliPrefixCode
{
    private readonly bool _useSimple;
    private readonly BrotliSimplePrefixCode? _simple;
    private readonly int[]? _complexLengths;
    private readonly int[]? _complexCodes;

    /// <summary>Build a prefix code over the symbols whose frequency is non-zero.</summary>
    /// <param name="frequencies">Frequency per symbol; size = alphabet size.</param>
    /// <param name="maxLength">Maximum code length (15 for main alphabets, 5 for code-length alphabet).</param>
    internal BrotliPrefixCode(int[] frequencies, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        int distinctCount = 0;
        for (int i = 0; i < frequencies.Length; i++)
        {
            if (frequencies[i] > 0) { distinctCount++; }
        }
        if (distinctCount is < 1 or > 4)
        {
            _useSimple = false;
            _complexLengths = BrotliHuffman.ComputeCodeLengths(frequencies, maxLength);
            _complexCodes = BrotliHuffman.BuildCanonicalCodes(_complexLengths);
        }
        else
        {
            _useSimple = true;
            List<int> distinctSymbols = new(distinctCount);
            for (int i = 0; i < frequencies.Length; i++)
            {
                if (frequencies[i] > 0) { distinctSymbols.Add(i); }
            }
            _simple = new BrotliSimplePrefixCode(distinctSymbols);
        }
    }

    /// <summary>Returns the (LSB-first code value, bit length) for emitting <paramref name="symbol"/>.</summary>
    internal (int CodeValue, int BitLength) GetCode(int symbol)
    {
        if (_useSimple) { return _simple!.GetCode(symbol); }
        return (_complexCodes![symbol], _complexLengths![symbol]);
    }

    /// <summary>Emit this prefix code's declaration into the bitstream.</summary>
    internal void EmitDeclaration(BrotliBitWriter bw, int alphabetBits)
    {
        ArgumentNullException.ThrowIfNull(bw);
        if (_useSimple) { _simple!.EmitDeclaration(bw, alphabetBits); }
        else { BrotliComplexPrefixCode.EmitDeclaration(bw, _complexLengths!); }
    }
}
