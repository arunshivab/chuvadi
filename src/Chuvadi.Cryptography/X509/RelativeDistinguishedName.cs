// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.1.2.4 — Name
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// A SET of one or more attributes that together form one component of a DN.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// RelativeDistinguishedName ::= SET SIZE (1..MAX) OF AttributeTypeAndValue
/// </code>
/// In real-world certificates an RDN almost always contains a single attribute;
/// multi-valued RDNs are rare but legal. Order within the SET is not significant
/// for DN comparison but Chuvadi preserves the encoded order for re-serialisation.
/// </remarks>
public sealed class RelativeDistinguishedName
{
    private readonly AttributeTypeAndValue[] _attributes;

    /// <summary>Initialises a new RDN.</summary>
    public RelativeDistinguishedName(IList<AttributeTypeAndValue> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Count == 0)
        {
            throw new ArgumentException("RDN must contain at least one attribute.", nameof(attributes));
        }
        _attributes = attributes.ToArray();
    }

    /// <summary>The attributes that make up this RDN.</summary>
    public ReadOnlyCollection<AttributeTypeAndValue> Attributes
        => new(_attributes);

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder sb = new();
        for (int i = 0; i < _attributes.Length; i++)
        {
            if (i > 0) { sb.Append('+'); }
            sb.Append(_attributes[i]);
        }
        return sb.ToString();
    }

    /// <summary>Reads an RDN from a reader positioned at its SET.</summary>
    public static RelativeDistinguishedName Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader set = reader.ReadSet();
        List<AttributeTypeAndValue> attributes = new();
        while (!set.IsAtEnd)
        {
            attributes.Add(AttributeTypeAndValue.Read(set));
        }
        set.ExpectEnd();
        return new RelativeDistinguishedName(attributes);
    }
}
