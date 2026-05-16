// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.9 — Basic Constraints
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The Basic Constraints extension — identifies CA certificates and bounds
/// the depth of the chain they may issue.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// BasicConstraints ::= SEQUENCE {
///   cA                BOOLEAN DEFAULT FALSE,
///   pathLenConstraint INTEGER (0..MAX) OPTIONAL
/// }
/// </code>
/// Per RFC 5280 §4.2.1.9, pathLenConstraint is meaningful only when cA is TRUE
/// and the keyCertSign bit is set in KeyUsage. A value of N means at most N
/// intermediate CA certificates may follow this one in a certification path.
/// </remarks>
public sealed class BasicConstraintsExtension
{
    /// <summary>Initialises a new BasicConstraintsExtension.</summary>
    public BasicConstraintsExtension(bool isCa, int? pathLenConstraint)
    {
        if (pathLenConstraint is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pathLenConstraint),
                "pathLenConstraint must be non-negative.");
        }
        IsCa = isCa;
        PathLenConstraint = pathLenConstraint;
    }

    /// <summary>True when the subject is a Certification Authority.</summary>
    public bool IsCa { get; }

    /// <summary>The maximum path length constraint (null = unconstrained).</summary>
    public int? PathLenConstraint { get; }

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.BasicConstraints;

    /// <summary>Parses a BasicConstraints extension from the raw extnValue bytes.</summary>
    public static BasicConstraintsExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        Asn1Reader seq = r.ReadSequence();

        bool isCa = false;
        if (seq.TryPeekTag(Asn1Tag.Primitive(Asn1UniversalTag.Boolean)))
        {
            isCa = seq.ReadBoolean();
        }

        int? pathLen = null;
        if (!seq.IsAtEnd)
        {
            pathLen = seq.ReadInt32();
        }

        seq.ExpectEnd();
        r.ExpectEnd();
        return new BasicConstraintsExtension(isCa, pathLen);
    }
}
