// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for SHA-256
//
// Test vectors sourced from NIST FIPS 180-4 Appendix B and the NIST CAVS
// (Cryptographic Algorithm Validation System) examples published by NIST.
// https://csrc.nist.gov/projects/cryptographic-algorithm-validation-program

using System;
using System.Text;
using Chuvadi.Cryptography.Hashing;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Hashing;

public sealed class Sha256Tests
{
    private static string ToHex(byte[] bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public void HashOf_EmptyString_MatchesNistVector()
    {
        // FIPS 180-4 Appendix B: SHA-256("")
        byte[] digest = Sha256.HashData(Array.Empty<byte>());
        ToHex(digest).Should().Be(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void HashOf_AbcOneBlock_MatchesNistVector()
    {
        // FIPS 180-4 Appendix B.1: SHA-256("abc")
        byte[] digest = Sha256.HashData(Encoding.ASCII.GetBytes("abc"));
        ToHex(digest).Should().Be(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void HashOf_TwoBlocks_MatchesNistVector()
    {
        // FIPS 180-4 Appendix B.2: SHA-256 of the 56-byte string that triggers two blocks
        string input = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";
        byte[] digest = Sha256.HashData(Encoding.ASCII.GetBytes(input));
        ToHex(digest).Should().Be(
            "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1");
    }

    [Fact]
    public void HashOf_MillionA_MatchesNistVector()
    {
        // FIPS 180-4 Appendix B.3: SHA-256 of one million 'a' characters
        Sha256 sha = new();
        byte[] block = new byte[1000];
        Array.Fill(block, (byte)'a');
        for (int i = 0; i < 1000; i++)
        {
            sha.Update(block);
        }
        byte[] digest = new byte[32];
        sha.Finish(digest);
        ToHex(digest).Should().Be(
            "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0");
    }

    [Fact]
    public void Update_InMultipleChunks_MatchesOneShot()
    {
        byte[] data = new byte[1024];
        for (int i = 0; i < data.Length; i++) { data[i] = (byte)(i & 0xFF); }

        byte[] oneShot = Sha256.HashData(data);

        Sha256 sha = new();
        sha.Update(data.AsSpan(0, 100));
        sha.Update(data.AsSpan(100, 400));
        sha.Update(data.AsSpan(500, 524));
        byte[] streamed = new byte[32];
        sha.Finish(streamed);

        streamed.Should().Equal(oneShot);
    }

    [Fact]
    public void Update_AtBlockBoundary_MatchesOneShot()
    {
        byte[] data = new byte[256];
        for (int i = 0; i < data.Length; i++) { data[i] = (byte)i; }

        byte[] oneShot = Sha256.HashData(data);

        Sha256 sha = new();
        sha.Update(data.AsSpan(0, 64));   // exactly one block
        sha.Update(data.AsSpan(64, 64));  // another block
        sha.Update(data.AsSpan(128, 128));
        byte[] streamed = new byte[32];
        sha.Finish(streamed);

        streamed.Should().Equal(oneShot);
    }

    [Fact]
    public void Update_OneByteAtATime_MatchesOneShot()
    {
        byte[] data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog");

        Sha256 sha = new();
        for (int i = 0; i < data.Length; i++)
        {
            sha.Update(data.AsSpan(i, 1));
        }
        byte[] streamed = new byte[32];
        sha.Finish(streamed);

        streamed.Should().Equal(Sha256.HashData(data));
        ToHex(streamed).Should().Be(
            "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592");
    }

    [Fact]
    public void Finish_AlreadyFinalised_Throws()
    {
        Sha256 sha = new();
        sha.Update(Encoding.ASCII.GetBytes("hello"));
        byte[] first = new byte[32];
        sha.Finish(first);

        Action act = () => sha.Finish(new byte[32]);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Update_AfterFinish_Throws()
    {
        Sha256 sha = new();
        sha.Finish(new byte[32]);
        Action act = () => sha.Update(new byte[] { 0x01 });
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reset_AllowsReuse()
    {
        Sha256 sha = new();
        sha.Update(Encoding.ASCII.GetBytes("first"));
        byte[] firstDigest = new byte[32];
        sha.Finish(firstDigest);

        sha.Reset();
        sha.Update(Encoding.ASCII.GetBytes("second"));
        byte[] secondDigest = new byte[32];
        sha.Finish(secondDigest);

        // Both should match independent one-shot hashes
        firstDigest.Should().Equal(Sha256.HashData(Encoding.ASCII.GetBytes("first")));
        secondDigest.Should().Equal(Sha256.HashData(Encoding.ASCII.GetBytes("second")));
    }

    [Fact]
    public void DigestSize_AndBlockSize_AreCorrect()
    {
        Sha256 sha = new();
        sha.DigestSize.Should().Be(32);
        sha.BlockSize.Should().Be(64);
        sha.Name.Should().Be(HashAlgorithmName.Sha256);
    }

    [Fact]
    public void Finish_DestinationTooShort_Throws()
    {
        Sha256 sha = new();
        Action act = () => sha.Finish(new byte[31]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HashOf_55ByteInput_BoundaryCase()
    {
        // 55 bytes — one byte short of needing a second block for the 0x80 + length.
        byte[] data = new byte[55];
        for (int i = 0; i < 55; i++) { data[i] = (byte)i; }
        byte[] d1 = Sha256.HashData(data);

        // Should not equal a 56-byte hash (sanity)
        byte[] data56 = new byte[56];
        Array.Copy(data, data56, 55);
        byte[] d2 = Sha256.HashData(data56);
        d1.Should().NotEqual(d2);
    }

    [Fact]
    public void HashOf_64ByteInput_ExactBlockBoundary()
    {
        // Exactly one block of input — padding starts a fresh block.
        byte[] data = new byte[64];
        for (int i = 0; i < 64; i++) { data[i] = 0x61; }  // 64 'a' chars
        byte[] digest = Sha256.HashData(data);
        // Known good (computed via SHA-256 of "a"*64)
        ToHex(digest).Should().Be(
            "ffe054fe7ae0cb6dc65c3af9b61d5209f439851db43d0ba5997337df154668eb");
    }
}
