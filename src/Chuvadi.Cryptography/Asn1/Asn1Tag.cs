// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.1.2 — Identifier octets
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 foundation
//
// Models a parsed ASN.1 tag: the (class, constructed-flag, number) triple
// that appears in the identifier octets of every ASN.1 element.

using System;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Immutable description of an ASN.1 tag.
/// </summary>
/// <remarks>
/// An ASN.1 tag has three pieces: a class (universal / application /
/// context-specific / private), a primitive-or-constructed flag, and a
/// tag number. Together they uniquely identify the kind of element being
/// encoded. This struct holds all three.
/// </remarks>
public readonly struct Asn1Tag : IEquatable<Asn1Tag>
{
    /// <summary>Initialises an Asn1Tag.</summary>
    /// <param name="tagClass">The tag class.</param>
    /// <param name="isConstructed">True for constructed encoding, false for primitive.</param>
    /// <param name="tagNumber">The tag number. Must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">If tagNumber is negative.</exception>
    public Asn1Tag(Asn1TagClass tagClass, bool isConstructed, int tagNumber)
    {
        if (tagNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tagNumber),
                "ASN.1 tag numbers cannot be negative.");
        }

        TagClass = tagClass;
        IsConstructed = isConstructed;
        TagNumber = tagNumber;
    }

    /// <summary>The tag class.</summary>
    public Asn1TagClass TagClass { get; }

    /// <summary>True for constructed encoding (contents are themselves encoded values).</summary>
    public bool IsConstructed { get; }

    /// <summary>The tag number.</summary>
    public int TagNumber { get; }

    /// <summary>
    /// Builds a universal-class primitive tag for the given universal type.
    /// </summary>
    public static Asn1Tag Primitive(Asn1UniversalTag tag)
        => new(Asn1TagClass.Universal, isConstructed: false, (int)tag);

    /// <summary>
    /// Builds a universal-class constructed tag for the given universal type.
    /// Used for SEQUENCE, SET, and the constructed encodings of strings.
    /// </summary>
    public static Asn1Tag Constructed(Asn1UniversalTag tag)
        => new(Asn1TagClass.Universal, isConstructed: true, (int)tag);

    /// <summary>
    /// Builds a context-specific tag with the given number and constructed flag.
    /// </summary>
    public static Asn1Tag ContextSpecific(int tagNumber, bool isConstructed)
        => new(Asn1TagClass.ContextSpecific, isConstructed, tagNumber);

    /// <inheritdoc/>
    public bool Equals(Asn1Tag other)
        => TagClass == other.TagClass
        && IsConstructed == other.IsConstructed
        && TagNumber == other.TagNumber;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Asn1Tag other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(TagClass, IsConstructed, TagNumber);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Asn1Tag left, Asn1Tag right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Asn1Tag left, Asn1Tag right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString()
    {
        string prefix = TagClass switch
        {
            Asn1TagClass.Universal => "U",
            Asn1TagClass.Application => "A",
            Asn1TagClass.ContextSpecific => "[CTX]",
            Asn1TagClass.Private => "P",
            _ => "?",
        };
        string shape = IsConstructed ? "C" : "P";
        return $"{prefix}-{shape}-{TagNumber}";
    }
}
