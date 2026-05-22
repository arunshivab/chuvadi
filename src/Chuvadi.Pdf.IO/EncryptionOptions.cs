// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.5 (integration) — Chuvadi.Pdf.IO
//        v2.0.2 — corrected the "allow everything" default
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
    /// <summary>
    /// The /P permission mask that grants every action defined by PDF
    /// 32000-1:2008 §7.6.3.2, Table 22 (print, modify, copy, annotate,
    /// fill forms, accessibility extract, assemble, high-quality print).
    /// </summary>
    /// <remarks>
    /// Equal to <c>-4</c> (0xFFFFFFFC): all eight permission bits set,
    /// both reserved-must-be-1 bits set (bits 7 and 8), and all high
    /// reserved bits (13..32) set. Only bits 1 and 2 are clear because
    /// the spec reserves them to 0.
    /// </remarks>
    public const int AllPermissionsAllowed = -4;

    private EncryptionOptions(EncryptionAlgorithm algorithm, byte[] fileKey, string userPassword, string ownerPassword)
    {
        Algorithm = algorithm;
        FileKey = fileKey;
        UserPassword = userPassword;
        OwnerPassword = ownerPassword;
        Permissions = AllPermissionsAllowed;
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
    /// Gets or initialises the permission bit mask written to /P. Defaults to
    /// <see cref="AllPermissionsAllowed"/> (every action permitted). Set a
    /// narrower value to restrict what user-password holders may do.
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
