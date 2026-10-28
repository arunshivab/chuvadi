using System;

namespace Chuvadi.Zip.Internal;

/// <summary>
/// Traditional PKWARE ("ZipCrypto") decryption. READ ONLY: the scheme is cryptographically
/// broken and the library deliberately offers no way to write it.
/// </summary>
internal struct ZipCryptoKeys
{
    private uint _k0, _k1, _k2;

    public ZipCryptoKeys(ReadOnlySpan<byte> password)
    {
        _k0 = 305419896u;
        _k1 = 591751049u;
        _k2 = 878082192u;
        foreach (byte b in password) Update(b);
    }

    private void Update(byte c)
    {
        _k0 = Crc32.UpdateRaw(_k0, c);
        _k1 = (_k1 + (_k0 & 0xFF)) * 134775813u + 1;
        _k2 = Crc32.UpdateRaw(_k2, (byte)(_k1 >> 24));
    }

    private readonly byte NextKeyByte()
    {
        uint t = (_k2 & 0xFFFF) | 2;
        return (byte)((t * (t ^ 1)) >> 8);
    }

    public void Decrypt(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            byte p = (byte)(buffer[i] ^ NextKeyByte());
            Update(p);
            buffer[i] = p;
        }
    }
}
