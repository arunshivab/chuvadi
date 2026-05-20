// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fuzz.Targets;

/// <summary>
/// Fuzz target for the PDF content stream parser.
/// </summary>
/// <remarks>
/// Feeds arbitrary bytes to <see cref="ContentStreamParser.Parse"/> with a stub
/// resolver and null resources. <see cref="ContentException"/> is the documented
/// exception; anything else is a crash.
/// </remarks>
internal sealed class ContentStreamTarget : IFuzzTarget
{
    private readonly NullResolver _resolver = new();

    public string Name => "content-stream";

    public IReadOnlyList<Type> ExpectedExceptionTypes { get; } = new[]
    {
        typeof(ContentException),
        // The content-stream tokenizer throws PdfTokenizerException directly on
        // malformed tokens (unterminated strings, bad hex escapes, etc.).
        typeof(PdfTokenizerException),
        // ContentStreamParser calls into PdfName.FromRawBytes (for Name tokens)
        // and PdfName.Intern (for the Tf font-name operand), both of which throw
        // ArgumentException when given an empty name. This is documented parser
        // rejection — the input was malformed — but the thrown exception type
        // is too broad. See FOLLOW-UPS.md for the PR 2.1 cleanup that tightens
        // the throw sites to PdfTokenizerException so this entry can be removed.
        typeof(ArgumentException),
        typeof(System.IO.EndOfStreamException),
        typeof(FormatException),
    };

    public void Run(byte[] input)
    {
        ContentStreamParser parser = new(_resolver, resources: null);
        _ = parser.Parse(input);
    }

    /// <summary>
    /// Trivial resolver that returns <see cref="PdfNull.Value"/> for any reference and
    /// reports no objects exist. The content-stream parser doesn't usually require
    /// resolution for the operator-level fuzzing we care about; if it tries to follow
    /// a reference, null is a safe answer.
    /// </summary>
    private sealed class NullResolver : IPdfObjectResolver
    {
        public PdfPrimitive Resolve(PdfPrimitive primitive)
        {
            return primitive is PdfReference ? PdfNull.Value : primitive;
        }

        public PdfPrimitive ResolveById(PdfObjectId id) => PdfNull.Value;

        public bool Contains(PdfObjectId id) => false;
    }
}
