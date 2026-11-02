using System;
using System.Globalization;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>
    /// Office embedded-font obfuscation (ECMA-376 Part 1, 17.8.1 "Font Embedding"; the .odttf parts that
    /// w:embedRegular / w:embedBold / … reference with a w:fontKey GUID). The first 32 bytes of the font
    /// are XORed with the 16-byte key derived from the GUID; the operation is its own inverse.
    /// </summary>
    public static class EmbeddedFontObfuscation
    {
        /// <summary>Number of leading bytes the obfuscation touches.</summary>
        public const int ObfuscatedLength = 32;

        /// <summary>
        /// Derives the 16-byte XOR key from a font key such as "{E6E9B4A1-56A3-4E36-8E5F-A6B37C3D3F1E}".
        /// The GUID's 32 hex digits, read as 16 bytes in string order, are used in reverse order.
        /// </summary>
        /// <exception cref="FormatException">The key does not contain exactly 32 hex digits.</exception>
        public static byte[] DeriveKey(string fontKey)
        {
            ArgumentNullException.ThrowIfNull(fontKey);
            Span<char> hex = stackalloc char[32];
            int n = 0;
            foreach (char c in fontKey)
            {
                if (c is '{' or '}' or '-' || char.IsWhiteSpace(c))
                {
                    continue;
                }
                if (!Uri.IsHexDigit(c) || n == 32)
                {
                    throw new FormatException($"Font key '{fontKey}' is not a GUID.");
                }
                hex[n++] = c;
            }
            if (n != 32)
            {
                throw new FormatException($"Font key '{fontKey}' is not a GUID.");
            }
            byte[] stringOrder = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                stringOrder[i] = byte.Parse(hex.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            byte[] key = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                key[i] = stringOrder[15 - i];
            }
            return key;
        }

        /// <summary>Returns a de-obfuscated copy of an embedded font part. The input is never modified.</summary>
        public static byte[] Deobfuscate(ReadOnlySpan<byte> obfuscatedFont, string fontKey)
        {
            byte[] key = DeriveKey(fontKey);
            byte[] result = obfuscatedFont.ToArray();
            int limit = Math.Min(ObfuscatedLength, result.Length);
            for (int i = 0; i < limit; i++)
            {
                result[i] ^= key[i % 16];
            }
            return result;
        }

        /// <summary>Obfuscates a font for embedding (the same transform as <see cref="Deobfuscate"/>).</summary>
        public static byte[] Obfuscate(ReadOnlySpan<byte> font, string fontKey) => Deobfuscate(font, fontKey);
    }
}
