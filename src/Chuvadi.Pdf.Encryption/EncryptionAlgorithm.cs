// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption

namespace Chuvadi.Pdf.Encryption;

/// <summary>
/// Identifies which encryption algorithm a PDF uses.
/// </summary>
public enum EncryptionAlgorithm
{
    /// <summary>Document is not encrypted.</summary>
    None,

    /// <summary>RC4 with 40-bit key (V=1, R=2).</summary>
    Rc4_40,

    /// <summary>RC4 with 128-bit key (V=2 or V=4 with CFM=V2, R=3 or R=4).</summary>
    Rc4_128,

    /// <summary>AES with 128-bit key (V=4 with CFM=AESV2, R=4).</summary>
    Aes_128,

    /// <summary>AES with 256-bit key, ISO 32000-2 key derivation (V=5, R=6).</summary>
    Aes_256,
}
