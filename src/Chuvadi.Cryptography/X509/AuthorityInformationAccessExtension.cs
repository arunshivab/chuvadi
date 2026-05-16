// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.2.1 — Authority Information Access
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>One access description inside an AuthorityInfoAccess extension.</summary>
public sealed class AccessDescription
{
    /// <summary>Initialises a new AccessDescription.</summary>
    public AccessDescription(ObjectIdentifier method, GeneralName location)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(location);
        Method = method;
        Location = location;
    }

    /// <summary>The access method OID (caIssuers, ocsp, ...).</summary>
    public ObjectIdentifier Method { get; }

    /// <summary>The location of the resource.</summary>
    public GeneralName Location { get; }
}

/// <summary>
/// The Authority Information Access extension — pointers to additional
/// resources about the certificate's issuer (typically caIssuers and OCSP).
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// AuthorityInfoAccessSyntax ::= SEQUENCE SIZE (1..MAX) OF AccessDescription
/// AccessDescription ::= SEQUENCE {
///   accessMethod   OBJECT IDENTIFIER,
///   accessLocation GeneralName
/// }
/// </code>
/// </remarks>
public sealed class AuthorityInformationAccessExtension
{
    private readonly AccessDescription[] _descriptions;

    /// <summary>Initialises a new AuthorityInformationAccessExtension.</summary>
    public AuthorityInformationAccessExtension(IList<AccessDescription> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        _descriptions = descriptions.ToArray();
    }

    /// <summary>The access descriptions.</summary>
    public ReadOnlyCollection<AccessDescription> Descriptions => new(_descriptions);

    /// <summary>The first OCSP URI in this extension, or null when none exists.</summary>
    public string? OcspUri
    {
        get
        {
            foreach (AccessDescription d in _descriptions)
            {
                if (d.Method.Equals(KnownOids.OcspAccess)
                    && d.Location.Kind == GeneralNameKind.UniformResourceIdentifier)
                {
                    return d.Location.StringValue;
                }
            }
            return null;
        }
    }

    /// <summary>The first caIssuers URI, or null when none exists.</summary>
    public string? CaIssuersUri
    {
        get
        {
            foreach (AccessDescription d in _descriptions)
            {
                if (d.Method.Equals(KnownOids.CaIssuers)
                    && d.Location.Kind == GeneralNameKind.UniformResourceIdentifier)
                {
                    return d.Location.StringValue;
                }
            }
            return null;
        }
    }

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.AuthorityInfoAccess;

    /// <summary>Parses an AIA extension from raw extnValue bytes.</summary>
    public static AuthorityInformationAccessExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        Asn1Reader seq = r.ReadSequence();
        List<AccessDescription> descriptions = new();
        while (!seq.IsAtEnd)
        {
            Asn1Reader desc = seq.ReadSequence();
            ObjectIdentifier method = desc.ReadObjectIdentifier();
            GeneralName location = GeneralName.Read(desc);
            desc.ExpectEnd();
            descriptions.Add(new AccessDescription(method, location));
        }
        seq.ExpectEnd();
        r.ExpectEnd();
        return new AuthorityInformationAccessExtension(descriptions);
    }
}
