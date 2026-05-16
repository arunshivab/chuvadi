// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography OID registry
//
// Central registry of every OID Chuvadi's signature workflows touch.
// Sourced from:
//   - RFC 5280 (X.509 PKIX), RFC 5912 (PKIX ASN.1 modules)
//   - RFC 5652 (CMS / PKCS#7)
//   - RFC 3161 (RFC 3161 timestamping)
//   - RFC 6960 (OCSP)
//   - RFC 8017 (PKCS#1), RFC 5480 (ECDSA), RFC 8032 (EdDSA)
//   - ETSI EN 319 122 (CAdES)
//
// Constants are grouped by family. All values use dotted-decimal form so
// they are immediately recognisable when grepping the code or comparing to spec.

using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Oids;

/// <summary>
/// Named ObjectIdentifier constants for the OIDs Chuvadi cares about.
/// </summary>
public static class KnownOids
{
    // ── Hash algorithms ───────────────────────────────────────────────────

    /// <summary>SHA-1. RFC 8017.</summary>
    public static readonly ObjectIdentifier Sha1 = new("1.3.14.3.2.26");

    /// <summary>SHA-224. NIST hash family OID arc.</summary>
    public static readonly ObjectIdentifier Sha224 = new("2.16.840.1.101.3.4.2.4");

    /// <summary>SHA-256.</summary>
    public static readonly ObjectIdentifier Sha256 = new("2.16.840.1.101.3.4.2.1");

    /// <summary>SHA-384.</summary>
    public static readonly ObjectIdentifier Sha384 = new("2.16.840.1.101.3.4.2.2");

    /// <summary>SHA-512.</summary>
    public static readonly ObjectIdentifier Sha512 = new("2.16.840.1.101.3.4.2.3");

    /// <summary>SHA-3 256.</summary>
    public static readonly ObjectIdentifier Sha3_256 = new("2.16.840.1.101.3.4.2.8");

    /// <summary>SHA-3 384.</summary>
    public static readonly ObjectIdentifier Sha3_384 = new("2.16.840.1.101.3.4.2.9");

    /// <summary>SHA-3 512.</summary>
    public static readonly ObjectIdentifier Sha3_512 = new("2.16.840.1.101.3.4.2.10");

    // ── Public-key algorithms ─────────────────────────────────────────────

    /// <summary>RSA encryption (PKCS#1 v1.5 signing uses this AlgorithmIdentifier for the key).</summary>
    public static readonly ObjectIdentifier RsaEncryption = new("1.2.840.113549.1.1.1");

    /// <summary>RSASSA-PSS. RFC 8017.</summary>
    public static readonly ObjectIdentifier RsaSsaPss = new("1.2.840.113549.1.1.10");

    /// <summary>id-mgf1 mask generation function. RFC 4055 §2.1.</summary>
    public static readonly ObjectIdentifier Mgf1 = new("1.2.840.113549.1.1.8");

    /// <summary>ECDSA with public key. RFC 5480.</summary>
    public static readonly ObjectIdentifier EcPublicKey = new("1.2.840.10045.2.1");

    /// <summary>Ed25519 (EdDSA). RFC 8032.</summary>
    public static readonly ObjectIdentifier Ed25519 = new("1.3.101.112");

    /// <summary>Ed448 (EdDSA). RFC 8032.</summary>
    public static readonly ObjectIdentifier Ed448 = new("1.3.101.113");

    // ── ECDSA curves ──────────────────────────────────────────────────────

    /// <summary>NIST P-256 / secp256r1 / prime256v1.</summary>
    public static readonly ObjectIdentifier Secp256r1 = new("1.2.840.10045.3.1.7");

    /// <summary>NIST P-384 / secp384r1.</summary>
    public static readonly ObjectIdentifier Secp384r1 = new("1.3.132.0.34");

    /// <summary>NIST P-521 / secp521r1.</summary>
    public static readonly ObjectIdentifier Secp521r1 = new("1.3.132.0.35");

    /// <summary>secp256k1 (Bitcoin curve, occasionally seen).</summary>
    public static readonly ObjectIdentifier Secp256k1 = new("1.3.132.0.10");

    /// <summary>brainpoolP256r1. RFC 5639.</summary>
    public static readonly ObjectIdentifier BrainpoolP256r1 = new("1.3.36.3.3.2.8.1.1.7");

    /// <summary>brainpoolP384r1.</summary>
    public static readonly ObjectIdentifier BrainpoolP384r1 = new("1.3.36.3.3.2.8.1.1.11");

