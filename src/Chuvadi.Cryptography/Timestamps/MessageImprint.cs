// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3161 §2.4.2 — MessageImprint
// PHASE: Phase 1.1.4 — RFC 3161 timestamps

using System;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// The cryptographic commitment that a timestamp token covers.
/// </summary>
/// <remarks>
/// RFC 3161 §2.4.2:
/// <code>
/// MessageImprint ::= SEQUENCE {
///   hashAlgorithm   AlgorithmIdentifier,
///   hashedMessage   OCTET STRING
/// }
/// </code>
/// For a signature timestamp embedded in CMS unsigned attrs (the typical PDF
/// case), <see cref="HashedMessage"/> is the hash of the signer's signature
/// bytes — computing <see cref="HashAlgorithm"/> over the SignerInfo.signature
/// OCTET STRING content.
/// </remarks>
public sealed class MessageImprint
{
    /// <summary>Initialises a new MessageImprint.</summary>
    public MessageImprint(AlgorithmIdentifier hashAlgorithm, byte[] hashedMessage)
    {
        ArgumentNullException.ThrowIfNull(hashAlgorithm);
        ArgumentNullException.ThrowIfNull(hashedMessage);
        HashAlgorithm = hashAlgorithm;
        HashedMessage = hashedMessage;
    }

    /// <summary>The hash algorithm used to produce <see cref="HashedMessage"/>.</summary>
    public AlgorithmIdentifier HashAlgorithm { get; }

    /// <summary>The hash of the data the TSA was asked to timestamp.</summary>
    public byte[] HashedMessage { get; }
}
