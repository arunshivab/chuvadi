// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.680 §46 (GeneralizedTime), §47 (UTCTime);
//        RFC 5280 §4.1.2.5 (PKIX restrictions)
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Encode and decode ASN.1 UTCTime and GeneralizedTime values.
/// </summary>
/// <remarks>
/// Per RFC 5280 §4.1.2.5 (PKIX restrictions for X.509 certificate validity dates):
/// <list type="bullet">
///   <item>UTCTime is "YYMMDDhhmmssZ" (always with seconds, always Z suffix).</item>
///   <item>GeneralizedTime is "YYYYMMDDhhmmssZ" with no fractional seconds.</item>
///   <item>UTCTime year 00..49 maps to 2000..2049; year 50..99 maps to 1950..1999.</item>
/// </list>
/// Chuvadi emits these strict forms only. On reading, accepts the RFC 5280
/// form plus the broader ASN.1 form (optional seconds in UTCTime, fractional
/// seconds in GeneralizedTime) which appears in CAdES signatures.
/// </remarks>
public static class Asn1Time
{
    // ── UTCTime ───────────────────────────────────────────────────────────

    /// <summary>Writes a UTCTime in RFC 5280 form (YYMMDDhhmmssZ).</summary>
    public static void WriteUtcTime(Stream output, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(output);
        DateTimeOffset utc = value.ToUniversalTime();
        if (utc.Year < 1950 || utc.Year > 2049)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                "UTCTime can only represent years 1950..2049. Use GeneralizedTime instead.");
        }

        int yearTwoDigit = utc.Year % 100;
        string s = string.Create(CultureInfo.InvariantCulture, $"{yearTwoDigit:D2}{utc.Month:D2}{utc.Day:D2}{utc.Hour:D2}{utc.Minute:D2}{utc.Second:D2}Z");
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.UtcTime), bytes.Length);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Reads a UTCTime.</summary>
    public static int ReadUtcTime(byte[] source, int offset, out DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.UtcTime))
        {
            throw new Asn1Exception($"Expected UTCTime tag, got {tag}", offset);
        }

        string s = Encoding.ASCII.GetString(source, contentOffset, len);
        value = ParseUtcTime(s, offset);
        return after;
    }

    private static DateTimeOffset ParseUtcTime(string s, long errorOffset)
    {
        // Forms supported:
        //   YYMMDDhhmmZ     (10+1 = 11 chars, no seconds)
        //   YYMMDDhhmmssZ   (12+1 = 13 chars, with seconds)
        //   YYMMDDhhmm±HHMM (15 chars, no seconds, offset)
        //   YYMMDDhhmmss±HHMM (17 chars, with seconds, offset)
        if (s.Length < 11)
        {
            throw new Asn1Exception($"UTCTime too short: '{s}'", errorOffset);
        }

        try
        {
            int yy = int.Parse(s.AsSpan(0, 2), CultureInfo.InvariantCulture);
            int month = int.Parse(s.AsSpan(2, 2), CultureInfo.InvariantCulture);
            int day = int.Parse(s.AsSpan(4, 2), CultureInfo.InvariantCulture);
            int hour = int.Parse(s.AsSpan(6, 2), CultureInfo.InvariantCulture);
            int minute = int.Parse(s.AsSpan(8, 2), CultureInfo.InvariantCulture);

            int year = yy < 50 ? 2000 + yy : 1900 + yy;

            int pos = 10;
            int second = 0;
            // Optional seconds
            if (s.Length >= 13 && char.IsDigit(s[10]) && char.IsDigit(s[11]))
            {
                second = int.Parse(s.AsSpan(10, 2), CultureInfo.InvariantCulture);
                pos = 12;
            }

            TimeSpan zoneOffset;
            if (pos >= s.Length)
            {
                throw new Asn1Exception($"UTCTime missing zone designator: '{s}'", errorOffset);
            }
            if (s[pos] == 'Z')
            {
                zoneOffset = TimeSpan.Zero;
            }
            else if ((s[pos] == '+' || s[pos] == '-') && s.Length >= pos + 5)
            {
                int sign = s[pos] == '+' ? 1 : -1;
                int hh = int.Parse(s.AsSpan(pos + 1, 2), CultureInfo.InvariantCulture);
                int mm = int.Parse(s.AsSpan(pos + 3, 2), CultureInfo.InvariantCulture);
                zoneOffset = new TimeSpan(sign * hh, sign * mm, 0);
            }
            else
            {
                throw new Asn1Exception($"UTCTime has invalid zone designator: '{s}'", errorOffset);
            }

            return new DateTimeOffset(year, month, day, hour, minute, second, zoneOffset);
        }
        catch (FormatException ex)
        {
            throw new Asn1Exception($"UTCTime is not parseable: '{s}'", ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new Asn1Exception($"UTCTime contains out-of-range component: '{s}'", ex);
        }
    }

    // ── GeneralizedTime ───────────────────────────────────────────────────

    /// <summary>Writes a GeneralizedTime in RFC 5280 form (YYYYMMDDhhmmssZ, no fractional seconds).</summary>
    public static void WriteGeneralizedTime(Stream output, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(output);
        DateTimeOffset utc = value.ToUniversalTime();
        string s = string.Create(CultureInfo.InvariantCulture, $"{utc.Year:D4}{utc.Month:D2}{utc.Day:D2}{utc.Hour:D2}{utc.Minute:D2}{utc.Second:D2}Z");
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.GeneralizedTime), bytes.Length);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Reads a GeneralizedTime.</summary>
    public static int ReadGeneralizedTime(byte[] source, int offset, out DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.GeneralizedTime))
        {
            throw new Asn1Exception($"Expected GeneralizedTime tag, got {tag}", offset);
        }
        string s = Encoding.ASCII.GetString(source, contentOffset, len);
        value = ParseGeneralizedTime(s, offset);
        return after;
    }

    private static DateTimeOffset ParseGeneralizedTime(string s, long errorOffset)
    {
        // Minimum: YYYYMMDDhh (10 chars). Accept many CAdES/RFC 5280 variants.
        if (s.Length < 10)
        {
            throw new Asn1Exception($"GeneralizedTime too short: '{s}'", errorOffset);
        }

        try
        {
            int year = int.Parse(s.AsSpan(0, 4), CultureInfo.InvariantCulture);
            int month = int.Parse(s.AsSpan(4, 2), CultureInfo.InvariantCulture);
            int day = int.Parse(s.AsSpan(6, 2), CultureInfo.InvariantCulture);
            int hour = int.Parse(s.AsSpan(8, 2), CultureInfo.InvariantCulture);

            int pos = 10;
            int minute = 0, second = 0;
            double fractional = 0.0;

            // Optional minutes
            if (pos + 1 < s.Length && char.IsDigit(s[pos]) && char.IsDigit(s[pos + 1]))
            {
                minute = int.Parse(s.AsSpan(pos, 2), CultureInfo.InvariantCulture);
                pos += 2;
            }
            // Optional seconds
            if (pos + 1 < s.Length && char.IsDigit(s[pos]) && char.IsDigit(s[pos + 1]))
            {
                second = int.Parse(s.AsSpan(pos, 2), CultureInfo.InvariantCulture);
                pos += 2;
            }
            // Optional fractional seconds (. or , then digits)
            if (pos < s.Length && (s[pos] == '.' || s[pos] == ','))
            {
                int fracStart = pos + 1;
                int fracEnd = fracStart;
                while (fracEnd < s.Length && char.IsDigit(s[fracEnd]))
                {
                    fracEnd++;
                }
                if (fracEnd > fracStart)
                {
                    fractional = double.Parse(
                        "0." + s.Substring(fracStart, fracEnd - fracStart),
                        CultureInfo.InvariantCulture);
                }
                pos = fracEnd;
            }

            // Zone
            TimeSpan zoneOffset;
            if (pos >= s.Length)
            {
                // No zone designator — treat as local per X.680, but PKIX requires Z;
                // we treat absence as UTC to be safe.
                zoneOffset = TimeSpan.Zero;
            }
            else if (s[pos] == 'Z')
            {
                zoneOffset = TimeSpan.Zero;
            }
            else if ((s[pos] == '+' || s[pos] == '-') && s.Length >= pos + 5)
            {
                int sign = s[pos] == '+' ? 1 : -1;
                int hh = int.Parse(s.AsSpan(pos + 1, 2), CultureInfo.InvariantCulture);
                int mm = int.Parse(s.AsSpan(pos + 3, 2), CultureInfo.InvariantCulture);
                zoneOffset = new TimeSpan(sign * hh, sign * mm, 0);
            }
            else
            {
                throw new Asn1Exception(
                    $"GeneralizedTime has invalid zone designator: '{s}'", errorOffset);
            }

            DateTimeOffset baseTime = new(year, month, day, hour, minute, second, zoneOffset);
            if (fractional > 0)
            {
                baseTime = baseTime.AddTicks((long)(fractional * TimeSpan.TicksPerSecond));
            }
            return baseTime;
        }
        catch (FormatException ex)
        {
            throw new Asn1Exception($"GeneralizedTime is not parseable: '{s}'", ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new Asn1Exception($"GeneralizedTime contains out-of-range component: '{s}'", ex);
        }
    }
}
