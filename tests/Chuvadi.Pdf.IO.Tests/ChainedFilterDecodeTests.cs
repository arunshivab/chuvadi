// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 — /Filter chains (Name or Array of Names)
// PHASE: v2.1.8 — Chuvadi.Pdf.IO tests
//
// Direct unit tests for the chained-/Filter path in
// Chuvadi.Pdf.IO.ObjectStreamReader.Decode (shared with
// PdfReader.DecodeStreamBytes as of v2.1.8). Up to v2.1.7 the PdfReader
// path silently emitted raw bytes when /Filter was an Array; v2.1.8
// promotes the array-aware helper from ObjectStreamReader so both
// callers use one implementation.
//
// Test inputs are produced via FilterPipeline.Encode (the same library
// the production decode path consults), inverting through the same
// codebase the SUT calls. This means a co-incident encode+decode bug
// could theoretically fool the test — but encoders and decoders live in
// separate methods on separate filter classes (e.g. AsciiHexFilter.Encode
// vs AsciiHexFilter.Decode), and the practical alternative (hand-rolling
// FlateDecode-compatible zlib output without first verifying Chuvadi's
// wire format) carried its own different and worse risk.

using System.Text;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class ChainedFilterDecodeTests
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    // ── /Filter chain decode ──────────────────────────────────────────────

    [Fact]
    public void Decode_SingleFlateDecode_RoundTripsThroughHelper()
    {
        // Sanity check that the single-Name path still works after v2.1.8's
        // refactor (the function used to live in PdfReader and was lifted
        // into ObjectStreamReader; we want to confirm the path that ran on
        // every v2.1.7 PDF still produces identical output).

        byte[] original = Encoding.ASCII.GetBytes("Hello, world!");
        byte[] flateEncoded = Pipeline.Encode("FlateDecode", original);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));

        byte[] decoded = ObjectStreamReader.Decode(dict, flateEncoded);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_NoFilter_ReturnsRawBytes()
    {
        // A stream without /Filter must be returned unchanged.
        byte[] original = Encoding.ASCII.GetBytes("uncompressed payload");
        PdfDictionary dict = new PdfDictionary();

        byte[] decoded = ObjectStreamReader.Decode(dict, original);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_ChainedFilters_AppliesInOrder_AsciiHexThenFlate()
    {
        // /Filter [/ASCIIHexDecode /FlateDecode] reads decode-order: first
        // ASCIIHexDecode removes the outermost hex wrapping, then
        // FlateDecode decompresses the inner bytes. Encoding order is
        // reverse: Flate-compress original, then ASCIIHex-wrap the result.
        // This is the bug class v2.1.8 fixes — pre-v2.1.8 PdfReader saw
        // GetName(/Filter) return null on an Array and silently emitted
        // the raw (still hex-wrapped + flated) bytes.

        byte[] original = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog.");
        byte[] flated = Pipeline.Encode("FlateDecode", original);
        byte[] hexWrappedFlated = Pipeline.Encode("ASCIIHexDecode", flated);

        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, new PdfArray(
        [
            PdfName.Intern("ASCIIHexDecode"),
            PdfName.Intern("FlateDecode"),
        ]));

        byte[] decoded = ObjectStreamReader.Decode(dict, hexWrappedFlated);

        decoded.Should().Equal(original,
            "ASCIIHexDecode + FlateDecode applied in order must reproduce the source bytes");
    }

    [Fact]
    public void Decode_SingleNameInArray_BehavesLikeSingleName()
    {
        // /Filter [/FlateDecode] is the array form with one entry. Both
        // shapes are legal per §7.4 and must yield identical decode output.

        byte[] original = Encoding.ASCII.GetBytes("array-with-one-element edge case");
        byte[] flated = Pipeline.Encode("FlateDecode", original);

        PdfDictionary dictArray = new PdfDictionary();
        dictArray.Set(PdfName.Filter, new PdfArray([PdfName.Intern("FlateDecode")]));

        PdfDictionary dictName = new PdfDictionary();
        dictName.Set(PdfName.Filter, PdfName.Intern("FlateDecode"));

        byte[] decodedArray = ObjectStreamReader.Decode(dictArray, flated);
        byte[] decodedName = ObjectStreamReader.Decode(dictName, flated);

        decodedArray.Should().Equal(original);
        decodedName.Should().Equal(decodedArray, "array and Name forms must agree");
    }

    [Fact]
    public void Decode_FilterArrayContainingNonName_ThrowsPdfParseException()
    {
        // /Filter [42 /FlateDecode] is structurally malformed — every array
        // entry must be a Name. The helper must reject it loudly rather
        // than silently skip or coerce.
        // (Order matters here: Decode walks the array left-to-right. If
        // we put a real filter first it would run on the input bytes and
        // throw from DeflateFilter on bad input. We want to verify the
        // shape check, so we put the non-Name first.)

        byte[] anyBytes = [0x01, 0x02];
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Filter, new PdfArray(
        [
            new PdfInteger(42),
            PdfName.Intern("FlateDecode"),
        ]));

        System.Action act = () => ObjectStreamReader.Decode(dict, anyBytes);

        act.Should().Throw<PdfParseException>()
            .WithMessage("*non-Name*");
    }
}
