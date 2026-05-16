// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.1 — Certificate
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// A fully-decoded X.509 certificate.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// Certificate ::= SEQUENCE {
///   tbsCertificate     TBSCertificate,
///   signatureAlgorithm AlgorithmIdentifier,
///   signatureValue     BIT STRING
/// }
/// </code>
/// Signature verification (which lands in a later commit) consists of:
/// <list type="number">
///   <item>Hashing <see cref="TbsCertificate.RawEncoding"/> with the algorithm
///         identified by <see cref="SignatureAlgorithm"/>.</item>
///   <item>Verifying that hash against <see cref="SignatureValue"/> using the
///         issuer's public key.</item>
///   <item>Confirming the algorithm declared in TBS (TbsCertificate.Signature)
///         matches <see cref="SignatureAlgorithm"/> — RFC 5280 §4.1.1.2.</item>
/// </list>
/// </remarks>
public sealed class X509Certificate
{
    /// <summary>Initialises a new X509Certificate.</summary>
    public X509Certificate(TbsCertificate tbs, AlgorithmIdentifier signatureAlgorithm,
        BitStringValue signatureValue, byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(tbs);
        ArgumentNullException.ThrowIfNull(signatureAlgorithm);
        ArgumentNullException.ThrowIfNull(signatureValue);
        ArgumentNullException.ThrowIfNull(rawEncoding);
        Tbs = tbs;
        SignatureAlgorithm = signatureAlgorithm;
        SignatureValue = signatureValue;
        RawEncoding = rawEncoding;
    }

    /// <summary>The TBS body — the bytes the signature actually covers.</summary>
    public TbsCertificate Tbs { get; }

    /// <summary>The signature algorithm declared on the outer Certificate.</summary>
    public AlgorithmIdentifier SignatureAlgorithm { get; }

    /// <summary>The signature value as a BIT STRING.</summary>
    public BitStringValue SignatureValue { get; }

    /// <summary>The complete DER encoding of the Certificate.</summary>
    public byte[] RawEncoding { get; }

    /// <summary>
    /// True when the algorithm in the TBS body matches the outer signatureAlgorithm.
    /// RFC 5280 §4.1.1.2: these MUST be equal.
    /// </summary>
    public bool TbsAndOuterAlgorithmsMatch => Tbs.Signature.Equals(SignatureAlgorithm);

    /// <summary>Convenience accessor for the subject DN.</summary>
    public X509Name Subject => Tbs.Subject;

    /// <summary>Convenience accessor for the issuer DN.</summary>
    public X509Name Issuer => Tbs.Issuer;

    /// <summary>Convenience accessor for the validity period.</summary>
    public Validity Validity => Tbs.Validity;

    /// <summary>True when this certificate is self-issued (Subject == Issuer).</summary>
    public bool IsSelfIssued
    {
        get
        {
            byte[] s = Tbs.Subject.RawEncoding;
            byte[] i = Tbs.Issuer.RawEncoding;
            if (s.Length != i.Length) { return false; }
            for (int k = 0; k < s.Length; k++)
            {
                if (s[k] != i[k]) { return false; }
            }
            return true;
        }
    }

    /// <summary>Decodes an X509 certificate from its DER-encoded bytes.</summary>
    public static X509Certificate Decode(byte[] der)
    {
        ArgumentNullException.ThrowIfNull(der);
        Asn1Reader top = new(der);
        byte[] raw = top.PeekEncoded();
        Asn1Reader cert = top.ReadSequence();
        TbsCertificate tbs = TbsCertificate.Read(cert);
        AlgorithmIdentifier sigAlg = AlgorithmIdentifier.Read(cert);
        BitStringValue sigValue = cert.ReadBitString();
        cert.ExpectEnd();
        top.ExpectEnd();
        return new X509Certificate(tbs, sigAlg, sigValue, raw);
    }
}
