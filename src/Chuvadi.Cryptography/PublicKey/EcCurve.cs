// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FIPS 186-4 Appendix D — Recommended Elliptic Curves
//        RFC 5480 §2.1.1.1 — Named Curves
// PHASE: Phase 1.1.4 — Public-key cryptography

using System;
using System.Globalization;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// A named elliptic curve over a prime field — the parameters needed to perform
/// ECDSA verification.
/// </summary>
/// <remarks>
/// All Chuvadi-supported curves use the short Weierstrass equation
/// <c>y² = x³ + a·x + b (mod p)</c> with <c>a = -3 (mod p)</c> per NIST
/// recommendations.
/// </remarks>
public sealed class EcCurve
{
    private EcCurve(string name, ObjectIdentifier oid, BigInteger p, BigInteger a, BigInteger b,
        BigInteger gx, BigInteger gy, BigInteger n, int fieldSizeBytes)
    {
        Name = name;
        Oid = oid;
        P = p;
        A = a;
        B = b;
        Gx = gx;
        Gy = gy;
        N = n;
        FieldSizeBytes = fieldSizeBytes;
    }

    /// <summary>Friendly name (e.g. "P-256").</summary>
    public string Name { get; }

    /// <summary>The OID that identifies this curve in SubjectPublicKeyInfo parameters.</summary>
    public ObjectIdentifier Oid { get; }

    /// <summary>Prime field modulus.</summary>
    public BigInteger P { get; }

    /// <summary>Curve coefficient a (= -3 mod p for NIST curves).</summary>
    public BigInteger A { get; }

    /// <summary>Curve coefficient b.</summary>
    public BigInteger B { get; }

    /// <summary>Base point x coordinate.</summary>
    public BigInteger Gx { get; }

    /// <summary>Base point y coordinate.</summary>
    public BigInteger Gy { get; }

    /// <summary>Order of the base point.</summary>
    public BigInteger N { get; }

    /// <summary>Field size in bytes (ceiling of bit length divided by 8).</summary>
    public int FieldSizeBytes { get; }

    /// <summary>
    /// True when <paramref name="x"/> and <paramref name="y"/> satisfy the curve equation.
    /// </summary>
    public bool IsOnCurve(BigInteger x, BigInteger y)
    {
        BigInteger lhs = BigInteger.ModPow(y, 2, P);
        BigInteger rhs = (BigInteger.ModPow(x, 3, P) + (A * x) + B) % P;
        if (rhs.Sign < 0) { rhs += P; }
        return lhs == rhs;
    }

    // ── Named curves (FIPS 186-4 Appendix D, NIST SP 800-186) ────────────

    /// <summary>NIST P-256 / secp256r1 / prime256v1.</summary>
    public static readonly EcCurve P256 = new(
        name: "P-256",
        oid: KnownOids.Secp256r1,
        p: ParseHex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF"),
        a: ParseHex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFC"),
        b: ParseHex("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B"),
        gx: ParseHex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"),
        gy: ParseHex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"),
        n: ParseHex("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"),
        fieldSizeBytes: 32);

    /// <summary>NIST P-384 / secp384r1.</summary>
    public static readonly EcCurve P384 = new(
        name: "P-384",
        oid: KnownOids.Secp384r1,
        p: ParseHex("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFFFF0000000000000000FFFFFFFF"),
        a: ParseHex("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFFFF0000000000000000FFFFFFFC"),
        b: ParseHex("B3312FA7E23EE7E4988E056BE3F82D19181D9C6EFE8141120314088F5013875AC656398D8A2ED19D2A85C8EDD3EC2AEF"),
        gx: ParseHex("AA87CA22BE8B05378EB1C71EF320AD746E1D3B628BA79B9859F741E082542A385502F25DBF55296C3A545E3872760AB7"),
        gy: ParseHex("3617DE4A96262C6F5D9E98BF9292DC29F8F41DBD289A147CE9DA3113B5F0B8C00A60B1CE1D7E819D7A431D7C90EA0E5F"),
        n: ParseHex("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFC7634D81F4372DDF581A0DB248B0A77AECEC196ACCC52973"),
        fieldSizeBytes: 48);

    /// <summary>NIST P-521 / secp521r1.</summary>
    public static readonly EcCurve P521 = new(
        name: "P-521",
        oid: KnownOids.Secp521r1,
        p: ParseHex("01FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
        a: ParseHex("01FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFC"),
        b: ParseHex("0051953EB9618E1C9A1F929A21A0B68540EEA2DA725B99B315F3B8B489918EF109E156193951EC7E937B1652C0BD3BB1BF073573DF883D2C34F1EF451FD46B503F00"),
        gx: ParseHex("00C6858E06B70404E9CD9E3ECB662395B4429C648139053FB521F828AF606B4D3DBAA14B5E77EFE75928FE1DC127A2FFA8DE3348B3C1856A429BF97E7E31C2E5BD66"),
        gy: ParseHex("011839296A789A3BC0045C8A5FB42C7D1BD998F54449579B446817AFBD17273E662C97EE72995EF42640C550B9013FAD0761353C7086A272C24088BE94769FD16650"),
        n: ParseHex("01FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFA51868783BF2F966B7FCC0148F709A5D03BB5C9B8899C47AEBB6FB71E91386409"),
        fieldSizeBytes: 66);

    /// <summary>Resolves a curve by its OID.</summary>
    /// <exception cref="NotSupportedException">Thrown for curves Chuvadi doesn't implement.</exception>
    public static EcCurve FromOid(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        if (oid.Equals(KnownOids.Secp256r1)) { return P256; }
        if (oid.Equals(KnownOids.Secp384r1)) { return P384; }
        if (oid.Equals(KnownOids.Secp521r1)) { return P521; }
        throw new NotSupportedException(
            $"Curve {oid} is not supported. Chuvadi currently supports NIST P-256, P-384, and P-521.");
    }

    private static BigInteger ParseHex(string hex)
    {
        // Prepend "00" to force unsigned interpretation by BigInteger.Parse
        return BigInteger.Parse("00" + hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
