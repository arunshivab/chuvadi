// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.13 — CRL Distribution Points
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>One distribution point inside a CRLDistributionPoints extension.</summary>
public sealed class DistributionPoint
{
    /// <summary>Initialises a new DistributionPoint.</summary>
    public DistributionPoint(IList<GeneralName> fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        FullName = new(Enumerable.ToArray(fullName));
    }

    /// <summary>The full names of the CRL distribution endpoint(s).</summary>
    public ReadOnlyCollection<GeneralName> FullName { get; }
}

/// <summary>
/// The CRL Distribution Points extension — locations from which the issuer's
/// Certificate Revocation List may be retrieved.
/// </summary>
/// <remarks>
/// Structure (simplified — Chuvadi tracks only the fullName variant which
/// covers virtually all real-world certificates):
/// <code>
/// CRLDistributionPoints ::= SEQUENCE SIZE (1..MAX) OF DistributionPoint
/// DistributionPoint ::= SEQUENCE {
///   distributionPoint [0] DistributionPointName OPTIONAL,
///   reasons           [1] ReasonFlags OPTIONAL,
///   cRLIssuer         [2] GeneralNames OPTIONAL
/// }
/// DistributionPointName ::= CHOICE {
///   fullName                [0] GeneralNames,
///   nameRelativeToCRLIssuer [1] RelativeDistinguishedName
/// }
/// </code>
/// </remarks>
public sealed class CrlDistributionPointsExtension
{
    private readonly DistributionPoint[] _points;

    /// <summary>Initialises a new CrlDistributionPointsExtension.</summary>
    public CrlDistributionPointsExtension(IList<DistributionPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = Enumerable.ToArray(points);
    }

    /// <summary>The distribution points.</summary>
    public ReadOnlyCollection<DistributionPoint> Points => new(_points);

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.CrlDistributionPoints;

    /// <summary>Parses a CRLDistributionPoints extension from raw extnValue bytes.</summary>
    public static CrlDistributionPointsExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        Asn1Reader seq = r.ReadSequence();
        List<DistributionPoint> points = new();
        while (!seq.IsAtEnd)
        {
            points.Add(ReadDistributionPoint(seq));
        }
        seq.ExpectEnd();
        r.ExpectEnd();
        return new CrlDistributionPointsExtension(points);
    }

    private static DistributionPoint ReadDistributionPoint(Asn1Reader reader)
    {
        Asn1Reader dp = reader.ReadSequence();
        List<GeneralName> fullName = new();

        if (dp.HasContextSpecific(0))
        {
            Asn1Reader dpn = dp.ReadExplicit(0);
            if (dpn.HasContextSpecific(0))
            {
                // fullName [0] IMPLICIT GeneralNames — but GeneralNames is a SEQUENCE,
                // so the IMPLICIT tag replaces the SEQUENCE tag. We need to walk its content.
                byte[] gnBytes = dpn.ReadImplicitOctets(0);
                Asn1Reader gnReader = new(gnBytes);
                while (!gnReader.IsAtEnd)
                {
                    fullName.Add(GeneralName.Read(gnReader));
                }
            }
            else
            {
                // nameRelativeToCRLIssuer is rare; skip silently and leave fullName empty.
                dpn.Skip();
            }
            dpn.ExpectEnd();
        }

        // Skip optional reasons and cRLIssuer.
        while (!dp.IsAtEnd)
        {
            dp.Skip();
        }
        dp.ExpectEnd();
        return new DistributionPoint(fullName);
    }
}
