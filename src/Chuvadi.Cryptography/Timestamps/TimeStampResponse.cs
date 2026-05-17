// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3161 §2.4.2 — Time-Stamp Protocol Response
// PHASE: Phase 1.2.3 — TSA fetching

using System;
using System.Collections.Generic;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// Status code from a TSA response per RFC 3161 §2.4.2 (PKIStatus).
/// </summary>
public enum TimeStampStatus
{
    /// <summary>Granted — timestamp produced as requested.</summary>
    Granted = 0,

    /// <summary>Granted with modifications — TSA chose different parameters but produced a token.</summary>
    GrantedWithMods = 1,

    /// <summary>Rejection — request refused.</summary>
    Rejection = 2,

    /// <summary>Waiting — TSA needs more time (not used for synchronous TSP).</summary>
    Waiting = 3,

    /// <summary>Revocation warning.</summary>
    RevocationWarning = 4,

    /// <summary>Revocation notification.</summary>
    RevocationNotification = 5,
}

/// <summary>
/// An RFC 3161 Time-Stamp Protocol response, as returned by a TSA.
/// </summary>
/// <remarks>
/// ASN.1 structure (RFC 3161 §2.4.2):
/// <code>
/// TimeStampResp ::= SEQUENCE {
///   status         PKIStatusInfo,
///   timeStampToken TimeStampToken  OPTIONAL
/// }
/// PKIStatusInfo ::= SEQUENCE {
///   status         PKIStatus,
///   statusString   PKIFreeText  OPTIONAL,
///   failInfo       PKIFailureInfo  OPTIONAL
/// }
/// </code>
/// On success (<see cref="TimeStampStatus.Granted"/> or
/// <see cref="TimeStampStatus.GrantedWithMods"/>), <see cref="TimeStampToken"/>
/// is non-null and carries the TSA's signed token.
/// </remarks>
public sealed class TimeStampResponse
{
    /// <summary>Initialises a new response.</summary>
    public TimeStampResponse(
        TimeStampStatus status,
        IReadOnlyList<string> statusStrings,
        TimeStampToken? timeStampToken)
    {
        ArgumentNullException.ThrowIfNull(statusStrings);
        Status = status;
        StatusStrings = statusStrings;
        TimeStampToken = timeStampToken;
    }

    /// <summary>Decoded PKIStatus value.</summary>
    public TimeStampStatus Status { get; }

    /// <summary>Human-readable status strings from the TSA, if any.</summary>
    public IReadOnlyList<string> StatusStrings { get; }

    /// <summary>The timestamp token; non-null on success.</summary>
    public TimeStampToken? TimeStampToken { get; }

    /// <summary>True iff the request was granted (with or without mods) and a token is present.</summary>
    public bool IsGranted =>
        TimeStampToken is not null
        && (Status == TimeStampStatus.Granted || Status == TimeStampStatus.GrantedWithMods);

    /// <summary>
    /// Decodes a DER-encoded RFC 3161 TimeStampResp. Throws on malformed input.
    /// </summary>
    public static TimeStampResponse Decode(byte[] der)
    {
        ArgumentNullException.ThrowIfNull(der);
        Asn1Reader root = new(der);
        Asn1Reader resp = root.ReadSequence();

        Asn1Reader statusInfo = resp.ReadSequence();
        BigInteger statusInt = statusInfo.ReadInteger();
        TimeStampStatus status = (TimeStampStatus)(int)statusInt;

        List<string> statusStrings = new();
        if (!statusInfo.IsAtEnd)
        {
            // statusString PKIFreeText OPTIONAL — SEQUENCE OF UTF8String
            if (statusInfo.PeekTag().TagNumber == (int)Asn1UniversalTag.Sequence)
            {
                Asn1Reader strs = statusInfo.ReadSequence();
                while (!strs.IsAtEnd)
                {
                    statusStrings.Add(strs.ReadUtf8String());
                }
            }
            // failInfo PKIFailureInfo OPTIONAL — BIT STRING (skip if present)
            if (!statusInfo.IsAtEnd)
            {
                statusInfo.Skip();
            }
        }

        TimeStampToken? token = null;
        if (!resp.IsAtEnd)
        {
            // The timeStampToken is itself a ContentInfo (CMS), so we read its
            // raw bytes and feed them to TimeStampToken.Decode.
            byte[] tokenDer = resp.ReadEncoded();
            token = TimeStampToken.Decode(tokenDer);
        }

        return new TimeStampResponse(status, statusStrings, token);
    }
}
