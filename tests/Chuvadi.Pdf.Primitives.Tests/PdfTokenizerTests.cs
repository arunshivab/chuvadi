// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Primitives.Tests;

public sealed class PdfTokenTypeTests
{
    [Fact]
    public void AllExpectedValues_ArePresent()
    {
        // Spot-check the enum has the values we depend on throughout the parser.
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.Integer).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.Real).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.Name).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.DictionaryStart).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.DictionaryEnd).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.ArrayStart).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.ArrayEnd).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.StreamStart).Should().BeTrue();
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.EndOfStream).Should().BeTrue();
    }
}

public sealed class PdfTokenTests
{
    [Fact]
    public void EndOfStream_IsEndOfStream()
    {
        PdfToken.EndOfStream.IsEndOfStream.Should().BeTrue();
    }

    [Fact]
    public void IsNumeric_TrueForIntegerAndReal()
    {
        var i = new PdfToken(PdfTokenType.Integer, "42"u8.ToArray(), 0);
        var r = new PdfToken(PdfTokenType.Real, "3.14"u8.ToArray(), 0);
        var n = new PdfToken(PdfTokenType.Name, "Type"u8.ToArray(), 0);

        i.IsNumeric.Should().BeTrue();
        r.IsNumeric.Should().BeTrue();
        n.IsNumeric.Should().BeFalse();
    }

    [Fact]
    public void RawText_DecodesAsLatin1()
    {
        var token = new PdfToken(PdfTokenType.Keyword, "BT"u8.ToArray(), 0);
        token.RawText.Should().Be("BT");
    }

    [Fact]
    public void ToString_IncludesTypeAndOffset()
    {
        var token = new PdfToken(PdfTokenType.Integer, "42"u8.ToArray(), 100);
        token.ToString().Should().Contain("Integer");
        token.ToString().Should().Contain("100");
        token.ToString().Should().Contain("42");
    }

