// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.3.2, Table 22 — User access permissions
// PHASE: v2.0.0 — EncryptionInfo unit tests

using Chuvadi.Pdf.Encryption;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class EncryptionInfoTests
{
    // ── Property propagation ──────────────────────────────────────────────

    [Fact]
    public void Constructor_PropertiesPropagate()
    {
        EncryptionInfo info = new EncryptionInfo(
            algorithm: EncryptionAlgorithm.Aes_256,
            keyLength: 32,
            revision: 6,
            version: 5,
            permissions: -3904,
            encryptMetadata: true);

        info.Algorithm.Should().Be(EncryptionAlgorithm.Aes_256);
        info.KeyLength.Should().Be(32);
        info.Revision.Should().Be(6);
        info.Version.Should().Be(5);
        info.Permissions.Should().Be(-3904);
        info.EncryptMetadata.Should().BeTrue();
    }

    [Fact]
    public void Constructor_EncryptMetadataFalse_Propagates()
    {
        EncryptionInfo info = new EncryptionInfo(
            algorithm: EncryptionAlgorithm.Aes_128,
            keyLength: 16,
            revision: 4,
            version: 4,
            permissions: 0,
            encryptMetadata: false);

        info.EncryptMetadata.Should().BeFalse();
    }

    // ── Permission decoders: bit set → property true ──────────────────────

    [Theory]
    [InlineData(1 << 2, true, false, false, false, false, false, false, false)]   // Print
    [InlineData(1 << 3, false, true, false, false, false, false, false, false)]   // Modify
    [InlineData(1 << 4, false, false, true, false, false, false, false, false)]   // Copy
    [InlineData(1 << 5, false, false, false, true, false, false, false, false)]   // Annotate
    [InlineData(1 << 8, false, false, false, false, true, false, false, false)]   // FillForms
    [InlineData(1 << 9, false, false, false, false, false, true, false, false)]   // AccessibilityExtract
    [InlineData(1 << 10, false, false, false, false, false, false, true, false)]  // Assemble
    [InlineData(1 << 11, false, false, false, false, false, false, false, true)]  // PrintHighQuality
    public void Permission_SingleBit_OnlyMatchingFlagIsTrue(
        int permissions,
        bool expectedPrint,
        bool expectedModify,
        bool expectedCopy,
        bool expectedAnnotate,
        bool expectedFillForms,
        bool expectedAccessibility,
        bool expectedAssemble,
        bool expectedPrintHighQuality)
    {
        EncryptionInfo info = new EncryptionInfo(
            algorithm: EncryptionAlgorithm.Aes_128,
            keyLength: 16,
            revision: 4,
            version: 4,
            permissions: permissions,
            encryptMetadata: true);

        info.AllowPrint.Should().Be(expectedPrint);
        info.AllowModify.Should().Be(expectedModify);
        info.AllowCopy.Should().Be(expectedCopy);
        info.AllowAnnotate.Should().Be(expectedAnnotate);
        info.AllowFillForms.Should().Be(expectedFillForms);
        info.AllowAccessibilityExtract.Should().Be(expectedAccessibility);
        info.AllowAssemble.Should().Be(expectedAssemble);
        info.AllowPrintHighQuality.Should().Be(expectedPrintHighQuality);
    }

    [Fact]
    public void Permission_AllBitsZero_AllFlagsFalse()
    {
        EncryptionInfo info = new EncryptionInfo(
            algorithm: EncryptionAlgorithm.Aes_128,
            keyLength: 16,
            revision: 4,
            version: 4,
            permissions: 0,
            encryptMetadata: true);

        info.AllowPrint.Should().BeFalse();
        info.AllowModify.Should().BeFalse();
        info.AllowCopy.Should().BeFalse();
        info.AllowAnnotate.Should().BeFalse();
        info.AllowFillForms.Should().BeFalse();
        info.AllowAccessibilityExtract.Should().BeFalse();
        info.AllowAssemble.Should().BeFalse();
        info.AllowPrintHighQuality.Should().BeFalse();
    }

    [Fact]
    public void Permission_AllRelevantBitsSet_AllFlagsTrue()
    {
        // All 8 permission bits combined into one mask.
        int allPermissions =
            (1 << 2) | (1 << 3) | (1 << 4) | (1 << 5) |
            (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11);

        EncryptionInfo info = new EncryptionInfo(
            algorithm: EncryptionAlgorithm.Aes_256,
            keyLength: 32,
            revision: 6,
            version: 5,
            permissions: allPermissions,
            encryptMetadata: true);

        info.AllowPrint.Should().BeTrue();
        info.AllowModify.Should().BeTrue();
        info.AllowCopy.Should().BeTrue();
        info.AllowAnnotate.Should().BeTrue();
        info.AllowFillForms.Should().BeTrue();
        info.AllowAccessibilityExtract.Should().BeTrue();
        info.AllowAssemble.Should().BeTrue();
        info.AllowPrintHighQuality.Should().BeTrue();
    }

    [Fact]
    public void Permission_DefaultAllowAll_AllFlagsTrue()
    {
        // -4 (= 0xFFFFFFFC) is the canonical "all permissions allowed" value:
        // all eight permission bits set (bits 2, 3, 4, 5, 8, 9, 10, 11),
        // both reserved-must-be-one bits set (6, 7), and all high reserved
        // bits (12-31) set. Only bits 0-1 are clear (they are reserved-must-
        // be-zero per spec).
        EncryptionInfo info = new EncryptionInfo(
            algorithm: EncryptionAlgorithm.Aes_256,
            keyLength: 32,
            revision: 6,
            version: 5,
            permissions: -4,
            encryptMetadata: true);

        info.AllowPrint.Should().BeTrue();
        info.AllowModify.Should().BeTrue();
        info.AllowCopy.Should().BeTrue();
        info.AllowAnnotate.Should().BeTrue();
        info.AllowFillForms.Should().BeTrue();
        info.AllowAccessibilityExtract.Should().BeTrue();
        info.AllowAssemble.Should().BeTrue();
        info.AllowPrintHighQuality.Should().BeTrue();
    }
}
