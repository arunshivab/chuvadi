// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Identifies the type of a token produced by <see cref="PdfTokenizer"/>.
/// </summary>
/// <remarks>
/// The PDF specification defines a small set of token categories.
/// The tokenizer classifies every byte sequence it reads into one of these.
/// Higher-level parsers combine tokens into <see cref="PdfPrimitive"/> objects.
/// PDF 32000-1:2008 §7.2 — Lexical conventions.
/// </remarks>
public enum PdfTokenType
{
    /// <summary>
    /// A signed integer literal, e.g. <c>42</c> or <c>-7</c>.
    /// PDF 32000-1:2008 §7.3.3.
    /// </summary>
    Integer,

    /// <summary>
    /// A real number literal containing a decimal point, e.g. <c>3.14</c> or <c>-.5</c>.
    /// PDF 32000-1:2008 §7.3.3.
    /// </summary>
    Real,

    /// <summary>
    /// A name token beginning with a solidus, e.g. <c>/FlateDecode</c>.
    /// The raw bytes do not include the leading solidus.
    /// PDF 32000-1:2008 §7.3.5.
    /// </summary>
    Name,

    /// <summary>
    /// A literal string enclosed in parentheses, e.g. <c>(Hello)</c>.
    /// The raw bytes include the surrounding parentheses.
    /// PDF 32000-1:2008 §7.3.4.2.
    /// </summary>
    LiteralString,

    /// <summary>
    /// A hexadecimal string enclosed in angle brackets, e.g. <c>&lt;48656C6C6F&gt;</c>.
    /// The raw bytes include the surrounding angle brackets.
    /// PDF 32000-1:2008 §7.3.4.3.
    /// </summary>
    HexString,

    /// <summary>
    /// The start of a dictionary: <c>&lt;&lt;</c>.
    /// PDF 32000-1:2008 §7.3.7.
    /// </summary>
    DictionaryStart,

    /// <summary>
    /// The end of a dictionary: <c>&gt;&gt;</c>.
    /// PDF 32000-1:2008 §7.3.7.
    /// </summary>
    DictionaryEnd,

    /// <summary>
    /// The start of an array: <c>[</c>.
    /// PDF 32000-1:2008 §7.3.6.
    /// </summary>
    ArrayStart,

    /// <summary>
    /// The end of an array: <c>]</c>.
    /// PDF 32000-1:2008 §7.3.6.
    /// </summary>
    ArrayEnd,

    /// <summary>
    /// The PDF keyword <c>true</c>.
    /// PDF 32000-1:2008 §7.3.2.
    /// </summary>
    True,

    /// <summary>
    /// The PDF keyword <c>false</c>.
    /// PDF 32000-1:2008 §7.3.2.
    /// </summary>
    False,

    /// <summary>
    /// The PDF keyword <c>null</c>.
    /// PDF 32000-1:2008 §7.3.9.
    /// </summary>
    Null,

    /// <summary>
    /// An indirect object reference suffix: the keyword <c>R</c> following
    /// two integers, e.g. the <c>R</c> in <c>12 0 R</c>.
    /// PDF 32000-1:2008 §7.3.10.
    /// </summary>
    Reference,

    /// <summary>
    /// The keyword <c>obj</c> — begins an indirect object definition.
    /// PDF 32000-1:2008 §7.3.10.
    /// </summary>
    ObjectStart,

    /// <summary>
    /// The keyword <c>endobj</c> — ends an indirect object definition.
    /// PDF 32000-1:2008 §7.3.10.
    /// </summary>
    ObjectEnd,

    /// <summary>
    /// The keyword <c>stream</c> — begins a stream body.
    /// The stream bytes follow immediately after the mandatory line ending.
    /// PDF 32000-1:2008 §7.3.8.1.
    /// </summary>
    StreamStart,

    /// <summary>
    /// The keyword <c>endstream</c> — ends a stream body.
    /// PDF 32000-1:2008 §7.3.8.1.
    /// </summary>
    StreamEnd,

    /// <summary>
    /// The keyword <c>xref</c> — begins a cross-reference table.
    /// PDF 32000-1:2008 §7.5.4.
    /// </summary>
    XRef,

    /// <summary>
    /// The keyword <c>trailer</c> — begins the trailer dictionary.
    /// PDF 32000-1:2008 §7.5.5.
    /// </summary>
    Trailer,

    /// <summary>
    /// The keyword <c>startxref</c> — precedes the byte offset of the xref table.
    /// PDF 32000-1:2008 §7.5.5.
    /// </summary>
    StartXRef,

    /// <summary>
    /// An unrecognised keyword or bare word, e.g. operator names in content streams
    /// such as <c>BT</c>, <c>ET</c>, <c>Tf</c>, <c>Tj</c>.
    /// The raw bytes contain the exact keyword bytes.
    /// </summary>
    Keyword,

    /// <summary>
    /// The PDF end-of-file marker <c>%%EOF</c>.
    /// PDF 32000-1:2008 §7.5.5.
    /// </summary>
    EndOfFile,

    /// <summary>
    /// The tokenizer has reached the end of the underlying stream and
    /// there are no more tokens to read.
    /// </summary>
    EndOfStream,
}
