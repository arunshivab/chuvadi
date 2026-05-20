// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Chuvadi.Pdf.Fuzz;

/// <summary>
/// Runs random-input mutation fuzzing against a target.
/// </summary>
/// <remarks>
/// The runner is NOT coverage-guided. It maintains a seed corpus in memory, picks a seed
/// at random per iteration, applies one or more mutations, then feeds the result to the
/// target. Throws on the target's documented <see cref="IFuzzTarget.ExpectedExceptionTypes"/>
/// list are counted as cleanly-rejected input. Any other exception is recorded as a crash.
/// </remarks>
internal sealed class FuzzRunner
{
    private readonly IFuzzTarget _target;
    private readonly string _crashesDir;
    private readonly FuzzOptions _options;
    private readonly List<byte[]> _corpus;
    private readonly Random _rng;

    public FuzzRunner(IFuzzTarget target, string corpusDir, string crashesDir, FuzzOptions options)
    {
        _target = target;
        _crashesDir = crashesDir;
        _options = options;
        _corpus = LoadCorpus(corpusDir);
        _rng = new Random(options.Seed);

        if (_corpus.Count == 0)
        {
            // No seeds — start with one zero-byte buffer. Mutations will grow it.
            _corpus.Add(new byte[1]);
        }
    }

    public FuzzResult Run()
    {
        Stopwatch sw = Stopwatch.StartNew();
        long iterations = 0;
        long expectedExceptions = 0;
        int crashCount = 0;
        HashSet<string> seenCrashHashes = new();
        TimeSpan reportInterval = TimeSpan.FromSeconds(10);
        TimeSpan nextReport = reportInterval;

        while (sw.Elapsed < _options.Duration)
        {
            byte[] input = NextMutation();
            iterations++;

            try
            {
                _target.Run(input);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                expectedExceptions++;
            }
            catch (Exception ex)
            {
                string hash = SaveCrash(input, ex);
                if (seenCrashHashes.Add(hash))
                {
                    crashCount++;
                    Console.WriteLine($"  CRASH  {hash}  {ex.GetType().Name}: {Truncate(ex.Message, 80)}");
                }
            }

            if (sw.Elapsed >= nextReport)
            {
                double rate = iterations / sw.Elapsed.TotalSeconds;
                Console.WriteLine($"  [{sw.Elapsed.TotalSeconds,5:F0}s] {iterations,10:N0} iter  " +
                                  $"{rate,7:N0}/s  expected={expectedExceptions:N0}  crashes={crashCount}");
                nextReport += reportInterval;
            }
        }
        sw.Stop();

        return new FuzzResult
        {
            Iterations = iterations,
            ExpectedExceptions = expectedExceptions,
            CrashCount = crashCount,
            ThroughputPerSec = (long)(iterations / sw.Elapsed.TotalSeconds),
        };
    }

    private bool IsExpected(Exception ex)
    {
        foreach (Type t in _target.ExpectedExceptionTypes)
        {
            if (t.IsAssignableFrom(ex.GetType()))
            {
                return true;
            }
        }
        return false;
    }

    private byte[] NextMutation()
    {
        byte[] seed = _corpus[_rng.Next(_corpus.Count)];
        int mutationCount = 1 + _rng.Next(3);   // 1-3 mutations stacked
        byte[] work = (byte[])seed.Clone();
        for (int i = 0; i < mutationCount; i++)
        {
            work = Mutate(work);
            if (work.Length > _options.MaxInputSize)
            {
                Array.Resize(ref work, _options.MaxInputSize);
            }
        }
        return work;
    }

    private byte[] Mutate(byte[] input)
    {
        if (input.Length == 0)
        {
            return new byte[] { (byte)_rng.Next(256) };
        }

        int op = _rng.Next(8);
        switch (op)
        {
            case 0:
                return FlipBit(input);
            case 1:
                return ReplaceByte(input);
            case 2:
                return InsertByte(input);
            case 3:
                return DeleteByte(input);
            case 4:
                return ReplaceWithInterestingInt(input);
            case 5:
                return TruncateRandom(input);
            case 6:
                return DuplicateRange(input);
            case 7:
                return Splice(input);
            default:
                return input;
        }
    }

    private byte[] FlipBit(byte[] input)
    {
        byte[] result = (byte[])input.Clone();
        int bitIndex = _rng.Next(result.Length * 8);
        result[bitIndex / 8] ^= (byte)(1 << (bitIndex % 8));
        return result;
    }

    private byte[] ReplaceByte(byte[] input)
    {
        byte[] result = (byte[])input.Clone();
        result[_rng.Next(result.Length)] = (byte)_rng.Next(256);
        return result;
    }

