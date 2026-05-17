// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — HMAC primitive

using System;
using Chuvadi.Cryptography.Hashing;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Hashing;

public sealed class HmacTests
{
    [Fact]
    public void Compute_Sha256_Rfc4231_TestCase1_MatchesExpected()
    {
        // RFC 4231 §4.2 Test Case 1
        byte[] key = new byte[20];
        for (int i = 0; i < 20; i++) { key[i] = 0x0b; }
        byte[] msg = System.Text.Encoding.ASCII.GetBytes("Hi There");

        byte[] mac = Hmac.Compute(HashAlgorithmName.Sha256, key, msg);

        Convert.ToHexString(mac).Should().Be(
            "B0344C61D8DB38535CA8AFCEAF0BF12B881DC200C9833DA726E9376C2E32CFF7");
    }

    [Fact]
    public void Compute_Sha256_Rfc4231_TestCase2_MatchesExpected()
    {
        // RFC 4231 §4.3 Test Case 2 — key "Jefe", data "what do ya want for nothing?"
        byte[] key = System.Text.Encoding.ASCII.GetBytes("Jefe");
        byte[] msg = System.Text.Encoding.ASCII.GetBytes("what do ya want for nothing?");

        byte[] mac = Hmac.Compute(HashAlgorithmName.Sha256, key, msg);

        Convert.ToHexString(mac).Should().Be(
            "5BDCC146BF60754E6A042426089575C75A003F089D2739839DEC58B964EC3843");
    }

    [Fact]
    public void Compute_Sha256_LongKey_HashesKeyFirst()
    {
        // RFC 4231 §4.7 Test Case 6 — key longer than block size
        byte[] key = new byte[131];
        for (int i = 0; i < 131; i++) { key[i] = 0xaa; }
        byte[] msg = System.Text.Encoding.ASCII.GetBytes(
            "Test Using Larger Than Block-Size Key - Hash Key First");

        byte[] mac = Hmac.Compute(HashAlgorithmName.Sha256, key, msg);

        Convert.ToHexString(mac).Should().Be(
            "60E431591EE0B67F0D8A26AACBF5B77F8E0BC6213728C5140546040F0EE37F54");
    }
}
