// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for EcdsaVerifier
//
// Test vectors generated externally with Python's cryptography library
// (OpenSSL). One valid and one corrupted signature per supported NIST
// curve (P-256, P-384, P-521).

using System;
using System.Numerics;
using Chuvadi.Cryptography.PublicKey;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PublicKey;

public sealed class EcdsaVerifierTests
{
    [Fact]
    public void Verify_P256Valid()
    {
        byte[] pubPoint = Convert.FromHexString(
            "0471c0d90ae4eff0c4718248513bc4658da27c1dcc23c5544b5d90f25f88e9528d14d5be6f61f969" +
            "3b9460d97001bc02a200b1458bd9835c551364c3f00dcf412b");
        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P256, pubPoint);
        byte[] hash = Convert.FromHexString(
            "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592");
        byte[] sig = Convert.FromHexString(
            "3045022024b9cc085316c626e4c8a175e52f0ab9eba8fa3a87f7538533ac391ace7ea0c60221008e" +
            "582cdfbfee79617f014953d3d1180b241e53b54b48ab77df102fa96e69ce05");
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().Be(true);
    }

    [Fact]
    public void Verify_P256Corrupted()
    {
        byte[] pubPoint = Convert.FromHexString(
            "0471c0d90ae4eff0c4718248513bc4658da27c1dcc23c5544b5d90f25f88e9528d14d5be6f61f969" +
            "3b9460d97001bc02a200b1458bd9835c551364c3f00dcf412b");
        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P256, pubPoint);
        byte[] hash = Convert.FromHexString(
            "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592");
        byte[] sig = Convert.FromHexString(
            "3045022024b9cc085316c626e4c8a175e52f0ab9eba8fa3a87f7538533ac391ace7ea0c60221008e" +
            "582cdfbfee79617f014953d3d1180b241e53b54b48ab77df102fa96e69ce04");
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().Be(false);
    }

    [Fact]
    public void Verify_P384Valid()
    {
        byte[] pubPoint = Convert.FromHexString(
            "049d996505c7df12cf1872dcd8c560820ba1c885f31156fb2de05fdae9c2ec10d71da45003b045bf" +
            "f3acb23538412909249e160232d20d331f6532428f9b28ed6a9c4130a5a36ca95001c72799f728bd" +
            "90dd1b427cffe979eafa1c685c104a539e");
        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P384, pubPoint);
        byte[] hash = Convert.FromHexString(
            "ca737f1014a48f4c0b6dd43cb177b0afd9e5169367544c494011e3317dbf9a509cb1e5dc1e85a941" +
            "bbee3d7f2afbc9b1");
        byte[] sig = Convert.FromHexString(
            "3065023100cb6b4d13a4dcd9406489df3f795849e5f6903b4d66ad660afee023267c1333f1f1c42b" +
            "9403cc43c81ca2318341d147780230451a30bd69b77c2d2d42e489b71e1c68b3bdfa7f16555da45c" +
            "f46bf0bb432e216a59f266f338dfe9da1da183423d2500");
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().Be(true);
    }

    [Fact]
    public void Verify_P384Corrupted()
    {
        byte[] pubPoint = Convert.FromHexString(
            "049d996505c7df12cf1872dcd8c560820ba1c885f31156fb2de05fdae9c2ec10d71da45003b045bf" +
            "f3acb23538412909249e160232d20d331f6532428f9b28ed6a9c4130a5a36ca95001c72799f728bd" +
            "90dd1b427cffe979eafa1c685c104a539e");
        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P384, pubPoint);
        byte[] hash = Convert.FromHexString(
            "ca737f1014a48f4c0b6dd43cb177b0afd9e5169367544c494011e3317dbf9a509cb1e5dc1e85a941" +
            "bbee3d7f2afbc9b1");
        byte[] sig = Convert.FromHexString(
            "3065023100cb6b4d13a4dcd9406489df3f795849e5f6903b4d66ad660afee023267c1333f1f1c42b" +
            "9403cc43c81ca2318341d147780230451a30bd69b77c2d2d42e489b71e1c68b3bdfa7f16555da45c" +
            "f46bf0bb432e216a59f266f338dfe9da1da183423d2501");
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().Be(false);
    }

    [Fact]
    public void Verify_P521Valid()
    {
        byte[] pubPoint = Convert.FromHexString(
            "0400a1e12be9769eb9682cc262878b2a203d0a31e154766e1fc80ab1d5b8226bd2257be15cb19099" +
            "84fdf5284c7e95ca25910744d4442913e6fe2f39dcdc65d727f65f00194e968fd0c1331999a695e4" +
            "11c0fce481b4c23adf84fc50607ac784590ba8cbd3f7a29e73d4ebba63293cd60016aa3f6a2cc730" +
            "5cb102e31ea9568e108eae5333");
        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P521, pubPoint);
        byte[] hash = Convert.FromHexString(
            "07e547d9586f6a73f73fbac0435ed76951218fb7d0c8d788a309d785436bbb642e93a252a954f239" +
            "12547d1e8a3b5ed6e1bfd7097821233fa0538f3db854fee6");
        byte[] sig = Convert.FromHexString(
            "3081880242009baf20b7c6325f2241e9a076b5c201061bcc7075e997c11d6aea4465f13583fd7c7d" +
            "7c7ede98cadf4557d63dadeec70239f8b25fa33ef7105245f4302a204cf1b9024200be8e8812fc87" +
            "d5527dbfc81d1c6893ed607a63dc041261a23294fd58d6b7aba6b167a0fad0f5bde2879693500a36" +
            "972b5d43ba888f7afa597b44b35be3d9f4f011");
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().Be(true);
    }

    [Fact]
    public void Verify_P521Corrupted()
    {
        byte[] pubPoint = Convert.FromHexString(
            "0400a1e12be9769eb9682cc262878b2a203d0a31e154766e1fc80ab1d5b8226bd2257be15cb19099" +
            "84fdf5284c7e95ca25910744d4442913e6fe2f39dcdc65d727f65f00194e968fd0c1331999a695e4" +
            "11c0fce481b4c23adf84fc50607ac784590ba8cbd3f7a29e73d4ebba63293cd60016aa3f6a2cc730" +
            "5cb102e31ea9568e108eae5333");
        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P521, pubPoint);
        byte[] hash = Convert.FromHexString(
            "07e547d9586f6a73f73fbac0435ed76951218fb7d0c8d788a309d785436bbb642e93a252a954f239" +
            "12547d1e8a3b5ed6e1bfd7097821233fa0538f3db854fee6");
        byte[] sig = Convert.FromHexString(
            "3081880242009baf20b7c6325f2241e9a076b5c201061bcc7075e997c11d6aea4465f13583fd7c7d" +
            "7c7ede98cadf4557d63dadeec70239f8b25fa33ef7105245f4302a204cf1b9024200be8e8812fc87" +
            "d5527dbfc81d1c6893ed607a63dc041261a23294fd58d6b7aba6b167a0fad0f5bde2879693500a36" +
            "972b5d43ba888f7afa597b44b35be3d9f4f010");
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().Be(false);
    }

    [Fact]
    public void Verify_MalformedSignature_ReturnsFalse()
    {
        // Build a valid key on P-256, then pass garbage signature bytes
        byte[] pubPoint = new byte[65];
        pubPoint[0] = 0x04;
        // Use the curve generator as the key (it's a valid point)
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        byte[] gx = g.X.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] gy = g.Y.ToByteArray(isUnsigned: true, isBigEndian: true);
        Array.Copy(gx, 0, pubPoint, 1 + (32 - gx.Length), gx.Length);
        Array.Copy(gy, 0, pubPoint, 33 + (32 - gy.Length), gy.Length);

        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P256, pubPoint);
        byte[] hash = new byte[32];
        byte[] sig = new byte[] { 0x00, 0x01, 0x02 };  // not a valid ASN.1 sequence
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().BeFalse();
    }

    [Fact]
    public void Verify_SignatureWithRZero_ReturnsFalse()
    {
        // r = 0 is rejected per FIPS 186-4 step 1
        // Build SEQUENCE { INTEGER 0, INTEGER 1 }: 30 06 02 01 00 02 01 01
        byte[] pubPoint = new byte[65];
        pubPoint[0] = 0x04;
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        byte[] gx = g.X.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] gy = g.Y.ToByteArray(isUnsigned: true, isBigEndian: true);
        Array.Copy(gx, 0, pubPoint, 1 + (32 - gx.Length), gx.Length);
        Array.Copy(gy, 0, pubPoint, 33 + (32 - gy.Length), gy.Length);

        var key = EcdsaPublicKey.FromUncompressedPoint(EcCurve.P256, pubPoint);
        byte[] hash = new byte[32];
        byte[] sig = { 0x30, 0x06, 0x02, 0x01, 0x00, 0x02, 0x01, 0x01 };
        bool result = EcdsaVerifier.Verify(key, hash, sig);
        result.Should().BeFalse();
    }

    [Fact]
    public void EcCurve_FromOid_ReturnsCorrectCurve()
    {
        EcCurve.FromOid(Chuvadi.Cryptography.Oids.KnownOids.Secp256r1).Should().BeSameAs(EcCurve.P256);
        EcCurve.FromOid(Chuvadi.Cryptography.Oids.KnownOids.Secp384r1).Should().BeSameAs(EcCurve.P384);
        EcCurve.FromOid(Chuvadi.Cryptography.Oids.KnownOids.Secp521r1).Should().BeSameAs(EcCurve.P521);
    }

    [Fact]
    public void EcCurve_FromOid_UnknownCurveThrowsNotSupported()
    {
        Action act = () => EcCurve.FromOid(new Chuvadi.Cryptography.Asn1.ObjectIdentifier("1.2.3.4"));
        act.Should().Throw<NotSupportedException>();
    }
}
