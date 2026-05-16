// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FIPS 186-4 Appendix D — Recommended Elliptic Curves
//        SEC 1 v2.0 §2.2 — Elliptic curve group laws
// PHASE: Phase 1.1.4 — Public-key cryptography
//
// Implementation strategy: affine coordinates with double-and-add scalar
// multiplication. Sufficient for verification (no secret data); a future
// performance pass could move to Jacobian coordinates and windowed-NAF.

using System;
using System.Numerics;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// A point on a short Weierstrass elliptic curve in affine coordinates.
/// </summary>
/// <remarks>
/// The point at infinity (group identity) is represented by
/// <see cref="IsInfinity"/> being true; the <see cref="X"/> and <see cref="Y"/>
/// values are irrelevant in that case.
/// </remarks>
public sealed class EcPoint
{
    /// <summary>The curve this point lives on.</summary>
    public EcCurve Curve { get; }

    /// <summary>The affine x coordinate (undefined when <see cref="IsInfinity"/>).</summary>
    public BigInteger X { get; }

    /// <summary>The affine y coordinate (undefined when <see cref="IsInfinity"/>).</summary>
    public BigInteger Y { get; }

    /// <summary>True when this point is the group identity (point at infinity).</summary>
    public bool IsInfinity { get; }

    private EcPoint(EcCurve curve, BigInteger x, BigInteger y, bool isInfinity)
    {
        Curve = curve;
        X = x;
        Y = y;
        IsInfinity = isInfinity;
    }

    /// <summary>Creates an affine point on <paramref name="curve"/>.</summary>
    /// <exception cref="ArgumentException">If (x, y) is not on the curve.</exception>
    public static EcPoint Create(EcCurve curve, BigInteger x, BigInteger y)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (!curve.IsOnCurve(x, y))
        {
            throw new ArgumentException($"Point ({x}, {y}) is not on curve {curve.Name}.");
        }
        return new EcPoint(curve, Mod(x, curve.P), Mod(y, curve.P), isInfinity: false);
    }

    /// <summary>The point at infinity on <paramref name="curve"/>.</summary>
    public static EcPoint Infinity(EcCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new EcPoint(curve, BigInteger.Zero, BigInteger.Zero, isInfinity: true);
    }

    /// <summary>The generator (base point) of <paramref name="curve"/>.</summary>
    public static EcPoint Generator(EcCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new EcPoint(curve, curve.Gx, curve.Gy, isInfinity: false);
    }

    // ── Group operations ─────────────────────────────────────────────────

    /// <summary>Returns this point's additive inverse (negation flips y).</summary>
    public EcPoint Negate()
    {
        if (IsInfinity) { return this; }
        return new EcPoint(Curve, X, Mod(-Y, Curve.P), isInfinity: false);
    }

    /// <summary>Returns <c>this + other</c>.</summary>
    public EcPoint Add(EcPoint other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!ReferenceEquals(Curve, other.Curve))
        {
            throw new ArgumentException("Points must lie on the same curve.", nameof(other));
        }

        // Identity cases
        if (IsInfinity) { return other; }
        if (other.IsInfinity) { return this; }

        // P + (-P) = O
        if (X == other.X)
        {
            BigInteger ySum = Mod(Y + other.Y, Curve.P);
            if (ySum.IsZero) { return Infinity(Curve); }
            // Otherwise this is point doubling (same point)
            return Double();
        }

        // Standard chord-and-tangent:
        // lambda = (y2 - y1) / (x2 - x1) mod p
        // x3 = lambda^2 - x1 - x2
        // y3 = lambda * (x1 - x3) - y1
        BigInteger dx = Mod(other.X - X, Curve.P);
        BigInteger dy = Mod(other.Y - Y, Curve.P);
        BigInteger lambda = Mod(dy * ModInverse(dx, Curve.P), Curve.P);

        BigInteger x3 = Mod((lambda * lambda) - X - other.X, Curve.P);
        BigInteger y3 = Mod((lambda * (X - x3)) - Y, Curve.P);

        return new EcPoint(Curve, x3, y3, isInfinity: false);
    }

    /// <summary>Returns <c>2 * this</c> (point doubling).</summary>
    public EcPoint Double()
    {
        if (IsInfinity) { return this; }
        if (Y.IsZero) { return Infinity(Curve); }

        // lambda = (3*x^2 + a) / (2*y) mod p
        // x3 = lambda^2 - 2*x
        // y3 = lambda * (x - x3) - y
        BigInteger three = new(3);
        BigInteger two = new(2);

        BigInteger numerator = Mod((three * X * X) + Curve.A, Curve.P);
        BigInteger denominator = Mod(two * Y, Curve.P);
        BigInteger lambda = Mod(numerator * ModInverse(denominator, Curve.P), Curve.P);

        BigInteger x3 = Mod((lambda * lambda) - (two * X), Curve.P);
        BigInteger y3 = Mod((lambda * (X - x3)) - Y, Curve.P);

        return new EcPoint(Curve, x3, y3, isInfinity: false);
    }

    /// <summary>
    /// Returns <c>k * this</c> via double-and-add. <paramref name="k"/> must be
    /// non-negative.
    /// </summary>
    public EcPoint Multiply(BigInteger k)
    {
        if (k.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), "Scalar must be non-negative.");
        }
        if (k.IsZero || IsInfinity)
        {
            return Infinity(Curve);
        }

        // Process bits from most significant to least
        EcPoint result = Infinity(Curve);
        EcPoint addend = this;

        BigInteger scalar = k;
        while (scalar.Sign > 0)
        {
            if ((scalar & BigInteger.One) == BigInteger.One)
            {
                result = result.Add(addend);
            }
            addend = addend.Double();
            scalar >>= 1;
        }

        return result;
    }

    // ── Modular arithmetic helpers ───────────────────────────────────────

    /// <summary>Returns <c>value mod m</c>, normalised to <c>[0, m)</c>.</summary>
    public static BigInteger Mod(BigInteger value, BigInteger m)
    {
        BigInteger r = value % m;
        if (r.Sign < 0) { r += m; }
        return r;
    }

    /// <summary>
    /// Returns the modular multiplicative inverse of <paramref name="a"/> mod
    /// <paramref name="m"/>, via the extended Euclidean algorithm.
    /// </summary>
    /// <exception cref="InvalidOperationException">If gcd(a, m) != 1.</exception>
    public static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        // Extended Euclidean: find (g, x) such that a*x + m*y = g.
        // If g != 1, no inverse exists.
        BigInteger old_r = Mod(a, m);
        BigInteger r = m;
        BigInteger old_s = BigInteger.One;
        BigInteger s = BigInteger.Zero;

        while (r.Sign > 0)
        {
            BigInteger quotient = old_r / r;
            BigInteger temp_r = old_r - (quotient * r);
            old_r = r;
            r = temp_r;
            BigInteger temp_s = old_s - (quotient * s);
            old_s = s;
            s = temp_s;
        }

        if (old_r != BigInteger.One)
        {
            throw new InvalidOperationException("Value has no modular inverse.");
        }
        return Mod(old_s, m);
    }
}
