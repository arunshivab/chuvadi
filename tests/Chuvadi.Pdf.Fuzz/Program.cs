// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.3 — Fuzzing harness

using System;
using System.IO;

namespace Chuvadi.Pdf.Fuzz;

/// <summary>
/// Entry point for the Chuvadi fuzz harness.
/// </summary>
/// <remarks>
/// <para>
/// Usage:
/// </para>
/// <code>
/// dotnet run -c Release -- &lt;target&gt; [--duration SECONDS] [--seed N] [--max-input-size BYTES]
/// </code>
/// <para>
/// Available targets are listed via <c>dotnet run -c Release -- --list</c>.
/// </para>
/// <para>
/// The harness is a random-input fuzzer (not coverage-guided). For each iteration it
/// picks a corpus seed, applies a randomly-chosen mutation, and feeds the mutated bytes
/// to the target. Exceptions on the target's documented exception type list are treated
/// as cleanly-rejected input. Any other exception is recorded as a crash, with the
/// input bytes saved to <c>crashes/&lt;target&gt;/&lt;sha256&gt;.bin</c> for triage.
/// </para>
/// <para>
/// For background on what this fuzzer does and does not catch, see
/// <c>tests/Chuvadi.Pdf.Fuzz/README.md</c>.
/// </para>
/// </remarks>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] == "--list")
        {
            foreach (string name in FuzzTargets.Names)
            {
                Console.WriteLine(name);
            }
            return 0;
        }

        if (args[0] == "--regenerate-corpus")
        {
            string repoRoot = FindRepoRoot();
            string corpusRoot = Path.Combine(repoRoot, "tests", "Chuvadi.Pdf.Fuzz", "corpus");
            Console.WriteLine($"Regenerating corpus under {corpusRoot}");
            CorpusGenerator.RegenerateAll(corpusRoot);
            Console.WriteLine("Done.");
            return 0;
        }

        string targetName = args[0];
        FuzzOptions options = ParseOptions(args);

        if (!FuzzTargets.TryGet(targetName, out IFuzzTarget? target))
        {
            Console.Error.WriteLine($"Unknown target '{targetName}'. Use --list to see available targets.");
            return 1;
        }

        string corpusDir = Path.Combine(AppContext.BaseDirectory, "corpus", targetName);
        string crashesDir = Path.Combine(Environment.CurrentDirectory, "crashes", targetName);
        Directory.CreateDirectory(crashesDir);

        Console.WriteLine($"Fuzzing target: {targetName}");
        Console.WriteLine($"Corpus:         {corpusDir}");
        Console.WriteLine($"Crashes:        {crashesDir}");
        Console.WriteLine($"Duration:       {options.Duration.TotalSeconds:F0} seconds");
        Console.WriteLine($"Seed:           {options.Seed}");
        Console.WriteLine($"Max input size: {options.MaxInputSize} bytes");
        Console.WriteLine();

        FuzzRunner runner = new(target!, corpusDir, crashesDir, options);
        FuzzResult result = runner.Run();

        Console.WriteLine();
        Console.WriteLine("─── Summary ──────────────────────────────────────");
        Console.WriteLine($"Iterations:     {result.Iterations:N0}");
        Console.WriteLine($"Throughput:     {result.ThroughputPerSec:N0} iter/sec");
        Console.WriteLine($"Expected throws: {result.ExpectedExceptions:N0}");
        Console.WriteLine($"Crashes found:  {result.CrashCount}");

        return result.CrashCount > 0 ? 77 : 0;   // exit 77 = crashes found (libFuzzer-compatible)
    }

    private static FuzzOptions ParseOptions(string[] args)
    {
        FuzzOptions options = new();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--duration":
                    options.Duration = TimeSpan.FromSeconds(int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--seed":
                    options.Seed = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--max-input-size":
                    options.MaxInputSize = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option '{args[i]}'.");
                    Environment.Exit(1);
                    break;
            }
        }
        return options;
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "Chuvadi.slnx")) ||
                File.Exists(Path.Combine(dir, "Chuvadi.sln")))
            {
                return dir;
            }
            string? parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) { break; }
            dir = parent;
        }
        throw new InvalidOperationException("Could not locate repo root (no Chuvadi.slnx found in ancestors).");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Chuvadi Fuzz Harness");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -c Release -- <target> [options]");
        Console.WriteLine("  dotnet run -c Release -- --list");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --duration SECONDS    Run duration in seconds (default 60)");
        Console.WriteLine("  --seed N              PRNG seed for reproducible runs (default time-based)");
        Console.WriteLine("  --max-input-size N    Maximum input size in bytes (default 65536)");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0    No crashes found");
        Console.WriteLine("  77   Crashes found (input bytes saved under crashes/<target>/)");
        Console.WriteLine("  1    Invalid arguments");
    }
}
