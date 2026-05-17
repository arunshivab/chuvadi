// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.2.1 — ResponderID
// PHASE: Phase 1.1.4 — OCSP

using System;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>
/// Identifies the responder that signed an OCSP response.
/// </summary>
/// <remarks>
/// RFC 6960 §4.2.1:
/// <code>
/// ResponderID ::= CHOICE {
///   byName  [1] EXPLICIT Name,
///   byKey   [2] EXPLICIT KeyHash  -- SHA-1 hash of responder's pubkey BIT STRING content
/// }
/// </code>
/// </remarks>
public sealed class ResponderID
{
    private ResponderID(X509Name? byName, byte[]? byKey)
    {
        ByName = byName;
        ByKey = byKey;
    }

    /// <summary>The responder's distinguished name, when the responder identified itself that way.</summary>
    public X509Name? ByName { get; }

    /// <summary>The SHA-1 hash of the responder's public key, when the responder identified itself by key hash.</summary>
    public byte[]? ByKey { get; }

    /// <summary>True iff this responder ID is the <c>byName</c> variant.</summary>
    public bool IsByName => ByName is not null;

    /// <summary>True iff this responder ID is the <c>byKey</c> variant.</summary>
    public bool IsByKey => ByKey is not null;

    /// <summary>Factory: responder identified by name.</summary>
    public static ResponderID FromName(X509Name name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new ResponderID(name, null);
    }

    /// <summary>Factory: responder identified by SHA-1 key hash.</summary>
    public static ResponderID FromKeyHash(byte[] keyHash)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        return new ResponderID(null, keyHash);
    }
}
