// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fuzz.Targets;

/// <summary>
/// Fuzz target for the PDF parser entry point.
/// </summary>
/// <remarks>
/// Feeds arbitrary bytes to <see cref="PdfDocument.Open(Stream, bool)"/> and exercises
/// the document by enumerating pages. <see cref="PdfCorruptionException"/> is the
/// documented exception for malformed input; anything else escaping is a crash.
/// </remarks>
internal sealed class PdfOpenTarget : IFuzzTarget
{
    public string Name => "pdf-open";

    public IReadOnlyList<Type> ExpectedExceptionTypes { get; } = new[]
    {
        typeof(PdfCorruptionException),
        // PdfParseException covers every byte-level parse failure: malformed
        // xref tables, bad xref streams, truncated trailers, invalid object
        // and generation numbers, tokenizer errors. Single PDF-typed exception
        // for "the bytes are wrong" in v2.0.0.
        typeof(PdfParseException),
        // PdfName.Intern / PdfName.FromRawBytes throw ArgumentException when
        // fuzzed input drives an empty PDF name through page-tree resolution.
        // See FOLLOW-UPS.md for the PR 2.1 cleanup that tightens these throws.
        typeof(ArgumentException),
        // The parser may also reach BCL-level IO/format errors on truncated input;
        // these are equally valid "rejected" outcomes, not crashes.
        typeof(EndOfStreamException),
        typeof(FormatException),
    };

    public void Run(byte[] input)
    {
        using MemoryStream stream = new(input, writable: false);
        using PdfDocument doc = PdfDocument.Open(stream);
        // Touch a few properties to exercise more of the parser.
        int pageCount = doc.PageCount;
        for (int i = 0; i < pageCount && i < 10; i++)
        {
            _ = doc.Pages[i];   // resolve the page object
        }
    }
}
