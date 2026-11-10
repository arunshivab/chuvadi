using System;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Fonts;
using Chuvadi.Zip;

namespace Chuvadi.AndroidSmoke;

/// <summary>Uses Chuvadi.Zip and Chuvadi.Fonts so that compiling this assembly proves the Android target can reference them.</summary>
public static class AndroidSmoke
{
    /// <summary>Zips the first embedded font and reads it back; returns true when the bytes match.</summary>
    public static bool RoundTrip()
    {
        var first = FontPack.Files.First();
        var font = FontPack.ReadAllBytes(first.FileName);
        var zip = ZipWriter.ToBytes(w =>
        {
            using var e = w.CreateEntry("font.ttf");
            e.Write(font, 0, font.Length);
            using var t = w.CreateEntry("note.txt", new ZipEntryOptions { Encryption = ZipEncryption.Aes256, Password = "secret" });
            var b = Encoding.UTF8.GetBytes("android");
            t.Write(b, 0, b.Length);
        });
        using var r = ZipReader.Open(zip, new ZipReaderOptions { Password = "secret" });
        return r.ReadAllBytes(r.FindEntry("font.ttf")!).AsSpan().SequenceEqual(font) && r.ReadAllText(r.FindEntry("note.txt")!) == "android";
    }
}
