using System;
using System.IO;
using System.Text;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// Builds the binary content for the streams under the <c>\x06DataSpaces</c> storage in
/// an OOXML-encrypted file. The byte-exact layouts here are derived from [MS-OFFCRYPTO]
/// §2.1.4–§2.1.8 and verified against Excel's actual output.
///
/// Strings in these records are length-prefixed UTF-16 LE, with the length being the byte
/// length (NOT char count). Each string is padded to a 4-byte boundary.
///
/// Records contain a fixed trailing version footer:
///   ReaderVersion (4 bytes): major=1, minor=0
///   UpdaterVersion (4 bytes): major=1, minor=0
///   WriterVersion (4 bytes): major=1, minor=0
/// </summary>
internal static class DataSpacesBuilder
{
    /// <summary>
    /// The Version stream content. 76 bytes total.
    /// Format:
    ///   u32 stringLength (60)
    ///   UTF-16 LE: "Microsoft.Container.DataSpaces" (60 bytes, no null terminator)
    ///   3x version footer (ReaderVersion, UpdaterVersion, WriterVersion) - 12 bytes
    /// </summary>
    public static byte[] BuildVersionStream()
    {
        using var ms = new MemoryStream();
        WritePrefixedUtf16(ms, "Microsoft.Container.DataSpaces", aligned: false);
        WriteVersionFooter(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// The DataSpaceMap stream content. 112 bytes total.
    /// Format:
    ///   DataSpaceMapHeader:
    ///     u32 headerLength = 8
    ///     u32 entryCount = 1
    ///   DataSpaceMapEntry:
    ///     u32 entryLength (in bytes from this field through end of entry, including itself)
    ///     u32 referenceComponentCount = 1
    ///     ReferenceComponent[]:
    ///       u32 referenceType (0 = stream)
    ///       PrefixedUnicodeString name "EncryptedPackage"
    ///     PrefixedUnicodeString dataSpaceName "StrongEncryptionDataSpace"
    /// </summary>
    public static byte[] BuildDataSpaceMap()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)8);  // headerLength
            bw.Write((uint)1);  // entryCount
        }

        // Build the entry into a temporary buffer to compute its length.
        using var entryMs = new MemoryStream();
        using (var bw = new BinaryWriter(entryMs, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)1);  // referenceComponentCount
            bw.Write((uint)0);  // referenceType = 0 (stream)
            // PrefixedUnicodeString "EncryptedPackage" (32 bytes UTF-16, padded to 4-byte boundary)
            WritePrefixedUtf16(entryMs, "EncryptedPackage", aligned: true);
            // PrefixedUnicodeString "StrongEncryptionDataSpace" (50 bytes UTF-16, padded)
            WritePrefixedUtf16(entryMs, "StrongEncryptionDataSpace", aligned: true);
        }
        var entryBytes = entryMs.ToArray();

        // Write entry length (entryLength field + entry content length).
        using (var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)(4 + entryBytes.Length));  // entryLength includes itself
            bw.Write(entryBytes);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// The StrongEncryptionDataSpace stream. 64 bytes.
    /// Format:
    ///   u32 headerLength = 8
    ///   u32 transformCount = 1
    ///   PrefixedUnicodeString transformName "StrongEncryptionTransform"
    /// </summary>
    public static byte[] BuildStrongEncryptionDataSpace()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)8);  // headerLength
            bw.Write((uint)1);  // transformCount
        }
        WritePrefixedUtf16(ms, "StrongEncryptionTransform", aligned: true);
        return ms.ToArray();
    }

    /// <summary>
    /// The \x06Primary stream. 200 bytes.
    /// Format:
    ///   TransformInfoHeader:
    ///     u32 transformInfoSize = 88 (total size of this header including TransformId)
    ///     u32 transformType = 1
    ///     PrefixedUnicodeString transformId "{FF9A3F03-56EF-4613-BDD5-5A41C1D07246}"
    ///   PrefixedUnicodeString transformName "Microsoft.Container.EncryptionTransform"
    ///   VersionInfo (12 bytes): Reader/Updater/Writer versions
    ///   u32 encryptionTransformInfo = 4
    /// </summary>
    public static byte[] BuildPrimary()
    {
        using var ms = new MemoryStream();

        // Compute TransformInfoHeader size first.
        using var headerMs = new MemoryStream();
        using (var bw = new BinaryWriter(headerMs, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)1);  // transformType
            WritePrefixedUtf16(headerMs, "{FF9A3F03-56EF-4613-BDD5-5A41C1D07246}", aligned: true);
        }
        var headerInner = headerMs.ToArray();

        // Write the full Primary stream.
        using (var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)(4 + headerInner.Length));  // transformInfoSize = self + inner
            bw.Write(headerInner);
        }
        WritePrefixedUtf16(ms, "Microsoft.Container.EncryptionTransform", aligned: true);
        WriteVersionFooter(ms);

        // Trailing data: 12 bytes of zeros + u32 EncryptionConfig = 4
        // (per Excel's reference; the zeros are reserved fields)
        using (var bw = new BinaryWriter(ms, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)0);
            bw.Write((uint)0);
            bw.Write((uint)0);
            bw.Write((uint)4);  // EncryptionConfig
        }

        return ms.ToArray();
    }

    // ---- Helpers ----------------------------------------------------------------

    /// <summary>
    /// Writes a length-prefixed UTF-16 LE string. The 4-byte length prefix is the byte length
    /// of the string (NOT char count, NOT including any null terminator).
    /// If aligned is true, pads with zero bytes to a 4-byte boundary after the string.
    /// </summary>
    private static void WritePrefixedUtf16(Stream s, string value, bool aligned)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        using (var bw = new BinaryWriter(s, Encoding.Unicode, leaveOpen: true))
        {
            bw.Write((uint)bytes.Length);
            bw.Write(bytes);
        }
        if (aligned)
        {
            int pad = (4 - (bytes.Length & 3)) & 3;
            if (pad > 0) s.Write(new byte[pad], 0, pad);
        }
    }

    /// <summary>
    /// Writes the standard 12-byte version footer that appears in several DataSpaces records.
    /// Per [MS-OFFCRYPTO] §2.1.5 etc. — Reader/Updater/Writer versions all 1.0.
    /// </summary>
    private static void WriteVersionFooter(Stream s)
    {
        using var bw = new BinaryWriter(s, Encoding.Unicode, leaveOpen: true);
        bw.Write((ushort)1); bw.Write((ushort)0);  // ReaderVersion: major=1, minor=0
        bw.Write((ushort)1); bw.Write((ushort)0);  // UpdaterVersion
        bw.Write((ushort)1); bw.Write((ushort)0);  // WriterVersion
    }
}
