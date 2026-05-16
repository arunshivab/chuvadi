// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for SHA-512 and SHA-384

using System;
using System.Text;
using Chuvadi.Cryptography.Hashing;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Hashing;

public sealed class Sha512Tests
{
    private static string ToHex(byte[] bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public void HashOf_EmptyString_MatchesNistVector()
    {
        byte[] digest = Sha512.HashDataSha512(Array.Empty<byte>());
        ToHex(digest).Should().Be(
            "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce" +
            "47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e");
    }

    [Fact]
    public void HashOf_Abc_MatchesNistVector()
    {
        // FIPS 180-4 Appendix C.1
        byte[] digest = Sha512.HashDataSha512(Encoding.ASCII.GetBytes("abc"));
        ToHex(digest).Should().Be(
            "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a" +
            "2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f");
    }

    [Fact]
    public void HashOf_TwoBlock_MatchesNistVector()
    {
        // FIPS 180-4 Appendix C.2: 112-byte input forcing two 128-byte blocks
        string input = "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmn" +
                       "hijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu";
        byte[] digest = Sha512.HashDataSha512(Encoding.ASCII.GetBytes(input));
        ToHex(digest).Should().Be(
            "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018" +
            "501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909");
    }

    [Fact]
    public void HashOf_QuickBrownFox_MatchesKnownVector()
    {
        byte[] digest = Sha512.HashDataSha512(
            Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog"));
        ToHex(digest).Should().Be(
            "07e547d9586f6a73f73fbac0435ed76951218fb7d0c8d788a309d785436bbb64" +
            "2e93a252a954f23912547d1e8a3b5ed6e1bfd7097821233fa0538f3db854fee6");
    }

    [Fact]
    public void Update_InMultipleChunks_MatchesOneShot()
    {
        byte[] data = new byte[2048];
        for (int i = 0; i < data.Length; i++) { data[i] = (byte)(i & 0xFF); }
        byte[] oneShot = Sha512.HashDataSha512(data);

        Sha512 sha = new();
        sha.Update(data.AsSpan(0, 100));
        sha.Update(data.AsSpan(100, 1000));
        sha.Update(data.AsSpan(1100, 948));
        byte[] streamed = new byte[64];
        sha.Finish(streamed);
        streamed.Should().Equal(oneShot);
    }

    [Fact]
    public void Update_AtBlockBoundary_MatchesOneShot()
    {
        byte[] data = new byte[512];
        for (int i = 0; i < data.Length; i++) { data[i] = (byte)i; }
        byte[] oneShot = Sha512.HashDataSha512(data);

        Sha512 sha = new();
        sha.Update(data.AsSpan(0, 128));   // exactly one block
        sha.Update(data.AsSpan(128, 128));
        sha.Update(data.AsSpan(256, 256));
        byte[] streamed = new byte[64];
        sha.Finish(streamed);
        streamed.Should().Equal(oneShot);
    }

    [Fact]
    public void Reset_AllowsReuse()
    {
        Sha512 sha = new();
        sha.Update(Encoding.ASCII.GetBytes("first"));
        byte[] firstDigest = new byte[64];
        sha.Finish(firstDigest);

        sha.Reset();
        sha.Update(Encoding.ASCII.GetBytes("second"));
        byte[] secondDigest = new byte[64];
        sha.Finish(secondDigest);

        firstDigest.Should().Equal(Sha512.HashDataSha512(Encoding.ASCII.GetBytes("first")));
        secondDigest.Should().Equal(Sha512.HashDataSha512(Encoding.ASCII.GetBytes("second")));
    }

    [Fact]
    public void DigestSize_AndBlockSize_AreCorrect()
    {
        Sha512 sha = new();
        sha.DigestSize.Should().Be(64);
        sha.BlockSize.Should().Be(128);
        sha.Name.Should().Be(HashAlgorithmName.Sha512);
    }

    [Fact]
    public void HashOf_111ByteInput_BoundaryCase()
    {
        // 111 bytes — one byte short of needing a second block for 0x80 + length
        byte[] data = new byte[111];
        for (int i = 0; i < 111; i++) { data[i] = (byte)i; }
        byte[] digest = Sha512.HashDataSha512(data);
        digest.Length.Should().Be(64);
    }
}

public sealed class Sha384Tests
{
    private static string ToHex(byte[] bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();

    [Fact]
    public void HashOf_EmptyString_MatchesNistVector()
    {
        byte[] digest = Sha512.HashDataSha384(Array.Empty<byte>());
        ToHex(digest).Should().Be(
            "38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da" +
            "274edebfe76f65fbd51ad2f14898b95b");
    }

    [Fact]
    public void HashOf_Abc_MatchesNistVector()
    {
        // FIPS 180-4 Appendix D.1
        byte[] digest = Sha512.HashDataSha384(Encoding.ASCII.GetBytes("abc"));
        ToHex(digest).Should().Be(
            "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed" +
            "8086072ba1e7cc2358baeca134c825a7");
    }

    [Fact]
    public void HashOf_QuickBrownFox_MatchesKnownVector()
    {
        byte[] digest = Sha512.HashDataSha384(
            Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog"));
        ToHex(digest).Should().Be(
            "ca737f1014a48f4c0b6dd43cb177b0afd9e5169367544c494011e3317dbf9a50" +
            "9cb1e5dc1e85a941bbee3d7f2afbc9b1");
    }

    [Fact]
    public void HashOf_MillionA_MatchesNistVector()
    {
        Sha512 sha = new(HashAlgorithmName.Sha384);
        byte[] block = new byte[1000];
        Array.Fill(block, (byte)'a');
        for (int i = 0; i < 1000; i++) { sha.Update(block); }
        byte[] digest = new byte[48];
        sha.Finish(digest);
        ToHex(digest).Should().Be(
            "9d0e1809716474cb086e834e310a4a1ced149e9c00f248527972cec5704c2a5b" +
            "07b8b3dc38ecc4ebae97ddd87f3d8985");
    }

    [Fact]
    public void DigestSize_Is48Bytes()
    {
        Sha512 sha = new(HashAlgorithmName.Sha384);
        sha.DigestSize.Should().Be(48);
        sha.BlockSize.Should().Be(128);
        sha.Name.Should().Be(HashAlgorithmName.Sha384);
    }

    [Fact]
    public void Constructor_WithSha256Name_Throws()
    {
        Action act = () => new Sha512(HashAlgorithmName.Sha256);
        act.Should().Throw<ArgumentException>();
    }
}
