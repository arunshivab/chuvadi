# Encrypted fixtures

Agile-encrypted OOXML packages (CFB containers), generated once with Chuvadi.Sheets 1.7.0
(`SaveTo(path, EncryptionOptions)`, SpinCount 1000):

| File | Password | Content |
|---|---|---|
| sample-pw.xlsx | pw | `SheetsImportTests.Sample()` |
| sample-secret.xlsx | secret | `SheetsImportTests.Sample()` |

They are embedded in the test assembly (sample-pw.xlsx also in Chuvadi.Layout.Documents.Tests, which links it) and
opened only through a `MemoryStream` (`EncryptedFixtures.Open`).
No test writes or opens an encrypted package through a file path: on Windows, Microsoft Defender scans an encrypted
OOXML file on open by trying the default password, which can take seconds inside the OS before any library code
runs (D-44).
