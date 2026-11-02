using System.Reflection;

namespace Chuvadi.Layout.Tests;

/// <summary>
/// The encrypted fixtures (Fixtures/encrypted, embedded in the test assembly) as in-memory streams. Encrypted packages
/// are never written to or opened from a file path in these tests: on Windows, Microsoft Defender scans an encrypted
/// OOXML file on open by trying the default password, which can take seconds before any library code runs (D-44).
/// Linked into the Sheets and Documents test projects.
/// </summary>
internal static class EncryptedFixtures
{
    public static MemoryStream Open(string name)
    {
        Assembly asm = typeof(EncryptedFixtures).Assembly;
        string resource = asm.GetName().Name + ".Encrypted." + name;
        using Stream s = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing embedded fixture " + resource);
        MemoryStream ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
}
