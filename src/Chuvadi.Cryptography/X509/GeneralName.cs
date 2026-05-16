// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.6 — General Names
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.X509;

/// <summary>The variant types within a GeneralName CHOICE.</summary>
public enum GeneralNameKind
{
    /// <summary>otherName [0] — OtherName SEQUENCE.</summary>
    OtherName = 0,
    /// <summary>rfc822Name [1] — IA5String.</summary>
    Rfc822Name = 1,
    /// <summary>dNSName [2] — IA5String.</summary>
    DnsName = 2,
    /// <summary>x400Address [3] — ORAddress (raw).</summary>
    X400Address = 3,
    /// <summary>directoryName [4] — Name.</summary>
    DirectoryName = 4,
    /// <summary>ediPartyName [5] — EDIPartyName (raw).</summary>
    EdiPartyName = 5,
    /// <summary>uniformResourceIdentifier [6] — IA5String.</summary>
    UniformResourceIdentifier = 6,
    /// <summary>iPAddress [7] — OCTET STRING.</summary>
    IpAddress = 7,
    /// <summary>registeredID [8] — OBJECT IDENTIFIER.</summary>
    RegisteredId = 8,
}

/// <summary>
/// One alternative naming form for a certificate subject or other entity.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// GeneralName ::= CHOICE {
///   otherName                 [0] OtherName,
///   rfc822Name                [1] IA5String,
///   dNSName                   [2] IA5String,
///   x400Address               [3] ORAddress,
///   directoryName             [4] Name,
///   ediPartyName              [5] EDIPartyName,
///   uniformResourceIdentifier [6] IA5String,
///   iPAddress                 [7] OCTET STRING,
///   registeredID              [8] OBJECT IDENTIFIER
/// }
/// </code>
/// </remarks>
public sealed class GeneralName
{
    private GeneralName(GeneralNameKind kind, string? stringValue, byte[]? rawValue,
        X509Name? directoryName, ObjectIdentifier? oidValue)
    {
        Kind = kind;
        StringValue = stringValue;
        RawValue = rawValue;
        DirectoryName = directoryName;
        OidValue = oidValue;
    }

    /// <summary>Which CHOICE variant this name represents.</summary>
    public GeneralNameKind Kind { get; }

    /// <summary>The string value for the rfc822Name, dNSName, and URI variants.</summary>
    public string? StringValue { get; }

    /// <summary>The raw value for variants without a structured Chuvadi representation.</summary>
    public byte[]? RawValue { get; }

    /// <summary>The decoded value for the directoryName variant.</summary>
    public X509Name? DirectoryName { get; }

    /// <summary>The OID value for the registeredID variant.</summary>
    public ObjectIdentifier? OidValue { get; }

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        GeneralNameKind.Rfc822Name => $"rfc822:{StringValue}",
        GeneralNameKind.DnsName => $"DNS:{StringValue}",
        GeneralNameKind.UniformResourceIdentifier => $"URI:{StringValue}",
        GeneralNameKind.IpAddress => $"IP:{FormatIp(RawValue!)}",
        GeneralNameKind.DirectoryName => $"DirName:{DirectoryName}",
        GeneralNameKind.RegisteredId => $"RID:{OidValue}",
        _ => $"{Kind}:{RawValue?.Length ?? 0} bytes",
    };

    private static string FormatIp(byte[] bytes) => bytes.Length switch
    {
        4 => $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}",
        16 => FormatIPv6(bytes),
        _ => $"{bytes.Length}-byte address",
    };

    private static string FormatIPv6(byte[] b)
    {
        StringBuilder sb = new();
        for (int i = 0; i < 16; i += 2)
        {
            if (i > 0) { sb.Append(':'); }
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:x}", (b[i] << 8) | b[i + 1]);
        }
        return sb.ToString();
    }

    /// <summary>Reads the next GeneralName from the reader.</summary>
    public static GeneralName Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Tag tag = reader.PeekTag();
        if (tag.TagClass != Asn1TagClass.ContextSpecific)
        {
            throw new Asn1Exception($"GeneralName must be context-specific, got {tag}");
        }

        GeneralNameKind kind = (GeneralNameKind)tag.TagNumber;
        switch (kind)
        {
            case GeneralNameKind.Rfc822Name:
            case GeneralNameKind.DnsName:
            case GeneralNameKind.UniformResourceIdentifier:
                {
                    byte[] content = reader.ReadImplicitOctets(tag.TagNumber);
                    string s = Encoding.ASCII.GetString(content);
                    return new GeneralName(kind, s, null, null, null);
                }
            case GeneralNameKind.IpAddress:
            case GeneralNameKind.OtherName:
            case GeneralNameKind.X400Address:
            case GeneralNameKind.EdiPartyName:
                {
                    byte[] raw = reader.ReadImplicitOctets(tag.TagNumber);
                    return new GeneralName(kind, null, raw, null, null);
                }
            case GeneralNameKind.DirectoryName:
                {
                    // directoryName is [4] EXPLICIT because Name is itself a CHOICE.
                    Asn1Reader inner = reader.ReadExplicit(tag.TagNumber);
                    X509Name name = X509Name.Read(inner);
                    inner.ExpectEnd();
                    return new GeneralName(kind, null, null, name, null);
                }
            case GeneralNameKind.RegisteredId:
                {
                    // [8] IMPLICIT OBJECT IDENTIFIER. The implicit tag replaces the OID universal tag.
                    byte[] contentBytes = reader.ReadImplicitOctets(tag.TagNumber);
                    ObjectIdentifier oid = Asn1ObjectIdentifier.DecodeContent(
                        contentBytes, 0, contentBytes.Length, errorOffset: 0);
                    return new GeneralName(kind, null, null, null, oid);
                }
            default:
                throw new Asn1Exception($"Unknown GeneralName tag [{tag.TagNumber}]");
        }
    }

    /// <summary>Reads a SEQUENCE OF GeneralName (i.e. a GeneralNames structure).</summary>
    public static List<GeneralName> ReadSequence(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        List<GeneralName> result = new();
        while (!seq.IsAtEnd)
        {
            result.Add(Read(seq));
        }
        seq.ExpectEnd();
        return result;
    }
}
