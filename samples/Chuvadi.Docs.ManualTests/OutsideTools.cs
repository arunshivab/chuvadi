using System;
using System.Diagnostics;
using System.IO;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Outside tools used as a cross-check in the tests only, never in shipped code (D-163, D-164).
/// A check runs when the tool is installed and prints that it was skipped otherwise (D-204, as
/// Chuvadi.Sheets' SH-2 (a)). msoffcrypto-tool decrypts in memory: the file goes in on stdin and the
/// package comes back on stdout, so no encrypted file is opened from disk (D-44).
/// </summary>
public static class OutsideTools
{
    private const string MsoffcryptoScript =
        "import sys,io,msoffcrypto\n" +
        "f=msoffcrypto.OfficeFile(io.BytesIO(sys.stdin.buffer.read()))\n" +
        "f.load_key(password=sys.argv[1])\n" +
        "o=io.BytesIO()\n" +
        "f.decrypt(o)\n" +
        "sys.stdout.buffer.write(o.getvalue())\n";

    private static bool? _haveMsoffcrypto;
    private static string? _python;

    /// <summary>True when Python with the msoffcrypto package is installed (checked once).</summary>
    public static bool MsoffcryptoAvailable()
    {
        if (_haveMsoffcrypto is bool known) return known;
        foreach (var candidate in new[] { "python3", "python", "py" })
        {
            try
            {
                var (code, _) = Run(candidate, new[] { "-I", "-c", "import msoffcrypto" }, Array.Empty<byte>());
                if (code == 0) { _python = candidate; _haveMsoffcrypto = true; return true; }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        _haveMsoffcrypto = false;
        return false;
    }

    /// <summary>Decrypts an encrypted Office file with msoffcrypto-tool, in memory.</summary>
    public static byte[] MsoffcryptoDecrypt(byte[] file, string password)
    {
        if (!MsoffcryptoAvailable()) throw new InvalidOperationException("msoffcrypto-tool is not installed.");
        var (code, output) = Run(_python!, new[] { "-I", "-c", MsoffcryptoScript, password }, file);
        if (code != 0) throw new InvalidOperationException($"msoffcrypto-tool failed (exit {code}).");
        return output;
    }

    /// <summary>
    /// Checks with msoffcrypto-tool, when installed, that <paramref name="encrypted"/> decrypts to
    /// the same package as <paramref name="expectedPackage"/> (zip entry times aside). Prints the result.
    /// </summary>
    public static void CheckWithMsoffcrypto(byte[] encrypted, string password, byte[] expectedPackage, string what)
    {
        if (!MsoffcryptoAvailable())
        {
            Console.WriteLine($"          msoffcrypto-tool: not installed, outside check skipped ({what})");
            return;
        }
        var external = MsoffcryptoDecrypt(encrypted, password);
        if (PackageCompare.Difference(external, expectedPackage) is { } difference)
            throw new InvalidOperationException($"{what}: msoffcrypto-tool decrypts to a different package ({difference}).");
        Console.WriteLine($"          msoffcrypto-tool: decrypted, identical package ({what})");
    }

    private static (int Code, byte[] Output) Run(string exe, string[] args, byte[] input)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("no process");
        var stdout = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
        var err = p.StandardError.ReadToEndAsync();
        p.StandardInput.BaseStream.Write(input);
        p.StandardInput.Close();
        copy.GetAwaiter().GetResult();
        err.GetAwaiter().GetResult();
        p.WaitForExit();
        return (p.ExitCode, stdout.ToArray());
    }
}
