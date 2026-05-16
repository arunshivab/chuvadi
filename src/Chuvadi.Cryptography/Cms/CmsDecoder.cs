// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder
//
// Entry point: bytes → ContentInfo → SignedData. The top-level API a PDF
// signature reader actually calls.

using System;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// Decodes CMS / PKCS#7 byte streams into structured Chuvadi objects.
/// </summary>
public static class CmsDecoder
{
    /// <summary>
    /// Decodes <paramref name="cms"/> as a ContentInfo. Use this when you have
    /// the bytes from a PDF /Contents field or a PKCS#7 file.
    /// </summary>
    public static ContentInfo DecodeContentInfo(byte[] cms)
    {
        ArgumentNullException.ThrowIfNull(cms);
        Asn1Reader reader = new(cms);
        ContentInfo ci = ContentInfo.Read(reader);
        reader.ExpectEnd();
        return ci;
    }

    /// <summary>
    /// Decodes <paramref name="cms"/> and returns the inner SignedData.
    /// Throws when the ContentInfo carries a different content type.
    /// </summary>
    public static SignedData DecodeSignedData(byte[] cms)
    {
        ContentInfo ci = DecodeContentInfo(cms);
        return ci.GetSignedData();
    }
}
