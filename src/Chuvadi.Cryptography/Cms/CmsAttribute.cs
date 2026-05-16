// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §11.1 — Attribute
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// A generic CMS Attribute — an OID identifying the attribute type, plus a
/// SET of one or more values whose content is defined per OID.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// Attribute ::= SEQUENCE {
///   attrType   OBJECT IDENTIFIER,
///   attrValues SET OF AttributeValue
/// }
/// AttributeValue ::= ANY
/// </code>
/// Each value is preserved as its complete encoded TLV bytes so callers can
/// decode them with whatever specific parser the attrType demands. Common
/// attrTypes registered in <see cref="Chuvadi.Cryptography.Oids.KnownOids"/>:
/// ContentType, MessageDigest, SigningTime, SigningCertificate(V2), 
/// SignatureTimeStampToken.
/// </remarks>
public sealed class CmsAttribute
{
    private readonly byte[][] _values;

    /// <summary>Initialises a new CmsAttribute.</summary>
    public CmsAttribute(ObjectIdentifier type, IList<byte[]> values)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException(
                "An Attribute must contain at least one value (CMS forbids empty SET OF).",
                nameof(values));
        }
        Type = type;
        _values = values.Select(v => v ?? throw new ArgumentNullException(nameof(values),
            "Attribute values must not be null.")).ToArray();
    }

    /// <summary>The attribute type OID.</summary>
    public ObjectIdentifier Type { get; }

    /// <summary>
    /// The complete encoded TLV bytes of each value in the order they appeared in the SET.
    /// </summary>
    public ReadOnlyCollection<byte[]> Values => new(_values);

    /// <summary>True when the attribute carries exactly one value (the typical case).</summary>
    public bool IsSingleValued => _values.Length == 1;

    /// <summary>Returns the single value, or throws when there are multiple.</summary>
    public byte[] SingleValue
    {
        get
        {
            if (_values.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Attribute {Type} has {_values.Length} values; expected exactly one.");
            }
            return _values[0];
        }
    }

    /// <summary>Reads a CmsAttribute from a reader at its SEQUENCE.</summary>
    public static CmsAttribute Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        ObjectIdentifier type = seq.ReadObjectIdentifier();

        Asn1Reader set = seq.ReadSet();
        List<byte[]> values = new();
        while (!set.IsAtEnd)
        {
            values.Add(set.ReadEncoded());
        }
        set.ExpectEnd();
        seq.ExpectEnd();
        return new CmsAttribute(type, values);
    }
}
