// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — X.509 path validation

using System;

namespace Chuvadi.Cryptography.PathValidation;

/// <summary>
/// The result of running the path-validation algorithm against one or more
/// candidate paths.
/// </summary>
public sealed class CertificatePathValidationResult
{
    /// <summary>Initialises a new result.</summary>
    public CertificatePathValidationResult(
        CertificatePathValidationStatus status,
        string message,
        CertificatePath? validatedPath)
    {
        ArgumentNullException.ThrowIfNull(message);
        Status = status;
        Message = message;
        ValidatedPath = validatedPath;
    }

    /// <summary>The validation outcome.</summary>
    public CertificatePathValidationStatus Status { get; }

    /// <summary>Human-readable explanation.</summary>
    public string Message { get; }

    /// <summary>The path that validated cleanly, when <see cref="IsValid"/> is true.</summary>
    public CertificatePath? ValidatedPath { get; }

    /// <summary>Convenience: true when <see cref="Status"/> is Valid.</summary>
    public bool IsValid => Status == CertificatePathValidationStatus.Valid;
}
