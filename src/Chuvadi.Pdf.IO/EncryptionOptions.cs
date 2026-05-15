// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.5 (integration) — Chuvadi.Pdf.IO
//
// Configuration for writing encrypted PDFs.

using System;
using Chuvadi.Pdf.Encryption;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Options that drive encrypted PDF writing.
/// </summary>
/// <remarks>
/// Only AES-128 and AES-256 are supported for writing. Legacy RC4 is read-only.
/// Construct an instance for the chosen algorithm via the static factories.
/// </remarks>
public sealed class EncryptionOptions
{
    private EncryptionOptions(EncryptionAlgorithm algorithm, byte[] fileKey, string userPassword, string ownerPassword)
    {
        Algorithm = algorithm;
        FileKey = fileKey;
        UserPassword = userPassword;
        OwnerPassword = ownerPassword;
        Permissions = -3904;  // default: allow everything (PDF spec all-bits-on)
        EncryptMetadata = true;
    }

    /// <summary>Gets the chosen encryption algorithm.</summary>
    public EncryptionAlgorithm Algorithm { get; }

    /// <summary>Gets the file encryption key. Random unless overridden.</summary>
    public byte[] FileKey { get; }

    /// <summary>Gets the user password used to derive the U/UE entries.</summary>
    public string UserPassword { get; }

    /// <summary>Gets the owner password used to derive the O/OE entries.</summary>
    public string OwnerPassword { get; }

    /// <summary>
    /// Gets or initialises the permission bit mask written to /P. Default: all
    /// permissions allowed.
    /// </summary>
    public int Permissions { get; init; }

    /// <summary>
    /// Gets or initialises whether /Metadata streams should be encrypted.
    /// Default: true. Setting false matches /EncryptMetadata=false in the spec.
    /// </summary>
    public bool EncryptMetadata { get; init; }

    /// <summary>
    /// Creates options for AES-128 encryption (V=4, R=4, AESV2 crypt filter).
    /// Generates a random 16-byte file key.
    /// </summary>
    public static EncryptionOptions Aes128(string userPassword, string? ownerPassword = null)
    {
        ArgumentNullException.ThrowIfNull(userPassword);
        return new EncryptionOptions(
            EncryptionAlgorithm.Aes_128,
            Encryptor.GenerateFileKeyAes128(),
            userPassword,
            ownerPassword ?? userPassword);
    }

    /// <summary>
    /// Creates options for AES-256 encryption (V=5, R=6, ISO 32000-2 standardised).
    /// Generates a random 32-byte file key.
    /// </summary>
    public static EncryptionOptions Aes256(string userPassword, string? ownerPassword = null)
    {
        ArgumentNullException.ThrowIfNull(userPassword);
        return new EncryptionOptions(
            EncryptionAlgorithm.Aes_256,
            Encryptor.GenerateFileKeyAes256(),
            userPassword,
            ownerPassword ?? userPassword);
    }
}
