// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
// PHASE: Phase 1 — Chuvadi.Pdf.Operations
// Exception raised when a page operation cannot be completed.

using System;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Thrown when a PDF page operation (merge, split, delete, rotate, reorder)
/// cannot be completed due to an invalid argument or document structure.
/// </summary>
public sealed class OperationsException : Exception
{
    /// <summary>Initialises a new <see cref="OperationsException"/> with no message.</summary>
    public OperationsException()
        : base("A PDF operations error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="OperationsException"/> with a message.</summary>
    public OperationsException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="OperationsException"/> with a message
    /// and an inner exception.
    /// </summary>
    public OperationsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
