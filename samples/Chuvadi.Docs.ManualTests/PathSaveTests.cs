using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// D-206 (DOC-18): every Docs save to a path, Document.SaveTo(path), Document.SaveTo(path,
/// EncryptionOptions) and DocxTemplate.Fill(inputPath, outputPath, ...), goes through the helper shared
/// with Chuvadi.Sheets 2.0.1: it replaces the target only when the save succeeded; an existing target
/// keeps its permissions (Unix) or attributes (Windows); a symbolic link is kept; a missing folder
/// and a target that cannot be written fail first, naming the target.
/// </summary>
public static class PathSaveTests
{
    private const string Pw = "S3cret!";

    public static void Run(string outDir)
    {
        var dir = Path.Combine(outDir, "pathsave");
        Directory.CreateDirectory(dir);
        var template = Path.Combine(dir, "template.docx");
        Doc("Hello {{Name}}").SaveTo(template);
        var saves = new (string Name, Action<string> Save)[]
        {
            ("SaveTo(path)", p => Doc("saved").SaveTo(p)),
            ("SaveTo(path, EncryptionOptions)", p => Doc("saved").SaveTo(p, new EncryptionOptions { Password = Pw, SpinCount = 1000 })),
            ("DocxTemplate.Fill(path, path)", p => DocxTemplate.Fill(template, p, new TemplateData().Set("Name", "saved"))),
        };

        foreach (var (name, save) in saves)
        {
            string Target(string label) => Path.Combine(dir, $"{label}-{saves.ToList().FindIndex(s => s.Name == name)}.docx");

            Check($"{name}: creates a new file and overwrites an existing one, leaving no temporary file", () =>
            {
                var t = Target("plain");
                if (File.Exists(t)) File.Delete(t);
                save(t);
                AssertTrue(File.Exists(t), "created");
                File.WriteAllText(t, "old");
                save(t);
                AssertTrue(new FileInfo(t).Length > 3, "overwritten");
                AssertTrue(!Temps(dir).Any(), "no temporary file: " + string.Join(", ", Temps(dir)));
            });

            Check($"{name}: an existing target keeps its permissions (Unix mode) or attributes (Windows)", () =>
            {
                var t = Target("keep");
                save(t);
                if (!OperatingSystem.IsWindows())
                {
                    var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
                    File.SetUnixFileMode(t, mode);
                    save(t);
                    AssertTrue(File.GetUnixFileMode(t) == mode, "mode " + File.GetUnixFileMode(t));
                }
                else
                {
                    File.SetAttributes(t, File.GetAttributes(t) | FileAttributes.NotContentIndexed);
                    save(t);
                    AssertTrue(File.GetAttributes(t).HasFlag(FileAttributes.NotContentIndexed), "attributes " + File.GetAttributes(t));
                }
            });

            Check($"{name}: a symbolic link at the path is kept; the file it points to is written", () =>
            {
                var real = Target("real");
                var link = Target("link");
                save(real);
                File.WriteAllText(real, "old");
                if (File.Exists(link) || new FileInfo(link).LinkTarget is not null) File.Delete(link);
                try { File.CreateSymbolicLink(link, real); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.WriteLine("          (symbolic links not allowed for this user: skipped)");
                    return;
                }
                save(link);
                AssertTrue(new FileInfo(link).LinkTarget is not null, "still a link");
                AssertTrue(new FileInfo(real).Length > 3, "the file it points to was written");
            });

            Check($"{name}: a missing folder fails before anything is written, naming the target", () =>
            {
                var t = Path.Combine(dir, "no-such-folder-" + Guid.NewGuid().ToString("N")[..6], "x.docx");
                Exception? ex = null;
                try { save(t); } catch (Exception e) { ex = e; }
                AssertTrue(ex is DirectoryNotFoundException && ex.Message.Contains(t), ex?.GetType().Name + ": " + ex?.Message);
            });

            Check($"{name}: a folder at the path fails with UnauthorizedAccessException and nothing is written", () =>
            {
                var t = Target("isfolder");
                Directory.CreateDirectory(t);
                Exception? ex = null;
                try { save(t); } catch (Exception e) { ex = e; }
                AssertTrue(ex is UnauthorizedAccessException, ex?.GetType().Name + ": " + ex?.Message);
                AssertTrue(Directory.Exists(t) && !Temps(dir).Any(), "folder untouched, no temporary file");
            });

            Check($"{name}: a read-only target fails with UnauthorizedAccessException and is not changed", () =>
            {
                var t = Target("readonly");
                if (File.Exists(t)) { File.SetAttributes(t, FileAttributes.Normal); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(t, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                File.WriteAllText(t, "old");
                if (OperatingSystem.IsWindows()) File.SetAttributes(t, FileAttributes.ReadOnly);
                else
                {
                    File.SetUnixFileMode(t, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                    if (Environment.UserName == "root")
                    {
                        Console.WriteLine("          (running as root, which may write read-only files: skipped)");
                        File.SetUnixFileMode(t, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                        return;
                    }
                }
                Exception? ex = null;
                try { save(t); } catch (Exception e) { ex = e; }
                AssertTrue(ex is UnauthorizedAccessException, ex?.GetType().Name + ": " + ex?.Message);
                AssertTrue(File.ReadAllText(t) == "old", "unchanged");
                if (OperatingSystem.IsWindows()) File.SetAttributes(t, FileAttributes.Normal);
                else File.SetUnixFileMode(t, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            });
        }

        Check("a failing save leaves the existing file untouched (SaveTo(path) and the encrypted save)", () =>
        {
            var bad = new Document();
            bad.AddParagraph().Image(ImageSpec.Inline(ErrorContractMatrix.Png1x1, "image/png", double.NaN, 10));
            foreach (var (label, act) in new (string, Action<string>)[]
            {
                ("plain", p => bad.SaveTo(p)),
                ("encrypted", p => bad.SaveTo(p, new EncryptionOptions { Password = Pw, SpinCount = 1000 })),
            })
            {
                var t = Path.Combine(dir, $"failing-{label}.docx");
                File.WriteAllText(t, "old");
                AssertThrows<ArgumentOutOfRangeException>(() => act(t), label);
                AssertTrue(File.ReadAllText(t) == "old", label + ": unchanged");
            }
            AssertTrue(!Temps(dir).Any(), "no temporary file");
        });
    }

    private static Document Doc(string text)
    {
        var d = new Document();
        d.AddParagraph(text);
        return d;
    }

    private static IEnumerable<string> Temps(string dir) => Directory.GetFiles(dir, ".*.tmp").Select(Path.GetFileName)!;
}