    private byte[] InsertByte(byte[] input)
    {
        int idx = _rng.Next(input.Length + 1);
        byte[] result = new byte[input.Length + 1];
        Array.Copy(input, 0, result, 0, idx);
        result[idx] = (byte)_rng.Next(256);
        Array.Copy(input, idx, result, idx + 1, input.Length - idx);
        return result;
    }

    private byte[] DeleteByte(byte[] input)
    {
        if (input.Length <= 1) { return input; }
        int idx = _rng.Next(input.Length);
        byte[] result = new byte[input.Length - 1];
        Array.Copy(input, 0, result, 0, idx);
        Array.Copy(input, idx + 1, result, idx, input.Length - idx - 1);
        return result;
    }

    private byte[] ReplaceWithInterestingInt(byte[] input)
    {
        // Boundary values: 0, 1, -1, INT_MAX, INT_MIN, UINT_MAX, common small numbers
        int[] interesting = { 0, 1, -1, int.MaxValue, int.MinValue, 0x7F, 0x80, 0xFF, 0xFFFF, 0x10000, -128, 127 };
        int value = interesting[_rng.Next(interesting.Length)];
        int width = 1 << _rng.Next(3);    // 1, 2, or 4 bytes
        if (input.Length < width) { return input; }
        int offset = _rng.Next(input.Length - width + 1);
        byte[] result = (byte[])input.Clone();
        for (int i = 0; i < width; i++)
        {
            result[offset + i] = (byte)((value >> (i * 8)) & 0xFF);
        }
        return result;
    }

    private byte[] TruncateRandom(byte[] input)
    {
        if (input.Length <= 1) { return input; }
        int newLength = _rng.Next(1, input.Length);
        byte[] result = new byte[newLength];
        Array.Copy(input, 0, result, 0, newLength);
        return result;
    }

    private byte[] DuplicateRange(byte[] input)
    {
        if (input.Length <= 1) { return input; }
        int start = _rng.Next(input.Length);
        int length = 1 + _rng.Next(Math.Min(input.Length - start, 64));
        byte[] result = new byte[input.Length + length];
        Array.Copy(input, 0, result, 0, start + length);
        Array.Copy(input, start, result, start + length, length);
        Array.Copy(input, start + length, result, start + length + length, input.Length - start - length);
        return result;
    }

    private byte[] Splice(byte[] input)
    {
        if (_corpus.Count < 2) { return input; }
        byte[] other = _corpus[_rng.Next(_corpus.Count)];
        if (other.Length == 0) { return input; }
        int cut = _rng.Next(input.Length);
        int otherCut = _rng.Next(other.Length);
        byte[] result = new byte[cut + (other.Length - otherCut)];
        Array.Copy(input, 0, result, 0, cut);
        Array.Copy(other, otherCut, result, cut, other.Length - otherCut);
        return result;
    }

    private string SaveCrash(byte[] input, Exception ex)
    {
        string hash = ComputeSha256(input);
        string binPath = Path.Combine(_crashesDir, $"{hash}.bin");
        string txtPath = Path.Combine(_crashesDir, $"{hash}.txt");

        if (!File.Exists(binPath))
        {
            File.WriteAllBytes(binPath, input);
            StringBuilder sb = new();
            sb.AppendLine($"Target:       {_target.Name}");
            sb.AppendLine($"Input length: {input.Length} bytes");
            sb.AppendLine($"SHA-256:      {hash}");
            sb.AppendLine($"Exception:    {ex.GetType().FullName}");
            sb.AppendLine($"Message:      {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("Stack trace:");
            sb.AppendLine(ex.ToString());
            File.WriteAllText(txtPath, sb.ToString());
        }
        return hash;
    }

    private static string ComputeSha256(byte[] input)
    {
        byte[] hash = SHA256.HashData(input);
        return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 16);
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) { return s; }
        return s.Substring(0, max - 3) + "...";
    }

    private static List<byte[]> LoadCorpus(string corpusDir)
    {
        List<byte[]> result = new();
        if (!Directory.Exists(corpusDir)) { return result; }
        foreach (string path in Directory.EnumerateFiles(corpusDir, "*.bin"))
        {
            result.Add(File.ReadAllBytes(path));
        }
        return result;
    }
}

/// <summary>Result of a fuzz run.</summary>
internal sealed class FuzzResult
{
    public long Iterations { get; init; }
    public long ExpectedExceptions { get; init; }
    public int CrashCount { get; init; }
    public long ThroughputPerSec { get; init; }
}

/// <summary>Options controlling a fuzz run.</summary>
internal sealed class FuzzOptions
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(60);
    public int Seed { get; set; } = unchecked((int)DateTime.UtcNow.Ticks);
    public int MaxInputSize { get; set; } = 65536;
}
