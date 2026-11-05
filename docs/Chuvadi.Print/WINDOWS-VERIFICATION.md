# Chuvadi.Print 1.0 — Windows verification

Run this checklist on **one Windows 10 PC (22H2)** and **one Windows 11 PC (24H2 or later)** before tagging
`print-v1.0.0`. Everything that Linux CI cannot prove is here. Steps are numbered so a failure can be reported
by number. Every step runs on both PCs.

Write down PASS / FAIL for each step. On a FAIL, keep the console output and any audit log written.

PowerShell 5.x: run each command line on its own (no `&&`).

---

## A. Preparation

1. **Code.** The release candidate branches are merged into `main`: A3, A4, A6, A5, Layout docs, rc.1, the D-48 removal,
   the package split, and Phase 1 part 1 (rc.2). Update your clone:
   ```powershell
   cd C:\src\chuvadi-monorepo\chuvadi
   git checkout main
   git pull
   git log --oneline -3
   ```
   Expected: `src\Chuvadi.Print\Chuvadi.Print.csproj` says `<Version>1.0.0-rc.2</Version>`.
2. **Local feed.** The Chuvadi.Pdf packages of the pinned version (3.22.1, the `ChuvadiPdfVersion` in
   `staging\Chuvadi.Print.Pdf.Raster\Chuvadi.Print.Pdf.Raster.csproj`) are in the local feed:
   ```powershell
   Get-ChildItem C:\src\local-nuget -Filter "Chuvadi.Pdf.Rendering.*.nupkg"
   ```
   Expected: `Chuvadi.Pdf.Rendering.3.22.1.nupkg` (the feed holds all 33 Chuvadi.Pdf packages of that version).
   If they are missing, in the chuvadi-pdf clone run `.\build\pack.ps1 -Version 3.22.1`, then
   `Copy-Item artifacts\nupkg\*.nupkg C:\src\local-nuget`. Do not point `-OutputDir`
   at the feed: the script empties its output folder first.
3. **Tools.** The .NET 10 SDK is installed (`dotnet --list-sdks` shows 10.x).
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
   Expected: 0 warnings. All Sheets, Docs and Print tests pass (Print: 229). No "Access is denied" from
   HttpListener: the IPP and hub tests listen on `localhost` without a URL reservation. If Windows Firewall
   asks about "dotnet", choose Cancel; the traffic is loopback only.
6. **ManualTests.**
   ```powershell
   dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests
   ```
   Expected: `Total: 59`, `Failed: 0` and `Skipped: 0`. On Windows the two Windows groups must PASS, not SKIP:
   `WindowsPrintersAreDiscoverable` and `WindowsPrintToPdfWritesFile`. The 12 `Remote...` groups and the
   six Phase 1 groups (`RouterFallsBackToRasterAndAuditsWhy`, `RouterSendsEmbeddedPdfAsIs`,
   `RawSocketDeliversBytesUnchanged`, `CupsQueuesAreListed`, `PrinterRegistryKeepsCapabilities`,
   `BackSideTransformFollowsPwgTable9`) must PASS.
7. **Chuvadi.Print.Pdf.Raster tests.**
   ```powershell
   dotnet test staging/Chuvadi.Print.Pdf.Raster.Tests -c Release
   ```
   Expected: 16 passed.
8. **Preview images.** Look at the two folders:
   ```powershell
   explorer artifacts\bin\Chuvadi.Print.Pdf.Raster.Tests\Release\net10.0\print-pdf-preview
   explorer artifacts\bin\Chuvadi.Print.Pdf.Raster.Tests\Release\net10.0\layout-print-preview
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

## D2. Raw delivery (Phase 1)

11a. **RAW queue writing to a file.** One-time setup in an **elevated** PowerShell. It makes a queue with the
    inbox "Generic / Text Only" driver whose port is a file:
    ```powershell
    Add-PrinterDriver -Name "Generic / Text Only"
    ```
    ```powershell
    Add-PrinterPort -Name "C:\Users\Public\chuvadi-raw.prn"
    ```
    ```powershell
    Add-Printer -Name "Chuvadi RAW Test" -DriverName "Generic / Text Only" -PortName "C:\Users\Public\chuvadi-raw.prn"
    ```
    Then in a normal window:
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --windows-raw-printer "Chuvadi RAW Test" --windows-raw-file "C:\Users\Public\chuvadi-raw.prn"
    ```
    Expected: `PASS  Windows RAW: spooler job <n>, <m> bytes; port file matches byte for byte`.
    To remove the queue afterwards (elevated): `Remove-Printer -Name "Chuvadi RAW Test"`.
