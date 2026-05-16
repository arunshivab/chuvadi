// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.1.2.7 — SubjectPublicKeyInfo
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The public key carried by an X.509 certificate, together with the algorithm
/// identifier needed to interpret its bytes.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// SubjectPublicKeyInfo ::= SEQUENCE {
///   algorithm         AlgorithmIdentifier,
///   subjectPublicKey  BIT STRING
/// }
/// </code>
/// The BIT STRING contents are algorithm-specific:
/// <list type="bullet">
///   <item>RSA: DER-encoded RSAPublicKey (modulus, exponent) — RFC 3279 §2.3.1.</item>
///   <item>ECDSA: an uncompressed (0x04 ‖ X ‖ Y) or compressed EC point — RFC 5480 §2.2.</item>
///   <item>Ed25519/Ed448: the raw 32 or 57 byte public key — RFC 8410 §4.</item>
/// </list>
/// Chuvadi keeps the BIT STRING content unparsed here; specialised decoders
/// will lift it into algorithm-specific public key types as those land.
/// </remarks>
public sealed class SubjectPublicKeyInfo
{
    /// <summary>Initialises a new SubjectPublicKeyInfo.</summary>
    public SubjectPublicKeyInfo(AlgorithmIdentifier algorithm, BitStringValue subjectPublicKey, byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(subjectPublicKey);
        ArgumentNullException.ThrowIfNull(rawEncoding);
        Algorithm = algorithm;
        SubjectPublicKey = subjectPublicKey;
        RawEncoding = rawEncoding;
    }

    /// <summary>The algorithm identifier of the public key.</summary>
    public AlgorithmIdentifier Algorithm { get; }

    /// <summary>The public-key bytes, encoded as an algorithm-specific BIT STRING.</summary>
    public BitStringValue SubjectPublicKey { get; }

    /// <summary>The complete DER encoding of the SubjectPublicKeyInfo (preserved for hashing).</summary>
    public byte[] RawEncoding { get; }

    /// <summary>Reads a SubjectPublicKeyInfo from a reader positioned at its SEQUENCE.</summary>
    public static SubjectPublicKeyInfo Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte[] raw = reader.PeekEncoded();
        Asn1Reader seq = reader.ReadSequence();
        AlgorithmIdentifier algorithm = AlgorithmIdentifier.Read(seq);
        BitStringValue spk = seq.ReadBitString();
        seq.ExpectEnd();
        return new SubjectPublicKeyInfo(algorithm, spk, raw);
    }
}
