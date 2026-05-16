// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.6 — Subject Alternative Name
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The Subject Alternative Name extension — additional naming forms for the
/// certificate subject.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// SubjectAltName ::= GeneralNames
/// GeneralNames ::= SEQUENCE SIZE (1..MAX) OF GeneralName
/// </code>
/// For modern web certificates, the subject CN field is often empty or
/// generic, and all hostnames are listed here as DNS GeneralNames. For PDF
/// document signing certificates, this extension typically carries an
/// rfc822Name (email address) for the signer.
/// </remarks>
public sealed class SubjectAlternativeNameExtension
{
    private readonly GeneralName[] _names;

    /// <summary>Initialises a new SubjectAlternativeNameExtension.</summary>
    public SubjectAlternativeNameExtension(IList<GeneralName> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        _names = new GeneralName[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            _names[i] = names[i];
        }
    }

    /// <summary>The alternative names for the subject.</summary>
    public ReadOnlyCollection<GeneralName> Names => new(_names);

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.SubjectAltName;

    /// <summary>Parses a SubjectAltName extension from raw extnValue bytes.</summary>
    public static SubjectAlternativeNameExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        List<GeneralName> names = GeneralName.ReadSequence(r);
        r.ExpectEnd();
        return new SubjectAlternativeNameExtension(names);
    }
}
