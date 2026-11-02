# Encrypted fixtures

An Agile-encrypted OOXML package (CFB container), generated once with Chuvadi.Docs 1.6.0
(`SaveTo(path, EncryptionOptions)`, SpinCount 1000):

| File | Password | Content |
|---|---|---|
| sniff-pw.docx | pw | One paragraph, "Sniffed and laid out" |

It is embedded in the test assembly (and in Chuvadi.Layout.Documents.Tests, which links it) and opened only through
a `MemoryStream` (`EncryptedFixtures.Open`).
No test writes or opens an encrypted package through a file path: on Windows, Microsoft Defender scans an encrypted
OOXML file on open by trying the default password, which can take seconds inside the OS before any library code
runs (D-44).
