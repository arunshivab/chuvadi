# Chuvadi PDF - virtual printer

"Chuvadi PDF" appears as a printer in every Windows application. Printing to it produces a PDF file,
records the job in an audit trail and, optionally, also prints it on a physical printer.

| Windows | What gets installed | Printers you see |
|---|---|---|
| Windows 11 24H2 or later (build 26100+) | Print Support App package + agent service | **Chuvadi PDF** (asks where to save), **Chuvadi PDF (Auto-save)** (saves to the configured folder) |
| Windows 10, or any Windows | Compatibility queue + agent service | **Chuvadi PDF** (saves to the configured folder) |

No printer driver, port monitor or print processor is installed. The compatibility queue uses Windows' own
"Microsoft Print To PDF" driver; the Windows 11 printers use Windows' own XPS-to-PDF converter.

## Requirements

- .NET 10 runtime (x64), machine-wide: https://dotnet.microsoft.com/download/dotnet/10.0
- Administrator rights to install.
- To build the Windows 11 package: .NET 10 SDK and the Windows 11 SDK 10.0.26100 or later
  (Visual Studio Installer, "Windows 11 SDK").

## 1. Build (on the build machine)

PowerShell, from `products\ChuvadiPdfPrinter`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```
Builds the agent into `out\agent`. For the Windows 11 package as well:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Psa
```
This also creates `out\psa\ChuvadiPdf.msix`, signed with a self-signed certificate (`CN=Chuvadi`, created
once in your certificate store), and `out\psa\ChuvadiPdf.cer`.

## 2. Install (on each PC, elevated PowerShell)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```
`-Mode Auto` (default) picks the Windows 11 printers when the PC is 24H2+ and the package was built, and
the compatibility queue otherwise. Force a mode with `-Mode Queue`, `-Mode Psa` or `-Mode Both`. Choose
where PDFs go with `-OutputFolder "D:\PDF"` (default `%PUBLIC%\Documents\Chuvadi PDF`).

To install on another PC, copy the whole `products\ChuvadiPdfPrinter` folder (with `out`) and run the same command.

## 3. Self-test (as a normal user, not elevated)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\selftest.ps1
```
Prints a test page through each installed printer that can be tested unattended (the queue and
"Chuvadi PDF (Auto-save)"), waits for the PDF, checks it and the audit trail, and prints the path so you can
open it. "SELF-TEST PASSED" means the whole chain works. "Chuvadi PDF" (Save As) needs a person; print
any document to it and choose where to save.

## Where things are

| What | Where |
|---|---|
| PDFs | `<OutputFolder>\<user name>\<yyyy-MM-dd HHmmss> <document title>.pdf` - each user's folder is readable only by that user, SYSTEM and Administrators |
| Settings | `%ProgramData%\Chuvadi\PdfPrinter\config.json` |
| Audit trail | `%ProgramData%\Chuvadi\PdfPrinter\audit.log` - one line per job: time, user, machine, title, pages, result |
| Diagnostics | `%ProgramData%\Chuvadi\PdfPrinter\agent.log` |
| Agent | `%ProgramFiles%\Chuvadi\PdfPrinter\ChuvadiPdfAgent.exe` (service "Chuvadi PDF Agent") |

## Settings (config.json)

Edit, then `Restart-Service ChuvadiPdfAgent`. Check a change with
`& "$env:ProgramFiles\Chuvadi\PdfPrinter\ChuvadiPdfAgent.exe" --validate`.

| Setting | Meaning |
|---|---|
| `OutputFolder` | Where PDFs are saved (environment variables allowed). |
| `PerUserSubfolder` | `true`: one private folder per user. `false`: everyone's PDFs in one folder (shared). |
| `FileNamePattern` | Tokens `{title}` `{user}` `{machine}` `{job}` `{date}` `{date:yyyyMMdd}`. Keep `{title}` for the self-test. |
| `Collision` | `AppendNumber` (default: "name (2).pdf"), `Overwrite`, or `Fail`. |
| `ForwardTo` | Also print every PDF on these printers - see below. |
| `MaxJobBytes` | Largest job accepted (default 2 GB). |

**Print and also send to a physical printer**: add entries to `ForwardTo`:
```json
"ForwardTo": [
  { "Kind": "Ipp", "Printer": "ipp://10.0.0.21/ipp/print", "Copies": 1, "Duplex": "Vertical" }
]
```
- `"Kind": "Ipp"` works now with any network printer that accepts PDF over IPP (most printers since about
  2015, "IPP Everywhere" / "AirPrint"); the PDF is passed through unchanged.
- `"Kind": "WindowsPrinter"` with a Windows printer name is supported by the code but needs the PDF renderer
  (Chuvadi.Print.Pdf), which is not part of this release; until then such jobs are saved normally and the
  forward is recorded as failed with that reason.

## Uninstall (elevated)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
```
Removes the printers, port, package, service and program files. Saved PDFs are never deleted. Settings and
the audit trail are kept unless you add `-RemoveData`; `-RemoveCertificate` also removes the package certificate.

## Troubleshooting

- **Self-test says the agent is not running**: `Get-Service ChuvadiPdfAgent`; read `agent.log`.
- **Queue jobs stay in the queue with an error**: the agent must be running (the queue's port is its pipe).
  Jobs wait and retry; start the service and they continue.
- **Windows 11 printers do not appear**: the package needs build 26100+, and its certificate must be trusted
  (`install.ps1` imports it). Event Viewer: Applications and Services Logs > Microsoft > Windows >
  AppXDeploymentServer, PrintService. The task writes errors to
  `%LOCALAPPDATA%\Packages\Chuvadi.PdfPrinter_*\LocalState\task.log`.
- **Agent in the foreground for debugging**: stop the service, then run
  `& "$env:ProgramFiles\Chuvadi\PdfPrinter\ChuvadiPdfAgent.exe" --console` in an elevated PowerShell.
