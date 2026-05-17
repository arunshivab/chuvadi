// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3161 §2.4.1 — Time-Stamp Protocol Request
// PHASE: Phase 1.2.3 — TSA fetching

using System;
using System.Numerics;
using System.Security.Cryptography;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.X509;
using Chuvadi.Cryptography.Hashing;
using ChuvadiHashAlgorithmName = Chuvadi.Cryptography.Hashing.HashAlgorithmName;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// An RFC 3161 Time-Stamp Protocol request, ready to POST to a TSA.
/// </summary>
/// <remarks>
/// <para>
/// ASN.1 structure (RFC 3161 §2.4.1):
/// <code>
/// TimeStampReq ::= SEQUENCE {
///   version       INTEGER  { v1(1) },
///   messageImprint MessageImprint,
///   reqPolicy     TSAPolicyId  OPTIONAL,
///   nonce         INTEGER  OPTIONAL,
///   certReq       BOOLEAN  DEFAULT FALSE,
///   extensions    [0] IMPLICIT Extensions  OPTIONAL
/// }
/// </code>
/// </para>
/// <para>
/// Built via <see cref="ForData"/> or <see cref="ForDigest"/>. The MIME
/// type when POSTing is <c>application/timestamp-query</c>; the response
/// comes back as <c>application/timestamp-reply</c>.
/// </para>
/// </remarks>
public sealed class TimeStampRequest
{
    /// <summary>Initialises a new request.</summary>
    /// <param name="messageImprint">Hash algorithm + hash of the data being stamped.</param>
    /// <param name="nonce">Optional 64-bit nonce; must equal the response's nonce.</param>
    /// <param name="certReq">When true, asks the TSA to embed its certificate in the response.</param>
    /// <param name="reqPolicy">Optional requested TSA policy OID.</param>
    public TimeStampRequest(
        MessageImprint messageImprint,
        BigInteger? nonce = null,
        bool certReq = true,
        ObjectIdentifier? reqPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(messageImprint);
        MessageImprint = messageImprint;
        Nonce = nonce;
        CertReq = certReq;
        ReqPolicy = reqPolicy;
    }

    /// <summary>The hash algorithm + the digest being time-stamped.</summary>
    public MessageImprint MessageImprint { get; }

    /// <summary>Optional nonce for replay-protection; null when not requested.</summary>
    public BigInteger? Nonce { get; }

    /// <summary>When true, the TSA is asked to include its cert in the response.</summary>
    public bool CertReq { get; }

    /// <summary>Optional requested policy OID; null when accepting the TSA's default.</summary>
    public ObjectIdentifier? ReqPolicy { get; }

    /// <summary>
    /// Builds a request that time-stamps <paramref name="data"/> with the given hash.
    /// A 64-bit random nonce is generated automatically.
    /// </summary>
    public static TimeStampRequest ForData(
        ReadOnlySpan<byte> data,
        ChuvadiHashAlgorithmName hashAlgorithm,
        bool certReq = true,
        ObjectIdentifier? reqPolicy = null)
    {
        IHashAlgorithm h = HashFactory.Create(hashAlgorithm);
        h.Update(data);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);
        return ForDigest(digest, hashAlgorithm, certReq, reqPolicy);
    }

    /// <summary>
    /// Builds a request from a pre-computed digest. A 64-bit random nonce is
    /// generated automatically.
    /// </summary>
    public static TimeStampRequest ForDigest(
        ReadOnlySpan<byte> digest,
        ChuvadiHashAlgorithmName hashAlgorithm,
        bool certReq = true,
        ObjectIdentifier? reqPolicy = null)
    {
        ObjectIdentifier oid = hashAlgorithm switch
        {
            ChuvadiHashAlgorithmName.Sha256 => KnownOids.Sha256,
            ChuvadiHashAlgorithmName.Sha384 => KnownOids.Sha384,
            ChuvadiHashAlgorithmName.Sha512 => KnownOids.Sha512,
            _ => throw new ArgumentException(
                $"Unsupported hash algorithm: {hashAlgorithm}", nameof(hashAlgorithm)),
        };
        AlgorithmIdentifier alg = new(oid, null);
        MessageImprint imprint = new(alg, digest.ToArray());
        BigInteger nonce = GenerateNonce();
        return new TimeStampRequest(imprint, nonce, certReq, reqPolicy);
    }

    /// <summary>DER-encodes this request per RFC 3161 §2.4.1.</summary>
    public byte[] Encode()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteInteger(1);  // version v1
        WriteMessageImprint(w);
        if (ReqPolicy is not null)
        {
            w.WriteObjectIdentifier(ReqPolicy);
        }
        if (Nonce is BigInteger n)
        {
            w.WriteInteger(n);
        }
        // certReq is BOOLEAN DEFAULT FALSE — only emit when true (per DER rules)
        if (CertReq)
        {
            w.WriteBoolean(true);
        }
        w.PopSequence();
        return w.ToArray();
    }

    private void WriteMessageImprint(Asn1Writer w)
    {
        // MessageImprint ::= SEQUENCE { hashAlgorithm AlgorithmIdentifier, hashedMessage OCTET STRING }
        w.PushSequence();
        w.PushSequence();
        w.WriteObjectIdentifier(MessageImprint.HashAlgorithm.Algorithm);
        if (MessageImprint.HashAlgorithm.ParametersAreAbsent
            || MessageImprint.HashAlgorithm.ParametersAreNull)
        {
            w.WriteNull();
        }
        w.PopSequence();
        w.WriteOctetString(MessageImprint.HashedMessage);
        w.PopSequence();
    }

    private static BigInteger GenerateNonce()
    {
        // 64-bit unsigned random; non-negative interpretation.
        byte[] buf = new byte[9];
        RandomNumberGenerator.Fill(buf.AsSpan(1, 8));
        buf[0] = 0;  // leading zero forces positive sign
        // BigInteger ctor takes little-endian by default; swap.
        Array.Reverse(buf);
        return new BigInteger(buf);
    }
}
