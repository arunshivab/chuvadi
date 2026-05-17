// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — CMS signing

using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Signing;

/// <summary>
/// A pluggable signing primitive used by <c>CmsSignedDataBuilder</c>
/// to produce a CMS SignerInfo signature. Implementations may be backed by
/// software keys, an HSM, a smartcard, or a remote signing service.
/// </summary>
/// <remarks>
/// Implementations are stateless from CMS's perspective: each call hashes a
/// message and signs it. The signer carries the certificate it signs with so
/// the CMS builder can identify the signer and decide which SubjectKeyIdentifier
/// or IssuerAndSerialNumber to put in the SignerInfo.
/// </remarks>
public interface ISigner
{
    /// <summary>The certificate this signer signs with.</summary>
    X509Certificate Certificate { get; }

    /// <summary>The hash algorithm used to digest the to-be-signed data.</summary>
    HashAlgorithmName HashAlgorithm { get; }

    /// <summary>
    /// The signature algorithm OID identifying this signer's combination of
    /// key type and hash. Used for the CMS SignerInfo.signatureAlgorithm field.
    /// </summary>
    /// <remarks>
    /// Examples: <c>1.2.840.113549.1.1.11</c> (sha256WithRSAEncryption),
    /// <c>1.2.840.10045.4.3.2</c> (ecdsa-with-SHA256). Chuvadi normalises CMS
    /// SignerInfos that use the bare-key OID, so both forms verify correctly.
    /// </remarks>
    AlgorithmIdentifier SignatureAlgorithm { get; }

    /// <summary>
    /// Signs <paramref name="dataToSign"/> after hashing it with
    /// <see cref="HashAlgorithm"/>. Implementations that hold keys outside
    /// Chuvadi (HSM, OS keystore) may apply the hash internally before signing.
    /// </summary>
    /// <param name="dataToSign">The raw bytes to be hashed and signed.</param>
    /// <returns>The signature bytes, as encoded for CMS SignerInfo.signature.</returns>
    byte[] Sign(byte[] dataToSign);
}
