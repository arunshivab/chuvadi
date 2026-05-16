// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §5.3, §11 — SignedAttributes / UnsignedAttributes
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// A collection of <see cref="CmsAttribute"/> values, with OID lookup and the
/// raw encoded bytes preserved for signature verification.
/// </summary>
/// <remarks>
/// For SignedAttributes (RFC 5652 §5.4), the byte sequence over which the
/// signature is computed is the DER encoding of the SET OF Attribute — not
/// the IMPLICIT [0] tagged form that appears on the wire. The
/// <see cref="DerEncodedForVerification"/> property holds the bytes needed
/// for that verification step.
/// </remarks>
public sealed class CmsAttributeTable
{
    private readonly CmsAttribute[] _attributes;

    /// <summary>Initialises a new CmsAttributeTable.</summary>
    public CmsAttributeTable(IList<CmsAttribute> attributes, byte[] derEncodedForVerification)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(derEncodedForVerification);
        _attributes = attributes.ToArray();
        DerEncodedForVerification = derEncodedForVerification;
    }

    /// <summary>The attributes in their original order.</summary>
    public ReadOnlyCollection<CmsAttribute> Attributes => new(_attributes);

    /// <summary>The number of attributes in the table.</summary>
    public int Count => _attributes.Length;

    /// <summary>
    /// The DER encoding of the SET OF Attribute, with the universal SET tag (0x31)
    /// substituted for the IMPLICIT [0] tag that appeared on the wire. These are
    /// the bytes the signature actually covers per RFC 5652 §5.4.
    /// </summary>
    public byte[] DerEncodedForVerification { get; }

    /// <summary>Returns the first attribute matching <paramref name="oid"/>, or null when absent.</summary>
    public CmsAttribute? Find(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        foreach (CmsAttribute a in _attributes)
        {
            if (a.Type.Equals(oid)) { return a; }
        }
        return null;
    }

    /// <summary>Returns all attributes matching <paramref name="oid"/>.</summary>
    public IEnumerable<CmsAttribute> FindAll(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        foreach (CmsAttribute a in _attributes)
        {
            if (a.Type.Equals(oid)) { yield return a; }
        }
    }
}
