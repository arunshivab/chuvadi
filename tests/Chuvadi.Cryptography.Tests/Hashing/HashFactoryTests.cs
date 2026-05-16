// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for HashFactory

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Hashing;

public sealed class HashFactoryTests
{
    [Theory]
    [InlineData(HashAlgorithmName.Sha256, 32, 64)]
    [InlineData(HashAlgorithmName.Sha384, 48, 128)]
    [InlineData(HashAlgorithmName.Sha512, 64, 128)]
    public void Create_ReturnsCorrectAlgorithm(HashAlgorithmName name, int digestSize, int blockSize)
    {
        IHashAlgorithm hash = HashFactory.Create(name);
        hash.Name.Should().Be(name);
        hash.DigestSize.Should().Be(digestSize);
        hash.BlockSize.Should().Be(blockSize);
    }

    [Fact]
    public void CreateFromOid_Sha256_ReturnsSha256()
    {
        IHashAlgorithm hash = HashFactory.CreateFromOid(KnownOids.Sha256);
        hash.Name.Should().Be(HashAlgorithmName.Sha256);
    }

    [Fact]
    public void CreateFromOid_Sha384_ReturnsSha384()
    {
        IHashAlgorithm hash = HashFactory.CreateFromOid(KnownOids.Sha384);
        hash.Name.Should().Be(HashAlgorithmName.Sha384);
    }

    [Fact]
    public void CreateFromOid_Sha512_ReturnsSha512()
    {
        IHashAlgorithm hash = HashFactory.CreateFromOid(KnownOids.Sha512);
        hash.Name.Should().Be(HashAlgorithmName.Sha512);
    }

    [Fact]
    public void CreateFromOid_Sha1_ThrowsNotSupported()
    {
        Action act = () => HashFactory.CreateFromOid(KnownOids.Sha1);
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*SHA-1*");
    }

    [Fact]
    public void CreateFromOid_Sha224_ThrowsNotSupported()
    {
        Action act = () => HashFactory.CreateFromOid(KnownOids.Sha224);
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*SHA-224*");
    }

    [Fact]
    public void CreateFromOid_Sha3_ThrowsNotSupported()
    {
        Action act = () => HashFactory.CreateFromOid(KnownOids.Sha3_256);
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*SHA-3*");
    }

    [Fact]
    public void CreateFromOid_UnknownOid_ThrowsArgument()
    {
        ObjectIdentifier unknown = new("1.2.3.4.5.6.7.8.9");
        Action act = () => HashFactory.CreateFromOid(unknown);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*does not name a recognised hash*");
    }

    [Fact]
    public void IsSupportedHash_TrueForSupportedAlgorithms()
    {
        HashFactory.IsSupportedHash(KnownOids.Sha256).Should().BeTrue();
        HashFactory.IsSupportedHash(KnownOids.Sha384).Should().BeTrue();
        HashFactory.IsSupportedHash(KnownOids.Sha512).Should().BeTrue();
    }

    [Fact]
    public void IsSupportedHash_FalseForUnsupported()
    {
        HashFactory.IsSupportedHash(KnownOids.Sha1).Should().BeFalse();
        HashFactory.IsSupportedHash(KnownOids.Sha224).Should().BeFalse();
        HashFactory.IsSupportedHash(new ObjectIdentifier("1.2.3.4")).Should().BeFalse();
    }
}
