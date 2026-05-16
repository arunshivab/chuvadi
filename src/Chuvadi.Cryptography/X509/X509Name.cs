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
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// An X.500 distinguished name — a sequence of Relative Distinguished Names.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// Name ::= CHOICE { rdnSequence  RDNSequence }
/// RDNSequence ::= SEQUENCE OF RelativeDistinguishedName
/// </code>
/// In RFC 5280-conformant certificates the CHOICE always resolves to
/// rdnSequence. RDN order in the encoding is significant: it goes from
/// most-general (e.g. C=US) to most-specific (e.g. CN=John Doe). The textual
/// presentation in <see cref="ToString"/> uses RFC 2253/4514 order
/// (most-specific first) which is what humans expect.
/// </remarks>
public sealed class X509Name
{
    private readonly RelativeDistinguishedName[] _rdns;

    /// <summary>Initialises a new X509Name.</summary>
    /// <param name="rdns">The RDNs in encoded order (most-general first).</param>
    /// <param name="rawEncoding">The full ASN.1 TLV bytes of the original Name (preserved for signing).</param>
    public X509Name(IList<RelativeDistinguishedName> rdns, byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(rdns);
        ArgumentNullException.ThrowIfNull(rawEncoding);
        _rdns = rdns.ToArray();
        RawEncoding = rawEncoding;
    }

    /// <summary>The RDNs in encoded order (most-general first).</summary>
    public ReadOnlyCollection<RelativeDistinguishedName> Rdns => new(_rdns);

    /// <summary>
    /// The original DER encoding of the Name. Preserved because signature
    /// verification requires byte-identical comparison of issuer/subject names.
    /// </summary>
    public byte[] RawEncoding { get; }

    /// <summary>
    /// Convenience accessor: returns the value of the first CN attribute encountered,
    /// or null if no CN exists in the DN.
    /// </summary>
    public string? CommonName => FindFirst(KnownOids.CommonName);

    /// <summary>
    /// Returns the first attribute value matching <paramref name="type"/> in any RDN,
    /// or null if no such attribute exists.
    /// </summary>
    public string? FindFirst(ObjectIdentifier type)
    {
        ArgumentNullException.ThrowIfNull(type);
        foreach (RelativeDistinguishedName rdn in _rdns)
        {
            foreach (AttributeTypeAndValue attr in rdn.Attributes)
            {
                if (attr.Type.Equals(type))
                {
                    return attr.Value;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Returns all attribute values matching <paramref name="type"/> across all RDNs
    /// in encoded order.
    /// </summary>
    public IEnumerable<string> FindAll(ObjectIdentifier type)
    {
        ArgumentNullException.ThrowIfNull(type);
        foreach (RelativeDistinguishedName rdn in _rdns)
        {
            foreach (AttributeTypeAndValue attr in rdn.Attributes)
            {
                if (attr.Type.Equals(type))
                {
                    yield return attr.Value;
                }
            }
        }
    }

    /// <summary>
    /// Renders the DN in RFC 2253/4514 textual form (most-specific first,
    /// comma-separated).
    /// </summary>
    public override string ToString()
    {
        StringBuilder sb = new();
        for (int i = _rdns.Length - 1; i >= 0; i--)
        {
            if (sb.Length > 0) { sb.Append(','); }
            sb.Append(_rdns[i]);
        }
        return sb.ToString();
    }

    /// <summary>Reads a Name from a reader positioned at its SEQUENCE.</summary>
    public static X509Name Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte[] raw = reader.PeekEncoded();
        Asn1Reader seq = reader.ReadSequence();
        List<RelativeDistinguishedName> rdns = new();
        while (!seq.IsAtEnd)
        {
            rdns.Add(RelativeDistinguishedName.Read(seq));
        }
        seq.ExpectEnd();
        return new X509Name(rdns, raw);
    }
}
