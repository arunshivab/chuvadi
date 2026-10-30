# Chuvadi.Print 1.0 — Windows verification

Run this checklist on **one Windows 10 PC (22H2)** and **one Windows 11 PC (24H2 or later)** before tagging
`print-v1.0.0`. Everything that Linux CI cannot prove is here. Steps are numbered so a failure can be reported
by number. Steps marked *Win11* or *Win10* run on that machine only; all other steps run on both.

Write down PASS / FAIL for each step. On a FAIL, keep the console output, `agent.log` and `audit.log`.

PowerShell 5.x: run each command line on its own (no `&&`).

---

## A. Preparation

1. **Code.** The release candidate branches are merged into `main`: A3, A4, the product, A6, A5, product
   forwarding, Layout docs, rc.1. Update your clone:
   ```powershell
   cd C:\Users\aruns\Documents\chuvadi-monorepo\chuvadi
   git checkout main
   git pull
   git log --oneline -3
   ```
   Expected: `src\Chuvadi.Print\Chuvadi.Print.csproj` says `<Version>1.0.0-rc.1</Version>`.
2. **Local feed.** The Chuvadi.Pdf packages of the pinned version (3.22.1, the `ChuvadiPdfVersion` in
   `staging\Chuvadi.Print.Pdf\Chuvadi.Print.Pdf.csproj`) are in the local feed:
   ```powershell
   Get-ChildItem C:\Users\aruns\Documents\local-nuget -Filter "Chuvadi.Pdf.Rendering.*.nupkg"
   ```
   Expected: `Chuvadi.Pdf.Rendering.3.22.1.nupkg` (the feed holds all 33 Chuvadi.Pdf packages of that version).
   If they are missing, in the chuvadi-pdf clone run `.\build\pack.ps1 -Version 3.22.1`, then
   `Copy-Item artifacts\nupkg\*.nupkg C:\Users\aruns\Documents\local-nuget`. Do not point `-OutputDir`
   at the feed: the script empties its output folder first.
3. **Tools.** The .NET 10 SDK is installed (`dotnet --list-sdks` shows 10.x). *Win11:* the Windows 11 SDK
   10.0.26100 or later is also installed (needed for the Print Support App).
4. **Printers.** Note which printers are available:
   ```powershell
   Get-Printer | Select-Object Name, DriverName, PortName
   ```
   Expected: "Microsoft Print to PDF" is listed. Note the name of any real printer, and the `ipp://` address
   of any network printer that supports IPP Everywhere / AirPrint.

## B. Library tests on Windows

5. **Monorepo tests.** In a **normal (not elevated)** PowerShell:
   ```powershell
   dotnet build Chuvadi.slnx -c Release
   dotnet test Chuvadi.slnx -c Release --no-build
   ```
   Expected: 0 warnings. All Sheets, Docs and Print tests pass (Print: 176). No "Access is denied" from
   HttpListener: the IPP and hub tests listen on `localhost` without a URL reservation. If Windows Firewall
   asks about "dotnet", choose Cancel; the traffic is loopback only.
6. **ManualTests.**
   ```powershell
   dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests
   ```
   Expected: `Failed: 0` and `Skipped: 0`. On Windows the two Windows groups must PASS, not SKIP:
   `WindowsPrintersAreDiscoverable` and `WindowsPrintToPdfWritesFile`. The 12 `Remote...` groups must PASS.
7. **Chuvadi.Print.Pdf tests.**
   ```powershell
   dotnet test staging/Chuvadi.Print.Pdf.Tests -c Release
   ```
   Expected: 27 passed.
8. **Preview images.** Look at the two folders:
   ```powershell
   explorer artifacts\bin\Chuvadi.Print.Pdf.Tests\Release\net10.0\print-pdf-preview
   explorer artifacts\bin\Chuvadi.Print.Pdf.Tests\Release\net10.0\layout-print-preview
   ```
   Expected:
   - **print-pdf-preview**, two A5 sheets:
     - Sheet 1 shows a landscape page turned clockwise. The black square is top-right and a tall black box is
       lower-left.
     - Sheet 2 shows a `/Rotate 90` page upright. The black square is top-right.
   - **layout-print-preview**: an A4 "Discharge summary - page 1" with crisp text in two columns.

## C. GDI printing (A3)

