# Chuvadi

**Pure-BCL document libraries for .NET — zero NuGet dependencies.**

*Chuvadi* (சுவடி) is the Tamil word for a palm-leaf manuscript bundle — documents, preserved carefully. This monorepo is a family of independent NuGet packages that read and write document formats using nothing but the .NET Base Class Library: a supply chain you can audit in one sitting.

| Package | Formats | Status |
|---|---|---|
| **[Chuvadi.Sheets](src/Chuvadi.Sheets/README.md)** | xlsx (read/write/encrypt), zip | v1.1.1 |
| **[Chuvadi.Docs](src/Chuvadi.Docs/README.md)** | docx (read/write/encrypt/templates) | v1.0.0 |
| Chuvadi.Pdf | pdf | planned |

## Shared foundation

Both OOXML packages source-link the same internals from [`shared/Chuvadi.Internal`](shared/Chuvadi.Internal): OPC packaging, and the full [MS-OFFCRYPTO] Agile Encryption stack — AES-256-CBC, iterated SHA-512 key derivation, **HMAC-SHA512 verified on every decrypt**, CFB container with DIFAT support, plus hardening utilities (decompression caps, bounded streams). A security fix lands once and ships in every package; each DLL remains a single, zero-dependency assembly.

## Quick taste

```csharp
// Excel
var wb = new Workbook();
wb.Sheet("Q2").Cell("A1").Value("Revenue").Bold();
wb.SaveTo("q2.xlsx", new EncryptionOptions { Password = "secret" });

// Word
var doc = new Document();
doc.AddParagraph("Quarterly Report", ParagraphStyle.Title);
doc.SaveTo("report.docx");

// Word templates: design in Word, fill from code
DocxTemplate.Fill("invoice-template.docx", "invoice-0042.docx",
    new Dictionary<string, string> { ["Total"] = "Rs 54,000.00" });
```

## Verification

Every package ships with an xUnit suite plus an end-to-end manual verification suite, both run in CI on Ubuntu and Windows. Generated files are cross-validated against independent implementations (openpyxl, python-docx, msoffcrypto-tool). CI also greps the shipped projects to enforce the zero-dependency promise. See [SECURITY.md](SECURITY.md) and [AUDIT_REPORT.md](AUDIT_REPORT.md).

## Releases

Per-package tags: `sheets-v1.1.1`, `docs-v1.0.0`. See [RELEASING.md](RELEASING.md).

## License

MIT.
