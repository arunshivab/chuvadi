// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3161 §2.4.2 — TSTInfo
// PHASE: Phase 1.1.4 — RFC 3161 timestamps

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// The structured timestamp content inside a TimeStampToken.
/// </summary>
/// <remarks>
/// RFC 3161 §2.4.2:
/// <code>
/// TSTInfo ::= SEQUENCE  {
///   version          INTEGER  { v1(1) },
///   policy           TSAPolicyId,
///   messageImprint   MessageImprint,
///   serialNumber     INTEGER,
///   genTime          GeneralizedTime,
///   accuracy         Accuracy                 OPTIONAL,
///   ordering         BOOLEAN             DEFAULT FALSE,
///   nonce            INTEGER                  OPTIONAL,
///   tsa              [0] EXPLICIT GeneralName OPTIONAL,
///   extensions       [1] IMPLICIT Extensions  OPTIONAL
/// }
/// </code>
/// Chuvadi parses the mandatory fields and the most useful optional ones
/// (genTime, messageImprint, serialNumber); other optional fields are
/// preserved as raw bytes in <see cref="RawEncoding"/> for advanced callers.
/// </remarks>
public sealed class TstInfo
{
    /// <summary>Initialises a new TstInfo.</summary>
    public TstInfo(
        int version,
        ObjectIdentifier policy,
        MessageImprint messageImprint,
        BigInteger serialNumber,
        DateTimeOffset genTime,
        byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(messageImprint);
        ArgumentNullException.ThrowIfNull(rawEncoding);
        Version = version;
        Policy = policy;
        MessageImprint = messageImprint;
        SerialNumber = serialNumber;
        GenTime = genTime;
        RawEncoding = rawEncoding;
    }

    /// <summary>Version of TSTInfo (per RFC 3161 the only currently-defined value is 1).</summary>
    public int Version { get; }

    /// <summary>The TSA's policy under which this token was issued.</summary>
    public ObjectIdentifier Policy { get; }

    /// <summary>The hash this token is asserting an existence-at-time claim for.</summary>
    public MessageImprint MessageImprint { get; }

    /// <summary>The TSA's unique serial number for this token.</summary>
    public BigInteger SerialNumber { get; }

    /// <summary>The time the TSA generated the token.</summary>
    public DateTimeOffset GenTime { get; }

    /// <summary>The full DER bytes of TSTInfo.</summary>
    public byte[] RawEncoding { get; }

    /// <summary>Parses a TstInfo from its DER encoding.</summary>
    public static TstInfo Decode(byte[] der)
    {
        ArgumentNullException.ThrowIfNull(der);
        Asn1Reader r = new(der);
        Asn1Reader seq = r.ReadSequence();

        BigInteger versionBig = seq.ReadInteger();
        int version = (int)versionBig;

        ObjectIdentifier policy = seq.ReadObjectIdentifier();

        // messageImprint SEQUENCE { hashAlgorithm AlgorithmIdentifier, hashedMessage OCTET STRING }
        Asn1Reader miSeq = seq.ReadSequence();
        Chuvadi.Cryptography.X509.AlgorithmIdentifier hashAlg
            = Chuvadi.Cryptography.X509.AlgorithmIdentifier.Read(miSeq);
        byte[] hashedMessage = miSeq.ReadOctetString();
        miSeq.ExpectEnd();
        MessageImprint imprint = new(hashAlg, hashedMessage);

        BigInteger serial = seq.ReadInteger();
        DateTimeOffset genTime = seq.ReadGeneralizedTime();

        // Skip the rest — accuracy, ordering, nonce, tsa, extensions are all OPTIONAL.
        while (!seq.IsAtEnd) { seq.Skip(); }

        return new TstInfo(version, policy, imprint, serial, genTime, der);
    }
}
