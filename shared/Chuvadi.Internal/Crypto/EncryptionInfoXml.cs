using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Chuvadi.Internal.Crypto;

/// <summary>
/// Reads and writes the EncryptionInfo stream of an encrypted OOXML file. The stream is a
/// little-endian framed XML document containing the agile-encryption parameters.
///
/// Format:
///   [u16 majorVersion = 4][u16 minorVersion = 4][u32 flags = 0x40][XML document]
///
/// The XML root is &lt;encryption&gt; in the namespace
///   "http://schemas.microsoft.com/office/2006/encryption"
/// containing two children:
///   &lt;keyData&gt;   — parameters for content encryption (per-segment)
///   &lt;keyEncryptors&gt;&lt;keyEncryptor uri="...passwordKeyEncryptor"&gt;
///     &lt;p:encryptedKey ... /&gt; — parameters for password-derived key encryption + verifier
/// </summary>
internal static class EncryptionInfoXml
{
    private const string NsBase = "http://schemas.microsoft.com/office/2006/encryption";
    private const string NsPassword = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";

    /// <summary>Writes the EncryptionInfo stream (header + XML) into <paramref name="output"/>.</summary>
    public static void Write(Stream output, AgileEncryption.Params p)
    {
        // 8-byte header: u16 major=4, u16 minor=4, u32 flags=0x40 (agile encryption).
        Span<byte> hdr = stackalloc byte[8];
        hdr[0] = 0x04; hdr[1] = 0x00;
        hdr[2] = 0x04; hdr[3] = 0x00;
        hdr[4] = 0x40; hdr[5] = 0x00; hdr[6] = 0x00; hdr[7] = 0x00;
        output.Write(hdr);

        // Build the XML manually to match Excel's exact byte layout.
        // Excel's reference uses:
        //   - encoding="UTF-8" (uppercase)
        //   - \r\n line break after the XML declaration
        //   - Three xmlns declarations in this specific order:
        //       xmlns (default), xmlns:p, xmlns:c
        //   - No whitespace/indentation between elements
        //   - Self-closing tags ending with /> (no space before />)
        //   - No xml byte order mark
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");

        sb.Append("<encryption");
        sb.Append(" xmlns=\"http://schemas.microsoft.com/office/2006/encryption\"");
        sb.Append(" xmlns:p=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\"");
        sb.Append(" xmlns:c=\"http://schemas.microsoft.com/office/2006/keyEncryptor/certificate\">");

        // <keyData ...>
        sb.Append("<keyData saltSize=\"16\" blockSize=\"16\" keyBits=\"256\" hashSize=\"64\"");
        sb.Append(" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA512\"");
        sb.Append(" saltValue=\""); sb.Append(Convert.ToBase64String(p.KeySalt)); sb.Append("\"/>");

        // <dataIntegrity ...>
        sb.Append("<dataIntegrity");
        sb.Append(" encryptedHmacKey=\""); sb.Append(Convert.ToBase64String(p.EncryptedHmacKey)); sb.Append("\"");
        sb.Append(" encryptedHmacValue=\""); sb.Append(Convert.ToBase64String(p.EncryptedHmacValue)); sb.Append("\"/>");

        // <keyEncryptors><keyEncryptor uri="...password"><p:encryptedKey .../></keyEncryptor></keyEncryptors>
        sb.Append("<keyEncryptors>");
        sb.Append("<keyEncryptor uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\">");
        sb.Append("<p:encryptedKey");
        sb.Append(" spinCount=\""); sb.Append(p.SpinCount.ToString(inv)); sb.Append("\"");
        sb.Append(" saltSize=\"16\" blockSize=\"16\" keyBits=\"256\" hashSize=\"64\"");
        sb.Append(" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA512\"");
        sb.Append(" saltValue=\""); sb.Append(Convert.ToBase64String(p.VerifierSalt)); sb.Append("\"");
        sb.Append(" encryptedVerifierHashInput=\""); sb.Append(Convert.ToBase64String(p.EncryptedVerifierHashInput)); sb.Append("\"");
        sb.Append(" encryptedVerifierHashValue=\""); sb.Append(Convert.ToBase64String(p.EncryptedVerifierHashValue)); sb.Append("\"");
        sb.Append(" encryptedKeyValue=\""); sb.Append(Convert.ToBase64String(p.EncryptedKeyValue)); sb.Append("\"/>");
        sb.Append("</keyEncryptor>");
        sb.Append("</keyEncryptors>");

        sb.Append("</encryption>");

        var xmlBytes = Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(xmlBytes, 0, xmlBytes.Length);
    }

    /// <summary>Reads the EncryptionInfo stream and returns the parsed agile-encryption parameters.</summary>
    public static AgileEncryption.Params Read(Stream input)
    {
        // Skip the 8-byte header.
        Span<byte> hdr = stackalloc byte[8];
        input.ReadExactly(hdr);

        var p = new AgileEncryption.Params();

        using var r = XmlReader.Create(input, new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            CloseInput = false,
        });

        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            switch (r.LocalName)
            {
                case "keyData":
                    p.KeySalt = ReadBase64Attr(r, "saltValue");
                    break;
                case "dataIntegrity":
                    p.EncryptedHmacKey = ReadBase64Attr(r, "encryptedHmacKey");
                    p.EncryptedHmacValue = ReadBase64Attr(r, "encryptedHmacValue");
                    break;
                case "encryptedKey":
                    // The <encryptedKey> inside <keyEncryptor uri="...password">.
                    p.SpinCount = int.Parse(r.GetAttribute("spinCount") ?? "100000", System.Globalization.CultureInfo.InvariantCulture);
                    p.VerifierSalt = ReadBase64Attr(r, "saltValue");
                    p.EncryptedVerifierHashInput = ReadBase64Attr(r, "encryptedVerifierHashInput");
                    p.EncryptedVerifierHashValue = ReadBase64Attr(r, "encryptedVerifierHashValue");
                    p.EncryptedKeyValue = ReadBase64Attr(r, "encryptedKeyValue");
                    break;
            }
        }

        return p;
    }

    private static byte[] ReadBase64Attr(XmlReader r, string name)
    {
        var v = r.GetAttribute(name);
        return v is null ? Array.Empty<byte>() : Convert.FromBase64String(v);
    }
}
