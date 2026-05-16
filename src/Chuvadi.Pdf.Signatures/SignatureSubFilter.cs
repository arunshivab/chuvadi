// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1 §12.8.3.3 — Signature SubFilter values
// PHASE: Phase 1.1.4 — PDF signature field reading

namespace Chuvadi.Pdf.Signatures;

/// <summary>
/// Constants and helpers for the /SubFilter entry of a PDF signature dictionary.
/// </summary>
/// <remarks>
/// The /SubFilter value determines how the /Contents bytes are encoded and
/// what kind of cryptographic structure they hold.
/// </remarks>
public static class SignatureSubFilter
{
    /// <summary>adbe.pkcs7.detached — most common modern PDF signature SubFilter.</summary>
    public const string AdbePkcs7Detached = "adbe.pkcs7.detached";

    /// <summary>adbe.pkcs7.sha1 — legacy signature; the /Contents is a PKCS#7 wrapping a SHA-1 digest.</summary>
    public const string AdbePkcs7Sha1 = "adbe.pkcs7.sha1";

    /// <summary>adbe.x509.rsa_sha1 — legacy raw signature; deprecated.</summary>
    public const string AdbeX509RsaSha1 = "adbe.x509.rsa_sha1";

    /// <summary>ETSI.CAdES.detached — CAdES-based PDF signature SubFilter (eIDAS qualified signatures).</summary>
    public const string EtsiCAdESDetached = "ETSI.CAdES.detached";

    /// <summary>ETSI.RFC3161 — PDF document timestamp SubFilter.</summary>
    public const string EtsiRfc3161 = "ETSI.RFC3161";

    /// <summary>
    /// Returns true when <paramref name="subFilter"/> indicates the /Contents value
    /// carries a CMS / PKCS#7 SignedData container (covered by Chuvadi.Cryptography.Cms).
    /// </summary>
    public static bool IsCmsBased(string subFilter)
    {
        if (subFilter is null) { return false; }
        return subFilter == AdbePkcs7Detached
            || subFilter == AdbePkcs7Sha1
            || subFilter == EtsiCAdESDetached
            || subFilter == EtsiRfc3161;
    }
}
