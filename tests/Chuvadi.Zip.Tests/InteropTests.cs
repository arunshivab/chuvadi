using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Chuvadi.Zip.Tests
{
    /// <summary>
    /// Cross-checks against independent implementations (Info-ZIP unzip/zip, Python zipfile, pyzipper).
    /// A test silently passes when its tool is not installed so CI on any OS stays green.
    /// </summary>
    public class InteropTests
    {
        private static (int Exit, string Output) Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, o);
            }
            catch (System.ComponentModel.Win32Exception) { return (-999, "missing"); }
        }

        private static bool Has(string tool, params string[] probe) => Run(tool, probe).Exit != -999;
        private static bool HasPython() => Run("python3", "-c", "print(1)").Exit == 0;
        private static bool HasPyzipper() => HasPython() && Run("python3", "-c", "import pyzipper").Exit == 0;

        private static byte[] Sample(int n, int seed)
        {
            var b = new byte[n];
            new Random(seed).NextBytes(b);
            for (int i = 0; i < n; i += 3) b[i] = 65;            // semi-compressible
            return b;
        }

        private static string Tmp() { var d = Path.Combine(Path.GetTempPath(), "czip-interop-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

        private const string PyCheck = @"
import sys, zipfile, hashlib
sys.stdout.reconfigure(encoding='utf-8')   # Windows consoles default to cp1252; -I ignores PYTHONIOENCODING
z = zipfile.ZipFile(sys.argv[1])
bad = z.testzip()
if bad: print('BAD', bad); sys.exit(2)
for i in z.infolist():
    print(i.filename, i.file_size, hashlib.sha256(z.read(i.filename)).hexdigest() if not i.is_dir() else '-', i.comment.decode('utf-8','replace'), repr(z.comment.decode('utf-8','replace')))
";

        [Fact]
        public void Python_and_unzip_accept_our_archives()
        {
            if (!HasPython()) return;
            var dir = Tmp();
            try
            {
                var data = Sample(200_000, 1);
                foreach (var (label, opts, seekable) in new[]
                {
                    ("default", new ZipWriterOptions(), true),
                    ("zip64", new ZipWriterOptions { Zip64 = Zip64Mode.Always }, true),
                    ("descriptor", new ZipWriterOptions(), false),
                    ("descriptor64", new ZipWriterOptions { Zip64 = Zip64Mode.Always }, false),
                    ("stored", new ZipWriterOptions { DefaultMethod = ZipCompressionMethod.Stored }, true),
                })
                {
                    var path = Path.Combine(dir, label + ".zip");
                    using (var fs = seekable ? (Stream)new FileStream(path, FileMode.Create) : new Chuvadi.Zip.ManualTests.ForwardOnlyFileWriter(path))
                    using (var w = ZipWriter.Create(fs, opts))
                    {
                        w.Comment = "archive ünï";
                        w.AddBytes("data.bin", data, new ZipEntryOptions { Comment = "c1" });
                        w.AddText("日本語/ñ.txt", "héllo");
                        w.AddDirectory("emptydir");
                        w.AddBytes("empty.bin", ReadOnlySpan<byte>.Empty);
                    }
                    var (exit, output) = Run("python3", "-I", "-c", PyCheck, path);
                    Assert.True(exit == 0, label + ": " + output);
                    var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
                    Assert.Contains("data.bin 200000 " + sha, output);
                    Assert.Contains("日本語/ñ.txt", output);
                    if (Has("unzip", "-v"))
                    {
                        var (e2, o2) = Run("unzip", "-t", path);
                        Assert.True(e2 == 0, label + " unzip -t: " + o2);
                    }
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Pyzipper_decrypts_our_aes_archives()
        {
            if (!HasPyzipper()) return;
            var dir = Tmp();
            try
            {
                var data = Sample(150_000, 2);
                foreach (var enc in new[] { ZipEncryption.Aes128, ZipEncryption.Aes192, ZipEncryption.Aes256 })
                foreach (var ver in new[] { ZipAesVersion.Ae1, ZipAesVersion.Ae2 })
                foreach (var seekable in new[] { true, false })
                {
                    var path = Path.Combine(dir, $"{enc}-{ver}-{seekable}.zip");
                    using (var fs = seekable ? (Stream)new FileStream(path, FileMode.Create) : new Chuvadi.Zip.ManualTests.ForwardOnlyFileWriter(path))
                    using (var w = ZipWriter.Create(fs, new ZipWriterOptions { Password = "pässwörd", Encryption = enc, AesVersion = ver }))
                    {
                        w.AddBytes("data.bin", data);
                        w.AddText("t.txt", "text");
                    }
                    var (exit, output) = Run("python3", "-I", "-c",
                        "import sys,pyzipper,hashlib\nsys.stdout.reconfigure(encoding='utf-8')\nz=pyzipper.AESZipFile(sys.argv[1])\nz.setpassword('pässwörd'.encode('utf-8'))\nprint(hashlib.sha256(z.read('data.bin')).hexdigest(), z.read('t.txt').decode())\n", path);
                    Assert.True(exit == 0, path + ": " + output);
                    Assert.Contains(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant() + " text", output);
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void We_read_python_archives_including_zip64_and_stored()
        {
            if (!HasPython()) return;
            var dir = Tmp();
            try
            {
                var path = Path.Combine(dir, "py.zip");
                var script = @"
import sys, zipfile
p = sys.argv[1]
with zipfile.ZipFile(p, 'w') as z:
    z.writestr('deflated.txt', b'abc' * 50000, compress_type=zipfile.ZIP_DEFLATED)
    z.writestr('stored.txt', b'plain', compress_type=zipfile.ZIP_STORED)
    z.writestr(zipfile.ZipInfo('dir/'), b'')
    zi = zipfile.ZipInfo('zip64.bin', date_time=(2019, 3, 4, 5, 6, 8))
    zi.compress_type = zipfile.ZIP_DEFLATED
    zi.create_system = 3                 # Unix; Python on Windows defaults to 0 (DOS), where the high attribute bits are not a mode
    zi.external_attr = 0o100755 << 16
    zi.comment = b'py comment'
    with z.open(zi, 'w', force_zip64=True) as f:
        f.write(b'z' * 123456)
    z.writestr('uni-é.txt', 'unicode name')
    z.comment = b'top comment'
";
                Assert.Equal(0, Run("python3", "-I", "-c", script, path).Exit);
                using var r = ZipReader.Open(path);
                r.Verify();
                Assert.Equal("top comment", r.Comment);
                Assert.Equal(new string('a', 0) + string.Concat(Enumerable.Repeat("abc", 50000)), r.ReadAllText(r.FindEntry("deflated.txt")!));
                Assert.Equal("plain", r.ReadAllText(r.FindEntry("stored.txt")!));
                Assert.True(r.FindEntry("dir/")!.IsDirectory);
                var z64 = r.FindEntry("zip64.bin")!;
                Assert.Equal(123456, z64.Size);
                Assert.Equal("py comment", z64.Comment);
                Assert.Equal(0x1ED, z64.UnixMode!.Value & 0xFFF);
                Assert.Equal("unicode name", r.ReadAllText(r.FindEntry("uni-é.txt")!));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void We_read_pyzipper_aes_archives()
        {
            if (!HasPyzipper()) return;
            var dir = Tmp();
            try
            {
                foreach (var bits in new[] { 128, 192, 256 })
                {
                    var path = Path.Combine(dir, $"aes{bits}.zip");
                    var script = $@"
import sys, pyzipper
with pyzipper.AESZipFile(sys.argv[1], 'w', compression=pyzipper.ZIP_DEFLATED, encryption=pyzipper.WZ_AES) as z:
    z.setpassword('secrét'.encode('utf-8'))
    z.setencryption(pyzipper.WZ_AES, nbits={bits})
    z.writestr('a.txt', b'hello aes ' * 20000)
    z.writestr('b.bin', bytes(range(256)) * 10)
";
                    Assert.Equal(0, Run("python3", "-I", "-c", script, path).Exit);
                    using var r = ZipReader.Open(path, new ZipReaderOptions { Password = "secrét" });
                    Assert.All(r.Entries, e => Assert.Equal(bits == 128 ? ZipEncryption.Aes128 : bits == 192 ? ZipEncryption.Aes192 : ZipEncryption.Aes256, e.Encryption));
                    Assert.Equal(string.Concat(Enumerable.Repeat("hello aes ", 20000)), r.ReadAllText(r.FindEntry("a.txt")!));
                    Assert.Equal(Enumerable.Range(0, 2560).Select(i => (byte)(i % 256)).ToArray(), r.ReadAllBytes(r.FindEntry("b.bin")!));
                    r.Verify();
                    Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.Entries[0], "wrong"));
                }
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void We_read_infozip_archives_including_zipcrypto()
        {
            if (!Has("zip", "-v") || !Has("unzip", "-v")) return;
            var dir = Tmp();
            try
            {
                var src = Path.Combine(dir, "src");
                Directory.CreateDirectory(Path.Combine(src, "sub"));
                var big = Sample(300_000, 3);
                File.WriteAllBytes(Path.Combine(src, "big.bin"), big);
                File.WriteAllText(Path.Combine(src, "sub", "t.txt"), "text file");

                var plain = Path.Combine(dir, "plain.zip");
                Assert.Equal(0, RunIn(src, "zip", "-r", "-q", plain, ".").Exit);
                using (var r = ZipReader.Open(plain))
                {
                    r.Verify();
                    Assert.Equal(big, r.ReadAllBytes(r.FindEntry("big.bin") ?? r.FindEntry("./big.bin")!));
                    Assert.Equal("text file", r.ReadAllText(r.FindEntry("sub/t.txt")!));
                }

                var enc = Path.Combine(dir, "enc.zip");
                Assert.Equal(0, RunIn(src, "zip", "-r", "-q", "-P", "hunter2", enc, ".").Exit);
                using (var r = ZipReader.Open(enc, new ZipReaderOptions { Password = "hunter2" }))
                {
                    Assert.All(r.Entries.Where(e => !e.IsDirectory), e => Assert.Equal(ZipEncryption.ZipCrypto, e.Encryption));
                    Assert.Equal(big, r.ReadAllBytes(r.FindEntry("big.bin")!));
                    r.Verify();
                    Assert.Throws<ZipPasswordRequiredException>(() => r.ReadAllBytes(r.FindEntry("big.bin")!, "bad"));
                }

                var z64 = Path.Combine(dir, "z64.zip");
                Assert.Equal(0, RunIn(src, "zip", "-r", "-q", "-fz", z64, ".").Exit);
                using (var r = ZipReader.Open(z64))
                {
                    r.Verify();
                    Assert.Equal(big, r.ReadAllBytes(r.FindEntry("big.bin")!));
                }

                // and Info-ZIP unzip extracts a tree we zipped
                var ours = Path.Combine(dir, "ours.zip");
                src.ZipDirectory(ours);
                var outDir = Path.Combine(dir, "unz");
                Assert.Equal(0, Run("unzip", "-q", ours, "-d", outDir).Exit);
                Assert.Equal(big, File.ReadAllBytes(Path.Combine(outDir, "big.bin")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static (int Exit, string Output) RunIn(string workDir, string file, params string[] args)
        {
            var psi = new ProcessStartInfo(file) { WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, o);
        }
    }
}
