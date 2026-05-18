// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Cli
// Top-level entry point and command dispatcher.

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Cli;

/// <summary>Entry point and command dispatcher for the chuvadi CLI.</summary>
public static class Program
{
    /// <summary>Process entry point.</summary>
    public static int Main(string[] args)
    {
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    /// Testable entry point: takes the argument vector plus stdout/stderr writers
    /// and returns the exit code without touching the real Console.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        if (stdout is null)
        {
            throw new ArgumentNullException(nameof(stdout));
        }

        if (stderr is null)
        {
            throw new ArgumentNullException(nameof(stderr));
        }

        if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage(stdout);
            return 0;
        }

        string verb = args[0];
        string[] verbArgs = new string[args.Length - 1];
        Array.Copy(args, 1, verbArgs, 0, args.Length - 1);

        ICommand? command = CommandRegistry.Find(verb);

        if (command is null)
        {
            stderr.WriteLine($"chuvadi: unknown command '{verb}'");
            stderr.WriteLine("Run 'chuvadi help' for a list of commands.");
            return 2;
        }

        try
        {
            return command.Run(verbArgs, stdout, stderr);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"chuvadi: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage(TextWriter stdout)
    {
        stdout.WriteLine("chuvadi — PDF toolkit");
        stdout.WriteLine();
        stdout.WriteLine("Usage: chuvadi <command> [args]");
        stdout.WriteLine();
        stdout.WriteLine("User commands:");
        stdout.WriteLine("  info              Show PDF metadata");
        stdout.WriteLine("  render            Rasterize a page to PNG");
        stdout.WriteLine("  watermark         Apply a text watermark");
        stdout.WriteLine("  redact            Apply rectangle-based redactions");
        stdout.WriteLine("  form-fill         Fill AcroForm fields");
        stdout.WriteLine("  extract-text      Extract text from a PDF");
        stdout.WriteLine("  outlines          List bookmark tree");
        stdout.WriteLine("  merge             Combine multiple PDFs");
        stdout.WriteLine("  split             Split a PDF into separate files");
        stdout.WriteLine("  delete            Delete pages from a PDF");
        stdout.WriteLine("  rotate            Rotate a page");
        stdout.WriteLine();
        stdout.WriteLine("Debug commands:");
        stdout.WriteLine("  tokenize          Dump PDF tokens from a content stream");
        stdout.WriteLine("  dump-objects      List indirect objects");
        stdout.WriteLine("  parse-content     Show parsed content stream operators");
        stdout.WriteLine("  decode-stream     Decode a filter (standalone)");
        stdout.WriteLine("  inspect-xref      Show xref table");
        stdout.WriteLine("  validate-fonts    Inspect embedded fonts");
        stdout.WriteLine();
        stdout.WriteLine("Run 'chuvadi <command> --help' for command-specific options.");
    }
}

// ── Command contract ──────────────────────────────────────────────────────

/// <summary>
/// Contract for a CLI subcommand. Implementations parse their argument vector
/// (which excludes the verb itself), perform work, and return an exit code.
/// </summary>
public interface ICommand
{
    /// <summary>Run the command. Returns 0 on success.</summary>
    int Run(string[] args, TextWriter stdout, TextWriter stderr);
}

// ── Command registry ──────────────────────────────────────────────────────

/// <summary>Maps verb names to command instances.</summary>
public static class CommandRegistry
{
    /// <summary>Returns a command for the given verb, or null when not found.</summary>
    public static ICommand? Find(string verb)
    {
        return verb switch
        {
            "info" => new Commands.InfoCommand(),
            "render" => new Commands.RenderCommand(),
            "watermark" => new Commands.WatermarkCommand(),
            "redact" => new Commands.RedactCommand(),
            "form-fill" => new Commands.FormFillCommand(),
            "extract-text" => new Commands.ExtractTextCommand(),
            "outlines" => new Commands.OutlinesCommand(),
            "merge" => new Commands.MergeCommand(),
            "split" => new Commands.SplitCommand(),
            "delete" => new Commands.DeleteCommand(),
            "rotate" => new Commands.RotateCommand(),
            "tokenize" => new Commands.TokenizeCommand(),
            "dump-objects" => new Commands.DumpObjectsCommand(),
            "parse-content" => new Commands.ParseContentCommand(),
            "decode-stream" => new Commands.DecodeStreamCommand(),
            "inspect-xref" => new Commands.InspectXrefCommand(),
            "validate-fonts" => new Commands.ValidateFontsCommand(),
            _ => null,
        };
    }
}

// ── Argument parsing utilities ────────────────────────────────────────────

/// <summary>
/// Minimal POSIX-flavoured argument parser shared by all commands.
/// Supports <c>--key value</c>, <c>--key=value</c>, repeated keys
/// (returned as a list), boolean switches, and positional arguments.
/// </summary>
public static class ArgParser
{
    /// <summary>
    /// Parses an argument vector into positional arguments and a multi-value
    /// option dictionary.
    /// </summary>
    public static ParsedArgs Parse(string[] args)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        List<string> positional = new List<string>();
        Dictionary<string, List<string>> options = new Dictionary<string, List<string>>();
        HashSet<string> flags = new HashSet<string>();

        int i = 0;

        while (i < args.Length)
        {
            string a = args[i];

            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                string key;
                string? value;
                int eq = a.IndexOf('=');

                if (eq > 0)
                {
                    key = a.Substring(2, eq - 2);
                    value = a.Substring(eq + 1);
                }
                else
                {
                    key = a.Substring(2);

                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        value = args[i + 1];
                        i++;
                    }
                    else
                    {
                        value = null;
                    }
                }

                if (value is null)
                {
                    flags.Add(key);
                }
                else
                {
                    if (!options.ContainsKey(key))
                    {
                        options[key] = new List<string>();
                    }

                    options[key].Add(value);
                }
            }
            else
            {
                positional.Add(a);
            }

            i++;
        }

        return new ParsedArgs(positional, options, flags);
    }
}

/// <summary>Result of <see cref="ArgParser.Parse(string[])"/>.</summary>
public sealed class ParsedArgs
{
    /// <summary>Initialises a parsed argument set.</summary>
    public ParsedArgs(
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, List<string>> options,
        IReadOnlySet<string> flags)
    {
        Positional = positional;
        Options = options;
        Flags = flags;
    }

    /// <summary>Positional (non-option) arguments in order.</summary>
    public IReadOnlyList<string> Positional { get; }

    /// <summary>Options keyed by name; each value is a list (to support repeats).</summary>
    public IReadOnlyDictionary<string, List<string>> Options { get; }

    /// <summary>Boolean flags (--key with no value).</summary>
    public IReadOnlySet<string> Flags { get; }

    /// <summary>Returns the single value for an option, or <paramref name="defaultValue"/>.</summary>
    public string? Get(string key, string? defaultValue = null)
    {
        if (Options.TryGetValue(key, out List<string>? list) && list.Count > 0)
        {
            return list[0];
        }

        return defaultValue;
    }

    /// <summary>Returns all values for a repeated option.</summary>
    public IReadOnlyList<string> GetAll(string key)
    {
        if (Options.TryGetValue(key, out List<string>? list))
        {
            return list;
        }

        return new List<string>();
    }

    /// <summary>True when the named flag is present.</summary>
    public bool HasFlag(string key) => Flags.Contains(key);
}