9. **Proof set to PDF files.**
   ```powershell
   dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --windows-printer "Microsoft Print to PDF" --windows-output "$env:USERPROFILE\Desktop\print-verify-gdi"
   explorer "$env:USERPROFILE\Desktop\print-verify-gdi"
   ```
   Expected: 11 lines starting `SENT`, and 11 PDFs created without any Save dialog. Open each one:
   - 01, 02, 03: a small A6 page at the top-left, centre and bottom-right of an A4 sheet.
   - 04: a Letter page fitted onto A4.
   - 05: a landscape sheet.
   - 06: upside down.
   - 07: a landscape page turned onto a portrait sheet.
   - 08: colour. 09: grey. 10: black-and-white dither.
   - 11: 3 pages x 2 copies, 8 sides including blank backs.
10. **Proof set to a real printer** (if available).
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --windows-printer "<your printer>"
    ```
    Expected: 11 jobs print with the same layouts as step 9. Job 11 prints on both sides when the printer
    duplexes. Check that nothing is cut off beyond the printer's own unprintable edge.

## D. IPP printer (A4), if one is available

11. **IPP proof set.**
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --ipp-printer "ipp://<printer-ip>/ipp/print" --ipp-pdf "C:\path\to\any.pdf"
    ```
    Expected: 6 raster jobs and the PDF print. Duplex backs are the right way up. For `ipps://` with the
    printer's own certificate, add `--ipp-insecure`.

## E. The Chuvadi PDF virtual printer

12. **Build the product.** From `products\ChuvadiPdfPrinter`:
    - Win10:
      ```powershell
      powershell -ExecutionPolicy Bypass -File scripts\build.ps1
      ```
    - Win11:
      ```powershell
      powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Psa
      ```
    Expected:
    - `out\agent\ChuvadiPdfAgent.exe` exists.
    - *Win11*: `out\psa\ChuvadiPdf.msix` and `out\psa\ChuvadiPdf.cer` also exist.
13. **Product tests.**
    ```powershell
    dotnet test ChuvadiPdfPrinter.sln -c Release
    ```
    Expected: 41 passed.
14. **Install the named-pipe queue.** *Win10* (on Win11, do this in step 21 with `-Mode Both`). In an
    **elevated** PowerShell, in `products\ChuvadiPdfPrinter`:
    ```powershell
    powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Mode Queue
    Get-Printer -Name "Chuvadi PDF" | Select-Object Name, DriverName, PortName
    Get-Service ChuvadiPdfAgent
    ```
    Expected:
    - The install ends with "Installed".
    - Driver `Microsoft Print To PDF`, port `\\.\pipe\ChuvadiPdfPrinter`.
    - The service is Running.
15. **Self-test.** In a **normal** PowerShell:
    ```powershell
    powershell -ExecutionPolicy Bypass -File scripts\selftest.ps1
    ```
    Expected: every step PASS, then `ALL SELF-TESTS PASSED`. The PDF it names opens and shows the test page.
