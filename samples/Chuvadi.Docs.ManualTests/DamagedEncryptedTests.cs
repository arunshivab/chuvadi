using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Docs.Model;
using Chuvadi.Internal.Crypto;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.1.0 (D-300): an encrypted container that is damaged, opened WITH a password, is a malformed file
/// (<see cref="DocxFormatException"/>) in every entry point, as in Chuvadi.Sheets; a missing or wrong password
/// is still <see cref="DocxPasswordRequiredException"/>. Also: <see cref="DocxReader.Open(Stream, string?)"/>'s
/// format error carries the cause's message (the Zip chat's D-293 item 1).
/// </summary>
public static class DamagedEncryptedTests
{
    private const string Pw = "S3cret!";

    private static byte[] Encrypted()
    {
        var d = new Document();
        d.AddParagraph("Hello {{Name}}");
        var ms = new MemoryStream();
        d.SaveTo(ms, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
        return ms.ToArray();
    }

    /// <summary>The encrypted file cut short after its header and one sector: the signature is intact, the container is not.</summary>
    private static byte[] Truncated()
    {
        var full = Encrypted();
        return full.AsSpan(0, 1024).ToArray();
    }

    /// <summary>A well-formed compound file holding only the EncryptionInfo stream of a real encrypted file.</summary>
    private static byte[] OnlyEncryptionInfo()
    {
        var streams = CfbContainer.Read(new MemoryStream(Encrypted()));
        var ms = new MemoryStream();
        CfbContainer.Write(ms, new List<CfbContainer.Node>
        {
            new() { Name = "EncryptionInfo", Type = CfbContainer.TypeStream, Data = streams["EncryptionInfo"] },
        });
        return ms.ToArray();
    }

    private static readonly (string Name, Action<byte[], string?> Run)[] EntryPoints =
    {
        ("DocxReader.Open", (b, pw) => DocxReader.Open(new MemoryStream(b), pw).Dispose()),
        ("Document.Load", (b, pw) => Document.Load(new MemoryStream(b), pw)),
        ("DocxTemplate.Fill", (b, pw) => DocxTemplate.Fill(new MemoryStream(b), new MemoryStream(), new TemplateData(), new TemplateOptions { Password = pw })),
        ("DocxTemplate.GetPlaceholders", (b, pw) => DocxTemplate.GetPlaceholders(new MemoryStream(b), pw)),
        ("DocxTemplate.CheckTemplate", (b, pw) => DocxTemplate.CheckTemplate(new MemoryStream(b), pw)),
    };

    private static string Each(byte[] bytes, string? password, Func<Exception?, bool> ok)
    {
        var wrong = new List<string>();
        foreach (var (name, run) in EntryPoints)
        {
            var ex = Catch(() => run(bytes, password));
            if (!ok(ex)) wrong.Add($"{name}: {ex?.GetType().Name ?? "no exception"}: {ex?.Message}");
        }
        return string.Join("; ", wrong);
    }

    private static Exception? Catch(Action a)
    {
        try { a(); return null; } catch (Exception e) { return e; }
    }

    public static void Run(string outDir)
    {
        Check("D-300: a damaged encrypted container opened with a password is DocxFormatException in all five entry points (was DocxPasswordRequiredException)", () =>
        {
            var bad = Each(Truncated(), Pw, ex => ex is DocxFormatException && ex is not DocxSecurityLimitException
                && ex.Message.Contains("could not be parsed", StringComparison.Ordinal));
            AssertTrue(bad.Length == 0, bad);
        });

        Check("D-300: an encrypted container without its EncryptedPackage stream, opened with a password, is DocxFormatException naming the stream", () =>
        {
            var bad = Each(OnlyEncryptionInfo(), Pw, ex => ex is DocxFormatException && ex.Message.Contains("EncryptedPackage", StringComparison.Ordinal));
            AssertTrue(bad.Length == 0, bad);
        });

        Check("unchanged: the damaged container without a password still asks for the password (the container is not parsed then)", () =>
        {
            var bad = Each(Truncated(), null, ex => ex is DocxPasswordRequiredException);
            AssertTrue(bad.Length == 0, bad);
        });

        Check("unchanged: an intact encrypted file with a wrong password is DocxPasswordRequiredException, and the right password opens it", () =>
        {
            var bytes = Encrypted();
            var bad = Each(bytes, "wrong", ex => ex is DocxPasswordRequiredException && ex.Message == "Incorrect password.");
            AssertTrue(bad.Length == 0, bad);
            AssertContains(Document.Load(new MemoryStream(bytes), Pw).GetText(), "Hello", "right password");
        });

        Check("DocxReader.Open's format error carries the cause's message after its own text", () =>
        {
            var ex = Catch(() => DocxReader.Open(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })).Dispose());
            AssertTrue(ex is DocxFormatException && ex.InnerException is not null, ex?.GetType().Name ?? "no exception");
            AssertTrue(ex!.Message == "Could not open the file as a docx package: " + ex.InnerException!.Message, ex.Message);
        });
    }
}
