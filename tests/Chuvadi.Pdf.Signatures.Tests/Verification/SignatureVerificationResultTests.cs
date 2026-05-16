// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for SignatureVerificationResult

using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Verification;

public sealed class SignatureVerificationResultTests
{
    [Fact]
    public void Construct_Valid_ExposesAllFields()
    {
        SignatureVerificationResult r = new(
            SignatureVerificationStatus.Valid,
            "ok",
            signerCertificate: null,
            integrityVerified: true);
        r.Status.Should().Be(SignatureVerificationStatus.Valid);
        r.IsValid.Should().BeTrue();
        r.IntegrityVerified.Should().BeTrue();
        r.Message.Should().Be("ok");
    }

    [Fact]
    public void IsValid_OnlyTrueForValidStatus()
    {
        foreach (SignatureVerificationStatus s in System.Enum.GetValues<SignatureVerificationStatus>())
        {
            SignatureVerificationResult r = new(s, "msg", null, integrityVerified: false);
            r.IsValid.Should().Be(s == SignatureVerificationStatus.Valid);
        }
    }
}
