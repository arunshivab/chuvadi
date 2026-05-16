// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.12 — Extended Key Usage
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The Extended Key Usage extension — additional or alternative purposes for
/// which the certified public key may be used.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// ExtKeyUsageSyntax ::= SEQUENCE SIZE (1..MAX) OF KeyPurposeId
/// KeyPurposeId ::= OBJECT IDENTIFIER
/// </code>
/// Common purposes registered in <see cref="KnownOids"/>: ServerAuth, ClientAuth,
/// CodeSigning, EmailProtection, TimeStamping, OcspSigning, DocumentSigning.
/// </remarks>
public sealed class ExtendedKeyUsageExtension
{
    private readonly ObjectIdentifier[] _purposes;

    /// <summary>Initialises a new ExtendedKeyUsageExtension.</summary>
    public ExtendedKeyUsageExtension(IList<ObjectIdentifier> purposes)
    {
        ArgumentNullException.ThrowIfNull(purposes);
        _purposes = purposes.ToArray();
    }

    /// <summary>The set of key purpose OIDs.</summary>
    public ReadOnlyCollection<ObjectIdentifier> Purposes => new(_purposes);

    /// <summary>True when the given purpose OID is present.</summary>
    public bool Allows(ObjectIdentifier purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        foreach (ObjectIdentifier p in _purposes)
        {
            if (p.Equals(purpose)) { return true; }
        }
        return false;
    }

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.ExtKeyUsage;

    /// <summary>Parses an ExtendedKeyUsage extension from raw extnValue bytes.</summary>
    public static ExtendedKeyUsageExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        Asn1Reader seq = r.ReadSequence();
        List<ObjectIdentifier> purposes = new();
        while (!seq.IsAtEnd)
        {
            purposes.Add(seq.ReadObjectIdentifier());
        }
        seq.ExpectEnd();
        r.ExpectEnd();
        return new ExtendedKeyUsageExtension(purposes);
    }
}
