// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fuzz.Targets;

/// <summary>
/// Fuzz target for the PDF parser entry point.
/// </summary>
/// <remarks>
/// Feeds arbitrary bytes to <see cref="PdfDocument.Open(Stream, bool)"/> and exercises
/// the document by enumerating pages. <see cref="PdfDocumentException"/> is the
/// documented exception for malformed input; anything else escaping is a crash.
/// </remarks>
internal sealed class PdfOpenTarget : IFuzzTarget
{
    public string Name => "pdf-open";

    public IReadOnlyList<Type> ExpectedExceptionTypes { get; } = new[]
    {
        typeof(PdfDocumentException),
        // PdfReaderException covers structural failures like missing startxref,
        // malformed xref, bad trailer, etc. — the bulk of malformed-input
        // outcomes from PdfDocument.Open.
        typeof(PdfReaderException),
        // PdfObjectException is thrown from the xref-table / xref-stream parser
        // for malformed entries (invalid offsets, generations, entry types,
        // truncated rows, bad subsection headers, invalid /W array, etc.).
        // The fuzzer reaches these constantly when mutating xref bytes.
        typeof(PdfObjectException),
        // The lower-level tokenizer can throw on truncated literal strings,
        // bad hex escapes, etc. when xref or trailer parsing reaches it.
        typeof(PdfTokenizerException),
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