16. **Print from an application.** Print a page from Notepad and one from Edge to "Chuvadi PDF".
    Expected:
    - Two separate PDFs appear in `C:\Users\Public\Documents\Chuvadi PDF\<your user name>\`, named
      `yyyy-MM-dd HHmmss <title>.pdf`.
    - Both open correctly.
    - `C:\ProgramData\Chuvadi\PdfPrinter\audit.log` has one line per job.
17. **Back-to-back jobs (named pipe).** Print the same document 3 times quickly.
    Expected: 3 distinct PDFs, e.g. "name.pdf", "name (2).pdf", "name (3).pdf". No job stuck in the queue
    (`Get-PrintJob -PrinterName "Chuvadi PDF"` is empty after a few seconds). None is truncated.
18. **Private folders** (if a second Windows user is available). Sign in as the second user and print once.
    Expected:
    - The second user gets their own folder.
    - The second user cannot open the first user's folder (Access denied).
    - An administrator can open both.
19. **Agent restart.** Stop the service (`Stop-Service ChuvadiPdfAgent`), print one page, then start it again
    (`Start-Service ChuvadiPdfAgent`).
    Expected:
    - While the service is stopped, the job waits in the queue with an error status.
    - After the start it is saved as a PDF, with no duplicate.
20. **Forward to a Windows printer** (Chuvadi.Print.Pdf + A3). In an elevated PowerShell, create a test
    queue that writes to a file, and point `ForwardTo` at it:
    ```powershell
    New-Item -ItemType Directory -Force -Path C:\ChuvadiVerify | Out-Null
    Add-PrinterPort -Name "C:\ChuvadiVerify\forward.pdf"
    Add-Printer -Name "Chuvadi Forward Test" -DriverName "Microsoft Print To PDF" -PortName "C:\ChuvadiVerify\forward.pdf"
    $cfg = "$env:ProgramData\Chuvadi\PdfPrinter\config.json"
    $c = [System.IO.File]::ReadAllText($cfg) | ConvertFrom-Json
    $c.ForwardTo = @([pscustomobject]@{ Kind = "WindowsPrinter"; Printer = "Chuvadi Forward Test"; Copies = 1; Duplex = "Simplex"; Paper = "Auto"; Color = "Color"; Scale = "ShrinkToFit"; MaxRasterDpi = 300 })
    [System.IO.File]::WriteAllText($cfg, ($c | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
    & "$env:ProgramFiles\Chuvadi\PdfPrinter\ChuvadiPdfAgent.exe" --validate --config $cfg
    Restart-Service ChuvadiPdfAgent
    ```
    Then print a 2-page document to "Chuvadi PDF". Expected:
    - The PDF is saved as in step 16.
    - `C:\ChuvadiVerify\forward.pdf` appears with the same 2 pages, at their own size and centred.
    - `agent.log` says "Forwarded".
    - `audit.log` gets two lines: the save and the forward.

    Repeat with a real printer's name in `Printer` if one is available: the pages print at their true size.
    Clean up afterwards:
    ```powershell
    $c.ForwardTo = @()
    [System.IO.File]::WriteAllText($cfg, ($c | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
    Restart-Service ChuvadiPdfAgent
    Remove-Printer -Name "Chuvadi Forward Test"
    Remove-PrinterPort -Name "C:\ChuvadiVerify\forward.pdf"
    ```
21. **Print Support App.** *Win11*: elevated, in `products\ChuvadiPdfPrinter`:
    ```powershell
    powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
    powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Mode Both
    Get-Printer | Where-Object Name -like "Chuvadi PDF*" | Select-Object Name, DriverName, PortName
    Get-AppxPackage -Name "Chuvadi.PdfPrinter"
    ```
    Expected:
    - Three printers: "Chuvadi PDF" (Save As), "Chuvadi PDF (Auto-save)", and "Chuvadi PDF (Compatibility)"
      on the pipe port.
    - The package is installed.
22. **Self-test of both Win11 variants.** *Win11*, normal PowerShell:
    ```powershell
    powershell -ExecutionPolicy Bypass -File scripts\selftest.ps1
    ```
    Expected: "Chuvadi PDF (Compatibility)" and "Chuvadi PDF (Auto-save)" are tested, then
    `ALL SELF-TESTS PASSED (2 printer(s))`.
23. **Save As printer.** *Win11*: from Notepad, print to "Chuvadi PDF". Expected:
    - A Save As window opens. The saved PDF opens correctly.
    - Printing to "Chuvadi PDF (Auto-save)" saves to your folder with no window.
24. **Uninstall.** Elevated:
    ```powershell
    powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
    Get-Printer | Where-Object Name -like "Chuvadi PDF*"
    Get-Service ChuvadiPdfAgent -ErrorAction SilentlyContinue
    ```
    Expected:
    - No Chuvadi printers and no service remain.
    - *Win11*: no package (`Get-AppxPackage Chuvadi.PdfPrinter` is empty).
    - The saved PDFs are still in `C:\Users\Public\Documents\Chuvadi PDF`.

## F. Sign-off and tag

25. **Record the result.** All steps that apply are PASS on both PCs. Note the PC names and Windows builds
    (`winver`) in the PR description.
26. **Release commit.** On a branch from `main`:
    - Set `<Version>1.0.0</Version>` in `src\Chuvadi.Print\Chuvadi.Print.csproj`.
    - In `src\Chuvadi.Print\CHANGELOG.md`, rename the `1.0.0-rc.1 (unreleased)` heading to
      `1.0.0 (<date>)`.
    - Set the product's `ChuvadiPrintPackageVersion` default to `1.0.0`.
    - Build, test, open the PR and merge it.
27. **Tag.** After the merge:
    ```powershell
    git checkout main
    git pull
    git tag -s print-v1.0.0 -m "Chuvadi.Print 1.0.0"
    git push origin print-v1.0.0
    ```
    Expected: the Release workflow builds, tests, packs and publishes Chuvadi.Print 1.0.0.

## Results

| Step | Win10 | Win11 | Notes |
|---|---|---|---|
| 1–8 | | | |
| 9–10 | | | |
| 11 | | | |
| 12–20 | | | |
| 21–24 | n/a | | |
| 25–27 | | | |
