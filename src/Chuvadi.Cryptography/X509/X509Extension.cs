// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2 — Extensions
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// A single X.509 v3 extension — an OID, a criticality flag, and an opaque
/// OCTET STRING value whose contents are defined per OID.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// Extension ::= SEQUENCE {
///   extnID     OBJECT IDENTIFIER,
///   critical   BOOLEAN DEFAULT FALSE,
///   extnValue  OCTET STRING
/// }
/// </code>
/// The criticality flag has security significance: per RFC 5280 §4.2, a
/// relying party MUST reject a certificate with a critical extension it does
/// not understand. The raw extnValue bytes are preserved; specialised parsers
/// (BasicConstraintsExtension, KeyUsageExtension, etc.) interpret them on demand.
/// </remarks>
public sealed class X509Extension
{
    /// <summary>Initialises a new X509Extension.</summary>
    public X509Extension(ObjectIdentifier oid, bool critical, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(oid);
        ArgumentNullException.ThrowIfNull(value);
        Oid = oid;
        Critical = critical;
        Value = value;
    }

    /// <summary>The extension OID.</summary>
    public ObjectIdentifier Oid { get; }

    /// <summary>True when this extension is marked critical.</summary>
    public bool Critical { get; }

    /// <summary>The raw extnValue contents (the bytes inside the OCTET STRING wrapper).</summary>
    public byte[] Value { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        string critical = Critical ? " (critical)" : string.Empty;
        return $"{OidNameLookup.GetName(Oid)}{critical}, {Value.Length} bytes";
    }

    /// <summary>Reads an X509Extension from a reader positioned at its SEQUENCE.</summary>
    public static X509Extension Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        ObjectIdentifier oid = seq.ReadObjectIdentifier();

        bool critical = false;
        if (seq.TryPeekTag(Asn1Tag.Primitive(Asn1UniversalTag.Boolean)))
        {
            critical = seq.ReadBoolean();
        }

        byte[] value = seq.ReadOctetString();
        seq.ExpectEnd();
        return new X509Extension(oid, critical, value);
    }
}