    /// <summary>brainpoolP512r1.</summary>
    public static readonly ObjectIdentifier BrainpoolP512r1 = new("1.3.36.3.3.2.8.1.1.13");

    // ── Signature algorithms (combined hash + key) ────────────────────────

    /// <summary>SHA-1 with RSA. RFC 8017. Deprecated for new signatures.</summary>
    public static readonly ObjectIdentifier Sha1WithRsa = new("1.2.840.113549.1.1.5");

    /// <summary>SHA-256 with RSA.</summary>
    public static readonly ObjectIdentifier Sha256WithRsa = new("1.2.840.113549.1.1.11");

    /// <summary>SHA-384 with RSA.</summary>
    public static readonly ObjectIdentifier Sha384WithRsa = new("1.2.840.113549.1.1.12");

    /// <summary>SHA-512 with RSA.</summary>
    public static readonly ObjectIdentifier Sha512WithRsa = new("1.2.840.113549.1.1.13");

    /// <summary>SHA-1 with ECDSA. Deprecated.</summary>
    public static readonly ObjectIdentifier Sha1WithEcdsa = new("1.2.840.10045.4.1");

    /// <summary>SHA-256 with ECDSA.</summary>
    public static readonly ObjectIdentifier Sha256WithEcdsa = new("1.2.840.10045.4.3.2");

    /// <summary>SHA-384 with ECDSA.</summary>
    public static readonly ObjectIdentifier Sha384WithEcdsa = new("1.2.840.10045.4.3.3");

    /// <summary>SHA-512 with ECDSA.</summary>
    public static readonly ObjectIdentifier Sha512WithEcdsa = new("1.2.840.10045.4.3.4");

    // ── X.500 directory attribute OIDs (used in X.509 DistinguishedName) ──

    /// <summary>CN — common name. 2.5.4.3</summary>
    public static readonly ObjectIdentifier CommonName = new("2.5.4.3");

    /// <summary>SN — surname.</summary>
    public static readonly ObjectIdentifier Surname = new("2.5.4.4");

    /// <summary>serialNumber attribute (distinct from certificate serial number).</summary>
    public static readonly ObjectIdentifier SerialNumber = new("2.5.4.5");

    /// <summary>C — country.</summary>
    public static readonly ObjectIdentifier CountryName = new("2.5.4.6");

    /// <summary>L — locality.</summary>
    public static readonly ObjectIdentifier LocalityName = new("2.5.4.7");

    /// <summary>ST — state or province.</summary>
    public static readonly ObjectIdentifier StateOrProvinceName = new("2.5.4.8");

    /// <summary>O — organisation.</summary>
    public static readonly ObjectIdentifier OrganizationName = new("2.5.4.10");

    /// <summary>OU — organisational unit.</summary>
    public static readonly ObjectIdentifier OrganizationalUnitName = new("2.5.4.11");

    /// <summary>title.</summary>
    public static readonly ObjectIdentifier Title = new("2.5.4.12");

    /// <summary>givenName.</summary>
    public static readonly ObjectIdentifier GivenName = new("2.5.4.42");

    /// <summary>initials.</summary>
    public static readonly ObjectIdentifier Initials = new("2.5.4.43");

    /// <summary>pseudonym.</summary>
    public static readonly ObjectIdentifier Pseudonym = new("2.5.4.65");

    /// <summary>emailAddress (legacy, deprecated by RFC 5280 but still common).</summary>
    public static readonly ObjectIdentifier EmailAddress = new("1.2.840.113549.1.9.1");

    /// <summary>domainComponent.</summary>
    public static readonly ObjectIdentifier DomainComponent = new("0.9.2342.19200300.100.1.25");

    // ── X.509 certificate extensions (RFC 5280 §4.2) ──────────────────────

    /// <summary>subjectDirectoryAttributes.</summary>
    public static readonly ObjectIdentifier SubjectDirectoryAttributes = new("2.5.29.9");

    /// <summary>subjectKeyIdentifier.</summary>
    public static readonly ObjectIdentifier SubjectKeyIdentifier = new("2.5.29.14");

    /// <summary>keyUsage.</summary>
    public static readonly ObjectIdentifier KeyUsage = new("2.5.29.15");

    /// <summary>subjectAltName.</summary>
    public static readonly ObjectIdentifier SubjectAltName = new("2.5.29.17");

    /// <summary>issuerAltName.</summary>
    public static readonly ObjectIdentifier IssuerAltName = new("2.5.29.18");

