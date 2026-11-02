# Encrypted fixtures

Agile-encrypted OOXML packages (CFB containers), generated once with Chuvadi.Docs 1.6.0 and Chuvadi.Sheets 1.7.0
(`SaveTo(path, EncryptionOptions)`, SpinCount 1000):

| File | Password | Content |
|---|---|---|
| sniff-pw.docx | pw | One paragraph, "Sniffed and laid out" |
| sample-pw.xlsx | pw | `XlsxImportTests.Sample()` |
| sample-secret.xlsx | secret | `XlsxImportTests.Sample()` |

They are embedded in the test assembly and opened only through a `MemoryStream` (`DocxImportTests.Encrypted`).
No test writes or opens an encrypted package through a file path: on Windows, Microsoft Defender scans an encrypted
OOXML file on open by trying the default password, which can take seconds inside the OS before any library code
runs (D-44).
