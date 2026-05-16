// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.1 — Authority Key Identifier
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The Authority Key Identifier extension — identifies the public key whose
/// holder signed this certificate. Used for issuer lookup during path building.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// AuthorityKeyIdentifier ::= SEQUENCE {
///   keyIdentifier             [0] IMPLICIT KeyIdentifier OPTIONAL,
///   authorityCertIssuer       [1] IMPLICIT GeneralNames OPTIONAL,
///   authorityCertSerialNumber [2] IMPLICIT CertificateSerialNumber OPTIONAL
/// }
/// </code>
/// At least one of the three fields is typically present. Most certificates
/// supply only keyIdentifier (matching the issuer's SubjectKeyIdentifier).
/// Chuvadi exposes all three; the GeneralNames payload is kept raw because
/// the GeneralName CHOICE has its own non-trivial decoder.
/// </remarks>
public sealed class AuthorityKeyIdentifierExtension
{
    /// <summary>Initialises a new AuthorityKeyIdentifierExtension.</summary>
    public AuthorityKeyIdentifierExtension(
        byte[]? keyIdentifier,
        byte[]? authorityCertIssuerRaw,
        BigInteger? authorityCertSerialNumber)
    {
        KeyIdentifier = keyIdentifier;
        AuthorityCertIssuerRaw = authorityCertIssuerRaw;
        AuthorityCertSerialNumber = authorityCertSerialNumber;
    }

    /// <summary>The issuer's key identifier (typically SHA-1 of its SubjectPublicKey).</summary>
    public byte[]? KeyIdentifier { get; }

    /// <summary>The raw bytes of authorityCertIssuer (a GeneralNames CHOICE).</summary>
    public byte[]? AuthorityCertIssuerRaw { get; }

    /// <summary>The serial number of the authority certificate.</summary>
    public BigInteger? AuthorityCertSerialNumber { get; }

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.AuthorityKeyIdentifier;

    /// <summary>Parses an AuthorityKeyIdentifier extension from raw extnValue bytes.</summary>
    public static AuthorityKeyIdentifierExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        Asn1Reader seq = r.ReadSequence();

        byte[]? keyId = null;
        byte[]? issuerRaw = null;
        BigInteger? serial = null;

        while (!seq.IsAtEnd)
        {
            if (seq.HasContextSpecific(0))
            {
                keyId = seq.ReadImplicitOctets(0);
            }
            else if (seq.HasContextSpecific(1))
            {
                issuerRaw = seq.ReadImplicitOctets(1);
            }
            else if (seq.HasContextSpecific(2))
            {
                byte[] serialBytes = seq.ReadImplicitOctets(2);
                serial = Asn1Integer.DecodeContent(serialBytes, 0, serialBytes.Length, errorOffset: 0);
            }
            else
            {
                throw new Asn1Exception(
                    $"Unexpected element in AuthorityKeyIdentifier: {seq.PeekTag()}");
            }
        }

        seq.ExpectEnd();
        r.ExpectEnd();
        return new AuthorityKeyIdentifierExtension(keyId, issuerRaw, serial);
    }
}