    /// <summary>basicConstraints.</summary>
    public static readonly ObjectIdentifier BasicConstraints = new("2.5.29.19");

    /// <summary>nameConstraints.</summary>
    public static readonly ObjectIdentifier NameConstraints = new("2.5.29.30");

    /// <summary>cRLDistributionPoints.</summary>
    public static readonly ObjectIdentifier CrlDistributionPoints = new("2.5.29.31");

    /// <summary>certificatePolicies.</summary>
    public static readonly ObjectIdentifier CertificatePolicies = new("2.5.29.32");

    /// <summary>policyMappings.</summary>
    public static readonly ObjectIdentifier PolicyMappings = new("2.5.29.33");

    /// <summary>authorityKeyIdentifier.</summary>
    public static readonly ObjectIdentifier AuthorityKeyIdentifier = new("2.5.29.35");

    /// <summary>policyConstraints.</summary>
    public static readonly ObjectIdentifier PolicyConstraints = new("2.5.29.36");

    /// <summary>extKeyUsage.</summary>
    public static readonly ObjectIdentifier ExtKeyUsage = new("2.5.29.37");

    /// <summary>freshestCRL.</summary>
    public static readonly ObjectIdentifier FreshestCrl = new("2.5.29.46");

    /// <summary>inhibitAnyPolicy.</summary>
    public static readonly ObjectIdentifier InhibitAnyPolicy = new("2.5.29.54");

    /// <summary>authorityInfoAccess (RFC 5280 §4.2.2.1).</summary>
    public static readonly ObjectIdentifier AuthorityInfoAccess = new("1.3.6.1.5.5.7.1.1");

    // ── ExtendedKeyUsage purposes ─────────────────────────────────────────

    /// <summary>id-kp-serverAuth.</summary>
    public static readonly ObjectIdentifier ServerAuth = new("1.3.6.1.5.5.7.3.1");

    /// <summary>id-kp-clientAuth.</summary>
    public static readonly ObjectIdentifier ClientAuth = new("1.3.6.1.5.5.7.3.2");

    /// <summary>id-kp-codeSigning.</summary>
    public static readonly ObjectIdentifier CodeSigning = new("1.3.6.1.5.5.7.3.3");

    /// <summary>id-kp-emailProtection.</summary>
    public static readonly ObjectIdentifier EmailProtection = new("1.3.6.1.5.5.7.3.4");

    /// <summary>id-kp-timeStamping.</summary>
    public static readonly ObjectIdentifier TimeStamping = new("1.3.6.1.5.5.7.3.8");

    /// <summary>id-kp-OCSPSigning.</summary>
    public static readonly ObjectIdentifier OcspSigning = new("1.3.6.1.5.5.7.3.9");

    /// <summary>id-kp-documentSigning. RFC 9336.</summary>
    public static readonly ObjectIdentifier DocumentSigning = new("1.3.6.1.5.5.7.3.36");

    // ── AuthorityInfoAccess methods ───────────────────────────────────────

    /// <summary>id-ad-caIssuers.</summary>
    public static readonly ObjectIdentifier CaIssuers = new("1.3.6.1.5.5.7.48.2");

    /// <summary>id-ad-ocsp.</summary>
    public static readonly ObjectIdentifier OcspAccess = new("1.3.6.1.5.5.7.48.1");

    // ── PKCS#7 / CMS content types (RFC 5652) ─────────────────────────────

    /// <summary>id-data — CMS Data content type.</summary>
    public static readonly ObjectIdentifier CmsData = new("1.2.840.113549.1.7.1");

    /// <summary>id-signedData — CMS SignedData content type.</summary>
    public static readonly ObjectIdentifier CmsSignedData = new("1.2.840.113549.1.7.2");

    /// <summary>id-envelopedData — CMS EnvelopedData content type.</summary>
    public static readonly ObjectIdentifier CmsEnvelopedData = new("1.2.840.113549.1.7.3");

    /// <summary>id-digestedData — CMS DigestedData content type.</summary>
    public static readonly ObjectIdentifier CmsDigestedData = new("1.2.840.113549.1.7.5");

    /// <summary>id-encryptedData — CMS EncryptedData content type.</summary>
    public static readonly ObjectIdentifier CmsEncryptedData = new("1.2.840.113549.1.7.6");

    // ── CMS signed attributes ─────────────────────────────────────────────

    /// <summary>id-contentType.</summary>
    public static readonly ObjectIdentifier ContentType = new("1.2.840.113549.1.9.3");

