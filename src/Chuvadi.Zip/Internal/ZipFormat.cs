using System;
using System.Buffers.Binary;

namespace Chuvadi.Zip.Internal;

/// <summary>Zip file-format constants and small encoding helpers (APPNOTE 6.3.x).</summary>
internal static class ZipFormat
{
    public const uint LocalHeaderSig = 0x04034b50;
    public const uint CentralHeaderSig = 0x02014b50;
    public const uint EndOfCentralDirSig = 0x06054b50;
    public const uint Zip64EndOfCentralDirSig = 0x06064b50;
    public const uint Zip64LocatorSig = 0x07064b50;
    public const uint DataDescriptorSig = 0x08074b50;

    public const int LocalHeaderFixed = 30;
    public const int CentralHeaderFixed = 46;
    public const int EocdFixed = 22;
    public const int Zip64LocatorSize = 20;
    public const int Zip64EocdFixed = 56;

    public const ushort FlagEncrypted = 0x0001;
    public const ushort FlagDataDescriptor = 0x0008;
    public const ushort FlagStrongEncryption = 0x0040;
    public const ushort FlagUtf8 = 0x0800;

    public const ushort ExtraZip64 = 0x0001;
    public const ushort ExtraNtfs = 0x000A;
    public const ushort ExtraExtendedTimestamp = 0x5455;
    public const ushort ExtraAes = 0x9901;
    public const ushort ExtraUnicodePath = 0x7075;
    public const ushort ExtraUnicodeComment = 0x6375;

    public const ushort MethodAes = 99;

    public const uint Max32 = 0xFFFFFFFF;
    public const ushort Max16 = 0xFFFF;

    /// <summary>Entries whose expected size reaches this are written as Zip64 in Auto mode
    /// (leaves ~1 MiB of headroom for incompressible-data and AES overhead).</summary>
    public const ulong Zip64AutoThreshold = 0xFFF00000UL;

    // ---- DOS date/time -------------------------------------------------------------

    public static DateTime DosToLocal(ushort date, ushort time)
    {
        int year = 1980 + (date >> 9);
        int month = (date >> 5) & 0xF;
        int day = date & 0x1F;
        int hour = time >> 11;
        int minute = (time >> 5) & 0x3F;
        int second = (time & 0x1F) * 2;
        if (month < 1 || month > 12) month = 1;
        if (day < 1) day = 1;
        int maxDay = DateTime.DaysInMonth(year, month);
        if (day > maxDay) day = maxDay;
        if (hour > 23) hour = 23;
        if (minute > 59) minute = 59;
        if (second > 59) second = 59;
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
    }

    public static (ushort Date, ushort Time) LocalToDos(DateTime local)
    {
        if (local.Year < 1980) local = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);
        else if (local.Year > 2107) local = new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Local);
        ushort date = (ushort)(((local.Year - 1980) << 9) | (local.Month << 5) | local.Day);
        ushort time = (ushort)((local.Hour << 11) | (local.Minute << 5) | (local.Second / 2));
        return (date, time);
    }

    // ---- Extra fields --------------------------------------------------------------

    /// <summary>Finds the first extra field with the given id. Tolerates truncated trailing data.</summary>
    public static bool TryGetExtra(ReadOnlySpan<byte> extra, ushort id, out ReadOnlySpan<byte> data)
    {
        int p = 0;
        while (p + 4 <= extra.Length)
        {
            ushort fid = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(p));
            int len = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(p + 2));
            p += 4;
            if (p + len > extra.Length) break;
            if (fid == id) { data = extra.Slice(p, len); return true; }
            p += len;
        }
        data = default;
        return false;
    }

    public static bool IsAscii(string s)
    {
        foreach (char c in s) if (c > 0x7F) return false;
        return true;
    }

    public static bool TryFileTime(long ft, out DateTimeOffset value)
    {
        value = default;
        try
        {
            if (ft <= 0) return false;
            value = new DateTimeOffset(DateTime.FromFileTimeUtc(ft), TimeSpan.Zero);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
