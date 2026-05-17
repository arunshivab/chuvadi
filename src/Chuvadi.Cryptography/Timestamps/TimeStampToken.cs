// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3161 §2.4.2 — TimeStampToken (a CMS SignedData)
// PHASE: Phase 1.1.4 — RFC 3161 timestamps

using System;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// An RFC 3161 TimeStampToken — a CMS SignedData wrapping a TSTInfo payload.
/// </summary>
/// <remarks>
/// RFC 3161 §2.4.2: <c>TimeStampToken ::= ContentInfo</c> where the inner
/// content type is <c>id-signedData</c> and the <c>encapContentInfo</c> of
/// that SignedData carries <c>id-ct-TSTInfo</c> as its content type with the
/// DER encoding of <see cref="TstInfo"/> as its content.
/// </remarks>
public sealed class TimeStampToken
{
    /// <summary>Initialises a new TimeStampToken.</summary>
    public TimeStampToken(SignedData signedData, TstInfo tstInfo, byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(signedData);
        ArgumentNullException.ThrowIfNull(tstInfo);
        ArgumentNullException.ThrowIfNull(rawEncoding);
        SignedData = signedData;
        TstInfo = tstInfo;
        RawEncoding = rawEncoding;
    }

    /// <summary>The underlying CMS SignedData (the signed bytes are <see cref="TstInfo"/>.RawEncoding).</summary>
    public SignedData SignedData { get; }

    /// <summary>The decoded TSTInfo payload.</summary>
    public TstInfo TstInfo { get; }

    /// <summary>The full DER bytes of the TimeStampToken (i.e. of the outer ContentInfo).</summary>
    public byte[] RawEncoding { get; }

    /// <summary>
    /// Parses a TimeStampToken from its DER encoding.
    /// </summary>
    /// <exception cref="Asn1.Asn1Exception">If the bytes are not a CMS SignedData wrapping TSTInfo.</exception>
    public static TimeStampToken Decode(byte[] der)
    {
        ArgumentNullException.ThrowIfNull(der);

        SignedData signedData = CmsDecoder.DecodeSignedData(der);

        if (!signedData.EncapContentInfo.ContentType.Equals(KnownOids.TstInfo))
        {
            throw new Chuvadi.Cryptography.Asn1.Asn1Exception(
                $"TimeStampToken's encapsulated content type is not id-ct-TSTInfo "
                + $"(got {signedData.EncapContentInfo.ContentType}).");
        }

        byte[] eContent = signedData.EncapContentInfo.Content
            ?? throw new Chuvadi.Cryptography.Asn1.Asn1Exception(
                "TimeStampToken must carry the TSTInfo inline as eContent (detached form is not legal for a TST).");

        TstInfo tstInfo = TstInfo.Decode(eContent);
        return new TimeStampToken(signedData, tstInfo, der);
    }
}
