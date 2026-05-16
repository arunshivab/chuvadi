// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.2 — Subject Key Identifier
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The Subject Key Identifier extension — a short octet string identifying
/// the certificate's public key, used to find issuer certificates during
/// path building.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// SubjectKeyIdentifier ::= KeyIdentifier
/// KeyIdentifier ::= OCTET STRING
/// </code>
/// The most common derivation method is the SHA-1 hash of the SubjectPublicKey
/// BIT STRING contents (RFC 5280 §4.2.1.2 method 1), but the field is opaque
/// and any unique identifier is permitted.
/// </remarks>
public sealed class SubjectKeyIdentifierExtension
{
    /// <summary>Initialises a new SubjectKeyIdentifierExtension.</summary>
    public SubjectKeyIdentifierExtension(byte[] keyIdentifier)
    {
        ArgumentNullException.ThrowIfNull(keyIdentifier);
        KeyIdentifier = keyIdentifier;
    }

    /// <summary>The key identifier bytes.</summary>
    public byte[] KeyIdentifier { get; }

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.SubjectKeyIdentifier;

    /// <summary>Parses a SubjectKeyIdentifier extension from raw extnValue bytes.</summary>
    public static SubjectKeyIdentifierExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        byte[] ki = r.ReadOctetString();
        r.ExpectEnd();
        return new SubjectKeyIdentifierExtension(ki);
    }
}