    /// <summary>id-messageDigest.</summary>
    public static readonly ObjectIdentifier MessageDigest = new("1.2.840.113549.1.9.4");

    /// <summary>id-signingTime.</summary>
    public static readonly ObjectIdentifier SigningTime = new("1.2.840.113549.1.9.5");

    /// <summary>id-countersignature.</summary>
    public static readonly ObjectIdentifier CounterSignature = new("1.2.840.113549.1.9.6");

    /// <summary>id-aa-signingCertificate (ESS, RFC 2634).</summary>
    public static readonly ObjectIdentifier SigningCertificate = new("1.2.840.113549.1.9.16.2.12");

    /// <summary>id-aa-signingCertificateV2 (ESS, RFC 5035).</summary>
    public static readonly ObjectIdentifier SigningCertificateV2 = new("1.2.840.113549.1.9.16.2.47");

    /// <summary>id-aa-signatureTimeStampToken — RFC 3161 timestamp embedded as unsigned attribute.</summary>
    public static readonly ObjectIdentifier SignatureTimeStampToken = new("1.2.840.113549.1.9.16.2.14");

    // ── RFC 3161 timestamping ─────────────────────────────────────────────

    /// <summary>id-ct-TSTInfo — TSA response content type.</summary>
    public static readonly ObjectIdentifier TstInfo = new("1.2.840.113549.1.9.16.1.4");

    // ── OCSP (RFC 6960) ───────────────────────────────────────────────────

    /// <summary>id-pkix-ocsp-basic — OCSP BasicResponse.</summary>
    public static readonly ObjectIdentifier OcspBasicResponse = new("1.3.6.1.5.5.7.48.1.1");

    /// <summary>id-pkix-ocsp-nonce.</summary>
    public static readonly ObjectIdentifier OcspNonce = new("1.3.6.1.5.5.7.48.1.2");

    /// <summary>id-pkix-ocsp-nocheck — certificate skipping OCSP for its own OCSP-signing cert.</summary>
    public static readonly ObjectIdentifier OcspNoCheck = new("1.3.6.1.5.5.7.48.1.5");

    // ── PDF signing (PDF 32000-1 §12.8.3.3) ───────────────────────────────

    /// <summary>adbe.pkcs7.detached — most common PDF signature SubFilter.</summary>
    public const string AdbePkcs7Detached = "adbe.pkcs7.detached";

    /// <summary>adbe.pkcs7.sha1 — legacy PDF signature SubFilter.</summary>
    public const string AdbePkcs7Sha1 = "adbe.pkcs7.sha1";

    /// <summary>adbe.x509.rsa_sha1 — legacy PDF signature SubFilter, deprecated.</summary>
    public const string AdbeX509RsaSha1 = "adbe.x509.rsa_sha1";

    /// <summary>ETSI.CAdES.detached — CAdES-based PDF signature SubFilter (eIDAS).</summary>
    public const string EtsiCAdESDetached = "ETSI.CAdES.detached";

    /// <summary>ETSI.RFC3161 — PDF document timestamp SubFilter.</summary>
    public const string EtsiRfc3161 = "ETSI.RFC3161";

    // ── PKCS#9 — additional attributes used in CMS ────────────────────────

    /// <summary>id-aa-encryp-attribute-OID (rarely seen but defined).</summary>
    public static readonly ObjectIdentifier ContentHint = new("1.2.840.113549.1.9.16.2.4");

    // ── CAdES baseline attributes (ETSI EN 319 122-1) ─────────────────────

    /// <summary>id-aa-ets-archiveTimestampV3 — CAdES-B-LTA archive timestamp.</summary>
    public static readonly ObjectIdentifier ArchiveTimestampV3 = new("0.4.0.1733.2.4");

    /// <summary>id-aa-ets-certValues — CAdES embedded certificates for LTV.</summary>
    public static readonly ObjectIdentifier CertValues = new("1.2.840.113549.1.9.16.2.23");

    /// <summary>id-aa-ets-revocationValues — CAdES embedded revocation info.</summary>
    public static readonly ObjectIdentifier RevocationValues = new("1.2.840.113549.1.9.16.2.24");

    /// <summary>id-aa-ets-certificateRefs — CAdES references to certs.</summary>
    public static readonly ObjectIdentifier CompleteCertificateRefs = new("1.2.840.113549.1.9.16.2.21");

    /// <summary>id-aa-ets-revocationRefs — CAdES references to revocation info.</summary>
    public static readonly ObjectIdentifier CompleteRevocationRefs = new("1.2.840.113549.1.9.16.2.22");
}
