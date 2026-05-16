// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for EcPoint

using System;
using System.Numerics;
using Chuvadi.Cryptography.PublicKey;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PublicKey;

public sealed class EcPointTests
{
    [Fact]
    public void Generator_IsOnCurve_AllCurves()
    {
        EcPoint.Generator(EcCurve.P256).IsInfinity.Should().BeFalse();
        EcPoint.Generator(EcCurve.P384).IsInfinity.Should().BeFalse();
        EcPoint.Generator(EcCurve.P521).IsInfinity.Should().BeFalse();
    }

    [Fact]
    public void Infinity_PlusAnything_IsAnything()
    {
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        EcPoint inf = EcPoint.Infinity(EcCurve.P256);
        EcPoint sum = inf.Add(g);
        sum.IsInfinity.Should().BeFalse();
        sum.X.Should().Be(g.X);
        sum.Y.Should().Be(g.Y);
    }

    [Fact]
    public void GeneratorTimesN_IsInfinity()
    {
        // Fundamental: n * G = identity, where n is the curve order
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        EcPoint result = g.Multiply(EcCurve.P256.N);
        result.IsInfinity.Should().BeTrue();
    }

    [Fact]
    public void GeneratorTimes2_EqualsDouble()
    {
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        EcPoint doubled = g.Double();
        EcPoint multiplied = g.Multiply(2);
        doubled.X.Should().Be(multiplied.X);
        doubled.Y.Should().Be(multiplied.Y);
    }

    [Fact]
    public void Point_PlusItsNegation_IsInfinity()
    {
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        EcPoint negG = g.Negate();
        EcPoint sum = g.Add(negG);
        sum.IsInfinity.Should().BeTrue();
    }

    [Fact]
    public void Multiply_ByZero_IsInfinity()
    {
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        EcPoint result = g.Multiply(BigInteger.Zero);
        result.IsInfinity.Should().BeTrue();
    }

    [Fact]
    public void Multiply_ByOne_IsSamePoint()
    {
        EcPoint g = EcPoint.Generator(EcCurve.P384);
        EcPoint result = g.Multiply(BigInteger.One);
        result.X.Should().Be(g.X);
        result.Y.Should().Be(g.Y);
    }

    [Fact]
    public void Multiply_Associative()
    {
        // (a + b) * G = (a * G) + (b * G)
        EcPoint g = EcPoint.Generator(EcCurve.P256);
        BigInteger a = 12345;
        BigInteger b = 67890;
        EcPoint sumScalars = g.Multiply(a + b);
        EcPoint sumPoints = g.Multiply(a).Add(g.Multiply(b));
        sumScalars.X.Should().Be(sumPoints.X);
        sumScalars.Y.Should().Be(sumPoints.Y);
    }

    [Fact]
    public void ModInverse_VerifiesInverseProperty()
    {
        BigInteger m = 17;  // prime
        BigInteger a = 5;
        BigInteger inv = EcPoint.ModInverse(a, m);
        ((a * inv) % m).Should().Be(BigInteger.One);
    }

    [Fact]
    public void Create_PointOffCurve_Throws()
    {
        Action act = () => EcPoint.Create(EcCurve.P256, 1, 1);
        act.Should().Throw<ArgumentException>();
    }
}
