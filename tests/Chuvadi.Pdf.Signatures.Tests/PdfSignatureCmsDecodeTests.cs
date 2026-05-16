// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — PdfSignature CMS decoding integration test

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Oids;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests;

public sealed class PdfSignatureCmsDecodeTests
{
    private static byte[] BuildMinimalSignedDataCms()
    {
        // Outer ContentInfo wrapping a SignedData with one detached eContent and
        // no certificates / no signer infos. Enough to verify the connection works.
        Asn1Writer w = new();
        w.PushSequence();   // ContentInfo
        w.WriteObjectIdentifier(KnownOids.CmsSignedData);
        w.PushExplicit(0);

        w.PushSequence();   // SignedData
        w.WriteInteger(1);  // version
        w.PushSet();        // digestAlgorithms (empty)
        w.PopSet();
        w.PushSequence();   // encapContentInfo
        w.WriteObjectIdentifier(KnownOids.CmsData);
        w.PopSequence();
        w.PushSet();        // signerInfos (empty)
        w.PopSet();
        w.PopSequence();    // close SignedData

        w.PopExplicit(0);
        w.PopSequence();    // close ContentInfo
        return w.ToArray();
    }

    [Fact]
    public void DecodeCms_RoundTripsThroughPdfSignature()
    {
        byte[] cms = BuildMinimalSignedDataCms();

        PdfSignature sig = new(
            fieldName: "Sig1",
            filter: "Adobe.PPKLite",
            subFilter: SignatureSubFilter.AdbePkcs7Detached,
            byteRange: new ByteRange(0, 100, 200, 100),
            contents: cms,
            name: null, reason: null, location: null, contactInfo: null,
            signingTimeFromDictionary: null,
            isDocumentTimestamp: false);

        sig.IsCmsBased.Should().BeTrue();
        SignedData sd = sig.DecodeCms();
        sd.Version.Should().Be(1);
        sd.EncapContentInfo.IsDetached.Should().BeTrue();
    }

    [Fact]
    public void DecodeCms_OnNonCmsSubFilter_Throws()
    {
        PdfSignature sig = new(
            fieldName: "Sig1",
            filter: null,
            subFilter: "adbe.x509.rsa_sha1",
            byteRange: new ByteRange(0, 100, 200, 100),
            contents: new byte[] { 0x00 },
            name: null, reason: null, location: null, contactInfo: null,
            signingTimeFromDictionary: null,
            isDocumentTimestamp: false);

        sig.IsCmsBased.Should().BeFalse();
        Action act = () => sig.DecodeCms();
        act.Should().Throw<InvalidOperationException>();
    }
}
