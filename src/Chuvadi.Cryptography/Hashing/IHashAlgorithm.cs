// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Cryptographic primitives

using System;

namespace Chuvadi.Cryptography.Hashing;

/// <summary>
/// A streaming cryptographic hash function.
/// </summary>
/// <remarks>
/// Usage pattern: construct an instance, call <see cref="Update"/> zero or more
/// times to feed bytes, then call <see cref="Finish"/> once to obtain the digest.
/// After <see cref="Finish"/> the instance is consumed; further calls throw.
/// To hash a second message construct a new instance, or call <see cref="Reset"/>.
/// </remarks>
public interface IHashAlgorithm
{
    /// <summary>The algorithm's identity.</summary>
    HashAlgorithmName Name { get; }

    /// <summary>The size of the digest in bytes.</summary>
    int DigestSize { get; }

    /// <summary>The block size in bytes (used for HMAC keying).</summary>
    int BlockSize { get; }

    /// <summary>Feeds a chunk of input into the hash state.</summary>
    void Update(ReadOnlySpan<byte> data);

    /// <summary>
    /// Finalises the hash and writes the digest into <paramref name="destination"/>.
    /// The destination must be at least <see cref="DigestSize"/> bytes long.
    /// Returns the number of bytes written.
    /// </summary>
    int Finish(Span<byte> destination);

    /// <summary>Resets the hash state so a new message can be hashed.</summary>
    void Reset();
}
