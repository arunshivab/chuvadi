// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for SignatureSubFilter

using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests;

public sealed class SignatureSubFilterTests
{
    [Theory]
    [InlineData("adbe.pkcs7.detached")]
    [InlineData("adbe.pkcs7.sha1")]
    [InlineData("ETSI.CAdES.detached")]
    [InlineData("ETSI.RFC3161")]
    public void IsCmsBased_ReturnsTrueForKnownSubFilters(string value)
    {
        SignatureSubFilter.IsCmsBased(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("adbe.x509.rsa_sha1")]
    [InlineData("custom.handler")]
    [InlineData("")]
    public void IsCmsBased_ReturnsFalseForOthers(string value)
    {
        SignatureSubFilter.IsCmsBased(value).Should().BeFalse();
    }

    [Fact]
    public void IsCmsBased_NullSafe()
    {
        SignatureSubFilter.IsCmsBased(null!).Should().BeFalse();
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        SignatureSubFilter.AdbePkcs7Detached.Should().Be("adbe.pkcs7.detached");
        SignatureSubFilter.EtsiCAdESDetached.Should().Be("ETSI.CAdES.detached");
        SignatureSubFilter.EtsiRfc3161.Should().Be("ETSI.RFC3161");
    }
}
