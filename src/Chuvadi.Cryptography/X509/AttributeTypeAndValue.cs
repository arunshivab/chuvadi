// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.1.2.4 — Name; RFC 4519 — DN attributes
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Globalization;
using System.IO;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// One attribute within a RelativeDistinguishedName — an OID identifying the
/// attribute type plus its value.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// AttributeTypeAndValue ::= SEQUENCE {
///   type   OBJECT IDENTIFIER,
///   value  ANY DEFINED BY type
/// }
/// </code>
/// In practice the value is almost always one of the directory string types
/// (UTF8String, PrintableString, T61String, BMPString) or IA5String for
/// emailAddress. Chuvadi exposes both the original tag-class and the decoded
/// string so callers can preserve canonical encodings when re-serialising.
/// </remarks>
public sealed class AttributeTypeAndValue
{
    /// <summary>Initialises a new AttributeTypeAndValue.</summary>
    public AttributeTypeAndValue(ObjectIdentifier type, string value, Asn1UniversalTag valueTag)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(value);
        Type = type;
        Value = value;
        ValueTag = valueTag;
    }

    /// <summary>The attribute type OID (e.g. KnownOids.CommonName).</summary>
    public ObjectIdentifier Type { get; }

    /// <summary>The decoded string value.</summary>
    public string Value { get; }

    /// <summary>The original ASN.1 string tag of the encoded value.</summary>
    public Asn1UniversalTag ValueTag { get; }

    /// <summary>The short attribute name (e.g. "CN", "O") if registered, otherwise the OID dotted form.</summary>
    public string ShortName => GetShortName(Type);

    private static string GetShortName(ObjectIdentifier oid)
    {
        if (oid.Equals(KnownOids.CommonName)) { return "CN"; }
        if (oid.Equals(KnownOids.Surname)) { return "SN"; }
        if (oid.Equals(KnownOids.SerialNumber)) { return "serialNumber"; }
        if (oid.Equals(KnownOids.CountryName)) { return "C"; }
        if (oid.Equals(KnownOids.LocalityName)) { return "L"; }
        if (oid.Equals(KnownOids.StateOrProvinceName)) { return "ST"; }
        if (oid.Equals(KnownOids.OrganizationName)) { return "O"; }
        if (oid.Equals(KnownOids.OrganizationalUnitName)) { return "OU"; }
        if (oid.Equals(KnownOids.Title)) { return "T"; }
        if (oid.Equals(KnownOids.GivenName)) { return "GN"; }
        if (oid.Equals(KnownOids.Initials)) { return "initials"; }
        if (oid.Equals(KnownOids.Pseudonym)) { return "pseudonym"; }
        if (oid.Equals(KnownOids.EmailAddress)) { return "E"; }
        if (oid.Equals(KnownOids.DomainComponent)) { return "DC"; }
        return oid.Dotted;
    }

    /// <inheritdoc/>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{ShortName}={Value}");

    /// <summary>
    /// Reads an AttributeTypeAndValue from a reader positioned at its SEQUENCE.
    /// </summary>
    public static AttributeTypeAndValue Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        ObjectIdentifier type = seq.ReadObjectIdentifier();

        Asn1Tag valueTag = seq.PeekTag();
        if (valueTag.TagClass != Asn1TagClass.Universal)
        {
            throw new Asn1Exception(
                $"AttributeTypeAndValue value must have universal class tag, got {valueTag}");
        }

        Asn1UniversalTag universal = (Asn1UniversalTag)valueTag.TagNumber;
        string value = universal switch
        {
            Asn1UniversalTag.Utf8String => seq.ReadUtf8String(),
            Asn1UniversalTag.PrintableString => seq.ReadPrintableString(),
            Asn1UniversalTag.IA5String => seq.ReadIA5String(),
            Asn1UniversalTag.BmpString => seq.ReadBmpString(),
            Asn1UniversalTag.T61String => ReadT61(seq),
            _ => throw new Asn1Exception(
                $"AttributeTypeAndValue value has unsupported tag {universal}"),
        };

        seq.ExpectEnd();
        return new AttributeTypeAndValue(type, value, universal);
    }

    private static string ReadT61(Asn1Reader reader)
    {
        // Asn1Reader doesn't have a dedicated T61 method; encode the captured TLV
        // and run it through Asn1String.ReadT61 directly.
        byte[] encoded = reader.ReadEncoded();
        Asn1String.ReadT61(encoded, 0, out string value);
        // No-op stream usage just to satisfy "unused argument" guards if any.
        using MemoryStream _ = new(encoded);
        return value;
    }
}