11b. **RAW refused by an XPS-only queue.**
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --windows-raw-printer "Microsoft Print to PDF"
    ```
    Expected: either `FAIL  Windows RAW -> Printer 'Microsoft Print to PDF' does not accept the RAW datatype ...`
    or a PASS line whose job then fails in the queue. Record which one; both are acceptable (the queue's v4 driver
    cannot print RAW data).
11c. **Raw port 9100**, if a network printer has it open (most office lasers do):
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --raw-printer socket://<printer-ip>:9100
    ```
    Expected: `PASS  Raw printer: 36 bytes sent ...` and a page with the line "Chuvadi.Print raw port 9100 check"
    (printers that do not read plain text print nothing; record that).
11d. **Route decision** for an IPP printer (prints nothing):
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --route-printer "ipp://<printer-ip>/ipp/print"
    ```
    Expected: `fonts unknown -> Raster (fallback: PdfAsIs: fonts not known to be embedded)`, and for embedded
    fonts `PdfAsIs` when the printer lists application/pdf, otherwise a Raster fallback naming why.

11e. **Discovery** on the network the printers are on (prints nothing):
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --discover 5
    ```
    Expected: `PASS  Discovery: <n> service(s) found`, then one line per service with its name, type
    (`_ipp._tcp`, `_ipps._tcp` or `_pdl-datastream._tcp`), URI, make and model, and `pdl` (the formats it
    advertises). Record each printer's lines. If Windows Firewall asks about "dotnet", choose Cancel (no rule is
    added). If discovery then finds nothing while a printer is on the same network, record that.

## D3. XPS writer (Phase 4)

11f. **XPS sample.** Write the two-page sample with Arial:
    ```powershell
    dotnet run -c Release --no-build --project samples/Chuvadi.Print.ManualTests -- --xps-sample "$env:USERPROFILE\Documents\chuvadi-xps-sample.xps"
    ```
    Expected: `PASS  XPS sample: 2 pages written to ... font: C:\Windows\Fonts\arial.ttf`.
    Open the file in XPS Viewer (if it is missing: Settings, System, Optional features, add "XPS Viewer").
    Page 1 shows:
    - the title and a line of text with Omega, Zhe and "fi";
    - a red rectangle, a blue rectangle with a black border, and a green dashed line with long dashes;
    - a purple ring with a hole, an orange-to-purple bar and a shaded blue ball;
    - a colour ramp square, a 2-by-2 checker (smoothing is the viewer's choice), and two overlapping grey squares
      at 50 % with the overlap **the same grey** (group opacity);
    - a striped disc, text rotated 30 degrees, half-transparent blue text, and the text line "Bottom line at 72 pt
      from the bottom edge", whose baseline is 72 pt (25.4 mm) above the bottom of the page (no line is drawn).
    Page 2 is A5 landscape with a frame on the page edge and centre lines. Select and copy some text: it pastes
    as the same words. Record anything that looks different.

## E. Sign-off and tag

12. **Record the result.** All steps that apply are PASS on both PCs. Note the PC names and Windows builds
    (`winver`) in the PR description.
13. **Release commit.** On a branch from `main`:
    - Set `<Version>1.0.0</Version>` in `src\Chuvadi.Print\Chuvadi.Print.csproj`.
    - In `src\Chuvadi.Print\CHANGELOG.md`, rename the `1.0.0-rc.1 (unreleased)` heading to
      `1.0.0 (<date>)`.
    - Build, test, open the PR and merge it.
14. **Tag.** After the merge:
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
| 12–14 | | | |