    [Fact]
    public void Equality_SameContent_AreEqual()
    {
        var a = new PdfToken(PdfTokenType.Integer, "42"u8.ToArray(), 0);
        var b = new PdfToken(PdfTokenType.Integer, "42"u8.ToArray(), 0);
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equality_DifferentType_AreNotEqual()
    {
        var a = new PdfToken(PdfTokenType.Integer, "42"u8.ToArray(), 0);
        var b = new PdfToken(PdfTokenType.Real, "42"u8.ToArray(), 0);
        a.Equals(b).Should().BeFalse();
    }
}

public sealed class PdfTokenizerTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static PdfTokenizer FromString(string pdf)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(pdf);
        return new PdfTokenizer(new MemoryStream(bytes));
    }

    // ── Construction ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullStream_Throws()
    {
        Action act = () => _ = new PdfTokenizer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NonReadableStream_Throws()
    {
        using var stream = new MemoryStream();
        // Wrap in a write-only stream substitute
        Action act = () => _ = new PdfTokenizer(new WriteOnlyStream());
        act.Should().Throw<ArgumentException>();
    }

    // ── Empty stream ──────────────────────────────────────────────────────

    [Fact]
    public void EmptyStream_ReturnsEndOfStream()
    {
        using var t = FromString("");
        PdfToken token = t.Read();
        token.IsEndOfStream.Should().BeTrue();
    }

    [Fact]
    public void WhitespaceOnly_ReturnsEndOfStream()
    {
        using var t = FromString("   \t\r\n  ");
        t.Read().IsEndOfStream.Should().BeTrue();
    }

    // ── Comments ──────────────────────────────────────────────────────────

    [Fact]
    public void Comment_IsSkipped()
    {
        using var t = FromString("% this is a comment\n42");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Integer);
        token.RawText.Should().Be("42");
    }

    [Fact]
    public void MultipleComments_AreSkipped()
    {
        using var t = FromString("% comment 1\n% comment 2\ntrue");
        t.Read().Type.Should().Be(PdfTokenType.True);
    }

    // ── Integers ──────────────────────────────────────────────────────────

    [Fact]
    public void Integer_Positive_IsTokenized()
    {
        using var t = FromString("42");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Integer);
        token.RawText.Should().Be("42");
    }

    [Fact]
    public void Integer_Negative_IsTokenized()
    {
        using var t = FromString("-7");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Integer);
        token.RawText.Should().Be("-7");
    }

    [Fact]
    public void Integer_Zero_IsTokenized()
    {
        using var t = FromString("0");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Integer);
        token.RawText.Should().Be("0");
    }

    [Fact]
    public void MultipleIntegers_AreTokenizedInSequence()
    {
        using var t = FromString("1 2 3");
        t.Read().RawText.Should().Be("1");
        t.Read().RawText.Should().Be("2");
        t.Read().RawText.Should().Be("3");
        t.Read().IsEndOfStream.Should().BeTrue();
    }

    // ── Reals ─────────────────────────────────────────────────────────────

    [Fact]
    public void Real_IsTokenized()
    {
        using var t = FromString("3.14");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Real);
        token.RawText.Should().Be("3.14");
    }

    [Fact]
    public void Real_LeadingDecimalPoint_IsTokenized()
    {
        using var t = FromString(".5");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Real);
        token.RawText.Should().Be(".5");
    }

    [Fact]
    public void Real_Negative_IsTokenized()
    {
        using var t = FromString("-1.5");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Real);
        token.RawText.Should().Be("-1.5");
    }

    // ── Names ─────────────────────────────────────────────────────────────

    [Fact]
    public void Name_IsTokenized()
    {
        using var t = FromString("/Type");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Name);
        // Raw bytes do NOT include the leading solidus.
        token.RawText.Should().Be("Type");
    }

    [Fact]
    public void Name_WithHashEscape_RawBytesPreserved()
    {
        using var t = FromString("/Hello#20World");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Name);
        // Raw bytes are pre-decode — PdfName.FromRawBytes handles decoding.
        token.RawText.Should().Be("Hello#20World");
    }

    [Fact]
    public void Name_EmptyAfterSolidus_ProducesEmptyToken()
    {
        // "/\n" — solidus followed immediately by whitespace = empty name.
        using var t = FromString("/ ");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.Name);
        token.RawBytes.Should().BeEmpty();
    }

    // ── Boolean and null keywords ─────────────────────────────────────────

    [Fact]
    public void True_IsTokenized()
    {
        using var t = FromString("true");
        t.Read().Type.Should().Be(PdfTokenType.True);
    }

    [Fact]
    public void False_IsTokenized()
    {
        using var t = FromString("false");
        t.Read().Type.Should().Be(PdfTokenType.False);
    }

    [Fact]
    public void Null_IsTokenized()
    {
        using var t = FromString("null");
        t.Read().Type.Should().Be(PdfTokenType.Null);
    }

    // ── PDF keywords ──────────────────────────────────────────────────────

    [Fact]
    public void Reference_R_IsTokenized()
    {
        using var t = FromString("12 0 R");
        t.Read(); // 12
        t.Read(); // 0
        PdfToken r = t.Read();
        r.Type.Should().Be(PdfTokenType.Reference);
    }

    [Fact]
    public void ObjectStart_IsTokenized()
    {
        using var t = FromString("obj");
        t.Read().Type.Should().Be(PdfTokenType.ObjectStart);
    }

    [Fact]
    public void ObjectEnd_IsTokenized()
    {
        using var t = FromString("endobj");
        t.Read().Type.Should().Be(PdfTokenType.ObjectEnd);
    }

    [Fact]
    public void StreamStart_IsTokenized()
    {
        using var t = FromString("stream");
        t.Read().Type.Should().Be(PdfTokenType.StreamStart);
    }

    [Fact]
    public void StreamEnd_IsTokenized()
    {
        using var t = FromString("endstream");
        t.Read().Type.Should().Be(PdfTokenType.StreamEnd);
    }

    [Fact]
    public void XRef_IsTokenized()
    {
        using var t = FromString("xref");
        t.Read().Type.Should().Be(PdfTokenType.XRef);
    }

    [Fact]
    public void Trailer_IsTokenized()
    {
        using var t = FromString("trailer");
        t.Read().Type.Should().Be(PdfTokenType.Trailer);
    }

    [Fact]
    public void StartXRef_IsTokenized()
    {
        using var t = FromString("startxref");
        t.Read().Type.Should().Be(PdfTokenType.StartXRef);
    }

    [Fact]
    public void EndOfFile_MarkerIsComment_ReturnsEndOfStream()
    {
        // PDF 32000-1:2008 §7.5.5 — %%EOF is a comment by lexical rules.
        // The % character begins a comment that runs to end of line.
        // The tokenizer correctly skips it and returns EndOfStream.
        // The reader layer locates %%EOF by backward byte scan, not inline tokenization.
        using var t = FromString("%%EOF");
        t.Read().IsEndOfStream.Should().BeTrue();
    }

    [Fact]
    public void EndOfFile_TokenType_ExistsInEnum()
    {
        // PdfTokenType.EndOfFile exists for completeness and is verified here.
        Enum.IsDefined(typeof(PdfTokenType), PdfTokenType.EndOfFile).Should().BeTrue();
    }

    [Fact]
    public void UnknownKeyword_IsTokenizedAsKeyword()
    {
        using var t = FromString("BT");
        t.Read().Type.Should().Be(PdfTokenType.Keyword);
    }

    // ── Delimiters ────────────────────────────────────────────────────────

    [Fact]
    public void DictionaryStart_IsTokenized()
    {
        using var t = FromString("<<");
        t.Read().Type.Should().Be(PdfTokenType.DictionaryStart);
    }

    [Fact]
    public void DictionaryEnd_IsTokenized()
    {
        using var t = FromString(">>");
        t.Read().Type.Should().Be(PdfTokenType.DictionaryEnd);
    }

    [Fact]
    public void ArrayStart_IsTokenized()
    {
        using var t = FromString("[");
        t.Read().Type.Should().Be(PdfTokenType.ArrayStart);
    }

    [Fact]
    public void ArrayEnd_IsTokenized()
    {
        using var t = FromString("]");
        t.Read().Type.Should().Be(PdfTokenType.ArrayEnd);
    }

    // ── Literal strings ───────────────────────────────────────────────────

    [Fact]
    public void LiteralString_Simple_IsTokenized()
    {
        using var t = FromString("(Hello)");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.LiteralString);
        token.RawText.Should().Be("(Hello)");
    }

    [Fact]
    public void LiteralString_WithEscapedParenthesis_IsTokenized()
    {
        using var t = FromString(@"(Hello \(World\))");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.LiteralString);
        token.RawText.Should().Contain("Hello");
    }

    [Fact]
    public void LiteralString_NestedParentheses_IsTokenized()
    {
        using var t = FromString("(Hello (World))");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.LiteralString);
        token.RawText.Should().Be("(Hello (World))");
    }

    [Fact]
    public void LiteralString_Empty_IsTokenized()
    {
        using var t = FromString("()");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.LiteralString);
        token.RawText.Should().Be("()");
    }

    [Fact]
    public void LiteralString_Unclosed_ThrowsTokenizerException()
    {
        using var t = FromString("(unclosed");
        Action act = () => t.Read();
        act.Should().Throw<PdfTokenizerException>();
    }

    // ── Hex strings ───────────────────────────────────────────────────────

    [Fact]
    public void HexString_IsTokenized()
    {
        using var t = FromString("<48656C6C6F>");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.HexString);
        token.RawText.Should().Be("<48656C6C6F>");
    }

    [Fact]
    public void HexString_Empty_IsTokenized()
    {
        using var t = FromString("<>");
        PdfToken token = t.Read();
        token.Type.Should().Be(PdfTokenType.HexString);
        token.RawText.Should().Be("<>");
    }

    [Fact]
    public void HexString_Unclosed_ThrowsTokenizerException()
    {
        using var t = FromString("<48656C");
        Action act = () => t.Read();
        act.Should().Throw<PdfTokenizerException>();
    }

    // ── Pushback ──────────────────────────────────────────────────────────

    [Fact]
    public void Unread_PushedTokenIsReturnedOnNextRead()
    {
        using var t = FromString("42 true");
        PdfToken first = t.Read();
        t.Unread(first);
        PdfToken again = t.Read();
        again.RawText.Should().Be("42");
    }

    [Fact]
    public void Unread_DoublePushback_Throws()
    {
        using var t = FromString("42 true");
        PdfToken first = t.Read();
        t.Unread(first);
        Action act = () => t.Unread(first);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Byte offset tracking ──────────────────────────────────────────────

    [Fact]
    public void ByteOffset_FirstToken_IsZero()
    {
        using var t = FromString("42");
        PdfToken token = t.Read();
        token.ByteOffset.Should().Be(0);
    }

    [Fact]
    public void ByteOffset_SecondToken_IsCorrect()
    {
        using var t = FromString("42 true");
        t.Read(); // "42" at offset 0
        PdfToken second = t.Read(); // "true" at offset 3
        second.ByteOffset.Should().Be(3);
    }

    // ── Seek ──────────────────────────────────────────────────────────────

    [Fact]
    public void Seek_ResetsToOffset()
    {
        using var t = FromString("42 true");
        t.Read(); // consume 42
        t.Seek(0);
        PdfToken token = t.Read();
        token.RawText.Should().Be("42");
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_ThenRead_ThrowsObjectDisposedException()
    {
        var t = FromString("42");
        t.Dispose();
        Action act = () => t.Read();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var t = FromString("42");
        t.Dispose();
        Action act = () => t.Dispose();
        act.Should().NotThrow();
    }

    // ── Real-world snippet ────────────────────────────────────────────────

    [Fact]
    public void RealWorldSnippet_IndirectObject_TokenizesCorrectly()
    {
        // A minimal indirect object definition.
        string pdf = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj";
        using var t = FromString(pdf);

        t.Read().Type.Should().Be(PdfTokenType.Integer);    // 1
        t.Read().Type.Should().Be(PdfTokenType.Integer);    // 0
        t.Read().Type.Should().Be(PdfTokenType.ObjectStart); // obj
        t.Read().Type.Should().Be(PdfTokenType.DictionaryStart); // <<
        t.Read().Type.Should().Be(PdfTokenType.Name);       // Type
        t.Read().Type.Should().Be(PdfTokenType.Name);       // Catalog
        t.Read().Type.Should().Be(PdfTokenType.Name);       // Pages
        t.Read().Type.Should().Be(PdfTokenType.Integer);    // 2
        t.Read().Type.Should().Be(PdfTokenType.Integer);    // 0
        t.Read().Type.Should().Be(PdfTokenType.Reference);  // R
        t.Read().Type.Should().Be(PdfTokenType.DictionaryEnd); // >>
        t.Read().Type.Should().Be(PdfTokenType.ObjectEnd);  // endobj
        t.Read().IsEndOfStream.Should().BeTrue();
    }

    [Fact]
    public void RealWorldSnippet_ArrayOfNumbers_TokenizesCorrectly()
    {
        // A typical MediaBox array.
        string pdf = "[0 0 595.28 841.89]";
        using var t = FromString(pdf);

        t.Read().Type.Should().Be(PdfTokenType.ArrayStart);
        t.Read().Type.Should().Be(PdfTokenType.Integer);
        t.Read().Type.Should().Be(PdfTokenType.Integer);
        t.Read().Type.Should().Be(PdfTokenType.Real);
        t.Read().Type.Should().Be(PdfTokenType.Real);
        t.Read().Type.Should().Be(PdfTokenType.ArrayEnd);
        t.Read().IsEndOfStream.Should().BeTrue();
    }
}

/// <summary>
/// A stream that is write-only, used to test the tokenizer's
/// constructor validation.
/// </summary>
internal sealed class WriteOnlyStream : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) { }
}
