# Chuvadi.Print — Changelog

## 1.0.0-rc.3 (2026-11-07)

All four packages move to 1.0.0-rc.3 (D-201 P-02). Phase 4 parts 1 to 3 (D-189, D-223, D-238): the vector page
model, the XPS writer, and vector printing to Windows queues. New API only. One approved change to existing
behaviour, in Chuvadi.Print.Remote:

- **`PrintAgent.RunOnceAsync` cancellation** (D-189, error-type change): when the caller cancels while an exchange
  with the hub is failing, `RunOnceAsync` now throws `OperationCanceledException` (the failure as its
  `InnerException`) instead of the transport error, for example `RemotePrintException` "HTTP 502". Before, that
  depended on timing. Failures without a cancellation keep their own types, `RunAsync` behaves as before, and a
  journalled outcome is still delivered by the next cycle.

- **Vector page model** (core): `VectorPage` (points, origin top left, y down) holding `VectorPath`,
  `VectorGlyphRun`, `VectorImageElement` and `VectorGroup` elements, each with its own `Transform`, `Clip`,
  `Opacity` and `BlendMode`. Brushes `VectorSolidBrush` (sRGB with alpha), `VectorLinearGradientBrush` and
  `VectorRadialGradientBrush` (PDF type 2 and 3 geometry, with `ExtendStart`/`ExtendEnd` and a `Transform`).
  `VectorStroke` (width, caps, joins, miter limit, dashes), `VectorGeometry` (move, line, cubic, close, fill rule),
  `VectorFont` (the font file), `VectorImage` (PNG or JPEG, size, resolution and `HasAlpha` read from the file;
  `FromPixels` encodes grey, RGB, grey with alpha or RGB with alpha as PNG), groups with `Isolated`, `Knockout` and `SoftMask`, and `VectorMatrix`.
- **XPS writer** (Chuvadi.Print.Windows, runs on any OS): `XpsWriter.Write(pages, stream)` and
  `Write(pages, path)` write an ECMA-388 package with System.IO.Compression's ZipArchive: TrueType fonts embedded
  once each, obfuscated (ECMA-388 9.1.7.3); images once each, PNG transparency kept; groups as nested Canvas
  elements; alpha soft masks as an OpacityMask holding a VisualBrush of the mask content; text as Glyphs
  with Unicode where every glyph's text is known. Pages are written one at a time. The same pages give the same
  bytes.
- **What XPS cannot express is refused, not approximated**: `XpsWriter.CanWrite` names the first page and item.
  Refused: blend modes other than Normal, knockout groups, luminosity soft masks (an XPS opacity mask uses alpha
  only), gradients that do not extend,
  radial gradients whose start circle is not a point inside the end circle, CFF fonts and font collections, fonts
  whose OS/2 fsType forbids embedding (Restricted License, bitmap only), glyph ids past the font's glyph count,
  CMYK JPEGs, strokes of zero width, all-zero dash patterns and values that are not finite. `Write` throws
  `NotSupportedException` for such a page.
- **Windows-only XPS tests** (D-189 item 14, D-202): new test project `tests/Chuvadi.Print.Xps.Tests`
  (`net10.0-windows`, WPF; never packed). It opens the XPS sample with WPF's own XPS reader, checks the page count
  and sizes, and checks rendered pixels at known points: shapes, group opacity, the checker image, text drawn
  with the embedded font, the soft-mask fades and the transparent image. It builds on every OS and runs on
  Windows. It joins Chuvadi.slnx and the Windows CI job through the shared-files change.
- **Windows interop is source-generated** (D-217 item 2, D-223): the 26 winspool.drv and gdi32.dll entry points of
  Chuvadi.Print.Windows are `LibraryImport` methods instead of classic `DllImport`. The marshalling code is now
  generated at build time, which is trimming- and NativeAOT-safe. The calls, the UTF-16 strings, the Win32 BOOL
  results and the last-error handling are the same, so behaviour, errors and output are unchanged. The project
  sets `AllowUnsafeBlocks` (Chuvadi.Print.Windows only), because the generated code uses pointers.
- **Trimming and NativeAOT** (D-217 item 1, D-223 step 3): the JSON in `PrinterRegistry` and in Chuvadi.Print.Remote
  (hub and agent messages, the agent journal, the token file, the file job store) uses System.Text.Json source
  generation instead of reflection, with enums written as names as before. The output is byte for byte the same
  as 1.0.0-rc.2 (golden tests), and files and messages written by rc.2 read back unchanged, including numeric enums
  and any letter case in property names. All four packages now set `IsTrimmable` and `IsAotCompatible`, so the
  trimming and AOT analyzers run on every build. A trimmed app (TrimMode full) and a NativeAOT app using all four
  packages publish with no warnings and work. No public API, behaviour or error changes.
- **ManualTests**: two new groups (`XpsSampleIsACompletePackage`, `XpsRefusesWhatItCannotExpress`, total 61) and
  `--xps-sample <file.xps>` (with `--xps-font`) to write a two-page sample for viewing. WINDOWS-VERIFICATION.md
  gains section D3.
- **Vector printing to Windows queues** (Phase 4 part 3, D-223 step 4, D-238; Chuvadi.Print.Windows, Windows 8
  and later). New API:
  - `WindowsXpsPrinterTarget` (an `IDocumentTarget`) prints vector pages as XPS through the Print Document
    Package API. The job PrintTicket is made from the same driver-merged DEVMODE as `WindowsPrinterTarget`
    (`PTConvertDevModeToPrintTicket`, job scope), so paper, orientation, duplex, colour, copies, collation, tray,
    media and resolution behave alike. Pages are placed with `PageLayout` and planned with `PrintPlan`, as on the
    raster routes.
  - Its members:
    - `Submit` (pages from `Source`) and `PrintPages` (the application's own pages);
    - `CanPrint` (says which page and item XPS cannot express);
    - `SaveXps` (the exact package, without printing) and `CreatePrintTicket`;
    - the GDI target's `Bin`, `MediaType`, `Resolution`, `Quality`, `PaperMatching` and `StrictCapabilities`;
    - `MarginReference`, `CopyHandling` and `StatusTimeout`;
    - `LastSpoolJobId`, `LastJobStatus` (`XpsJobStatus`, `XpsJobCompletion`), `LastStatusEventsConnected`,
      `LastPrintTicket` and `LastSheetCount`.
  - New defaults: `StatusTimeout` two minutes (`DefaultStatusTimeout`; null = do not wait), `CopyHandling.Auto`,
    `MarginReference.SheetEdge`, `PaperMatching.ExactOrCustom`, `StrictCapabilities` false.
  - Errors (D-189 item 11): a job the print system reports failed or cancelled, or that reports nothing within the
    timeout, raises `WindowsPrintException`. Through `PrintJob.SubmitTo` it becomes a Failed result; the router
    never retries on another route.
  - `WindowsQueueRoute` (`PrintRouteKind.WindowsQueue`, D-96 position 4) is used only where the application adds it
    (D-189 item 5): existing code keeps printing through GDI raster. Its check skips to the next route, with the
    reason in the audit, when there is no vector source, the queue cannot be opened, or a selected page holds
    content XPS cannot express. The check converts the selected pages, and printing converts them again.
  - Core: `IVectorPageSource` and `IVectorDocument`, the vector counterpart of `IPageRasterizer`. The PDF
    converter of part d will implement them. `VectorLayout.Place` and `VectorLayout.PageToSheet` place a vector
    page on its sheet with a `PagePlacement`.
  - Output change (D-189 item 12, accepted): a job sent through this route is drawn by the driver as vectors.
    Grey and black-and-white come from the driver; `PrintJobOptions.Monochrome` applies to rendered pages only.
  - COM and PrintTicket interop is our own source-generated code (`GeneratedComInterface`, `LibraryImport`;
    trimming- and AOT-safe). Method order and signatures were read from the public-domain mingw-w64 headers and
    checked against Microsoft's documentation (D-238). The record is `docs/Chuvadi.Print/PROVENANCE-XPS-COM.md`.
  - ManualTests: `--xps-ticket`, `--xps-queue` (with `--xps-queue-file`) and `--xps-route`, with
    `--xps-orientation`, `--xps-copies`, `--xps-software-copies`, `--xps-duplex` and `--xps-status-seconds`.
    WINDOWS-VERIFICATION.md gains section D4: two test queues whose port is a file, the PrintTicket, the sample
    through the XPS and PDF drivers, settings, the route decision, and a real printer.

- **In-memory PWG Raster spool** (D-217 item 4, D-223 step 5; Chuvadi.Print.Ipp). New API:
  `IppPrinterTarget.Spool` (`IppSpoolMode.TemporaryFile`, the default and today's behaviour, or `Memory`), with
  `MemorySpoolLimit` (default `DefaultMemorySpoolLimit`, 256 MB) and `WithSpool(mode, limit)`. A memory-spooled job
  that grows past the limit fails before anything is sent, with an `IppException`; it never falls back to a file.
  The memory is released when the job is sent or fails. Both spools send byte-identical jobs.
- **`FontEmbedding.NeedsGeneratedAppearances`** (D-235 L2.3, core). This new value means every font is embedded
  but some form fields or comments need appearances generated. The routes already require exactly
  `AllEmbedded`, so such a PDF is never sent as-is. `IppPdfRoute` gives the specific reason "form fields or comments
  need appearances generated". The existing values keep their numbers (0, 1, 2); the new one is 3. The Pdf font
  inspector in Chuvadi.Print.Pdf.Raster (L2) will report it.
- **GUIDE**: new sections on thread safety (D-217 item 3, audited against the code) and Android (D-217 item 5:
  permissions, cleartext `ipp://` on Android 9 and later, the multicast lock for discovery). The IPP section
  describes the spool, and the route list now names the WindowsQueue route.

## 1.0.0-rc.2 (2026-11-04)

Phase 1 of the vector and delivery plan (D-96, D-132, D-134). For all four packages.

- **Route chooser** (core): `PrintRouter` asks each `IPrintRoute` in D-96 order (`PdfAsIs`, `PageDescription`,
  `Cups`, `WindowsQueue`, `Raster`) and prints on the first that can. Site overrides (`ForcedRoute`,
  `DisabledRoutes`), a per-job override, and `Choose` to see the decision without printing. A route that fails
  while printing is not retried on another route, so nothing is printed twice. `RasterRoute` wraps any
  `IPrintTarget`.
- **Fonts rule** (D-132 item 2): a PDF is sent as-is only when every font is embedded.
  `PrintJobOptions.DocumentFonts` (default `Unknown`) and `PrintJob.WithFontsEmbedded()` let an application
  mark its own PDFs; `IPdfFontInspector` is the hook for a Chuvadi.Pdf inspector later. Unknown fonts go to
  raster.
- **Audit route** (D-132 item 1, output change): `PrintAuditEvent.Route` and `AuditRecord.Route` (Remote). It is
  `Raster` for `PrintTo`, `Document` for `SubmitTo`, or the router's choice with
  `(fallback: Kind: reason; ...)`. `FileAuditSink` writes it as a new 13th column, **last**, so the 12 existing
  columns keep their positions.
- **IPP routes** (Chuvadi.Print.Ipp): `IppPdfRoute` (`PdfAsIs`, or `Cups` through `IppPdfRoute.ForCups`) and
  `IppRasterRoute` (PWG Raster). `IppRoutes.CreateRouter` and `CreateCupsRouter` build both. Only an explicit
  `application/pdf` in document-format-supported counts as accepting PDF (D-132 item 3).
- **Capability cache** (Chuvadi.Print.Ipp): `IppCapabilityCache`, 10 minutes, then read again; also read again
  after a failed read or a failed job (D-132 item 6).
- **Certificate pinning** (Chuvadi.Print.Ipp): `IppClientOptions.PinnedCertificates` (SHA-256 fingerprints);
  `IppCertificates.GetServerCertificate`, `Fingerprint` and `Matches`. Without pins nothing changes.
  `AcceptAnyServerCertificate` still wins.
- **Raw delivery**: `IRawPrintTarget` (core). `RawSocketTarget` (TCP 9100, any OS, Chuvadi.Print.Ipp) and
  `WindowsRawPrinterTarget` (Windows queue with the RAW datatype through `StartDocPrinter`/`WritePrinter`,
  Chuvadi.Print.Windows). Both pass bytes unchanged and also implement `IDocumentTarget`.
- **Unchanged** (D-132 item 4): `IppDocumentTarget` still sends a PDF as application/octet-stream to a printer
  that lists only that, and still reads the printer's capabilities on each job. `IppPrinterTarget` and
  `WindowsPrinterTarget` behave as before.
- **ManualTests**: three new groups (`RouterFallsBackToRasterAndAuditsWhy`, `RouterSendsEmbeddedPdfAsIs`,
  `RawSocketDeliversBytesUnchanged`, total 56), and the options `--route-printer`, `--ipp-certificate`,
  `--raw-printer` and `--windows-raw-printer`. WINDOWS-VERIFICATION.md gains section D2.
- **Phase 1 part 2**:
  - **Saved printers** (core): `PrinterRegistry` and `SavedPrinter` (name, `PrinterConnection` Ipp, Cups,
    RawSocket or WindowsQueue, address, pinned certificate, forced route, location, notes, and the capabilities as
    last read with `CapabilitiesReadAt` and `CapabilitiesAge`). Saved as versioned JSON, written atomically.
    Credentials are never stored.
  - **IPP side** (Chuvadi.Print.Ipp): `IppCapabilityCache.StoreIn` and `LoadFrom` keep capabilities in a saved
    printer; `IppRoutes.CreateRouter(SavedPrinter)` applies its pin, forced route and name.
  - **Network discovery** (Chuvadi.Print.Ipp): `NetworkPrinterDiscovery.Browse` finds IPP, IPPS and raw-port
    printers with DNS-SD over multicast DNS (RFC 6762, RFC 6763), our own code. It runs only when the application
    calls it (D-132 item 6), sends one-shot queries from a temporary port and never listens on port 5353 (P07).
    `DiscoveredPrinter.ToSavedPrinter` turns a result into a saved printer.
  - **CUPS queues** (Chuvadi.Print.Ipp): `IppClient.CupsGetPrinters` and `CupsQueues.List` list a CUPS server's
    queues over IPP (CUPS-Get-Printers, D-94). `IppOperation` gains `CupsGetDefault` and `CupsGetPrinters`.
  - **Back-side table** (D-132 item 5, D-53): `IppPrinterTarget`'s duplex back-side transforms are now written
    directly from PWG 5102.4-2012 section 4.3.2.8, Table 9, and tested against every row of it. The values are
    the same as before, so **output is unchanged**; no bug fix was needed.
  - **ManualTests**: three more groups (`CupsQueuesAreListed`, `PrinterRegistryKeepsCapabilities`,
    `BackSideTransformFollowsPwgTable9`, total 59), and the options `--discover [seconds]` and `--cups-server`.

## 1.0.0-rc.1 (2026-11-04)

- **Package split (D-107, D-125)**: Chuvadi.Print is now four packages at one version, released together by the
  `print-v*` tag. Chuvadi.Print (core: settings, layout, plan, `PrintJob`, rasterisation interface, spool
  envelope, PNG preview, audit) has no device code; Chuvadi.Print.Windows, Chuvadi.Print.Ipp and
  Chuvadi.Print.Remote each depend on the core only. Namespaces and type names are unchanged, so callers add the
  package they use and change no code. One doc comment (`IDocumentTarget`) now names `IppDocumentTarget` in plain
  text instead of a link. The PDF adapter Chuvadi.Print.Pdf is renamed Chuvadi.Print.Pdf.Raster (namespace
  `Chuvadi.Print.Pdf.Raster`).
- **Removed (D-48, D-11 withdrawn)**: the "Chuvadi PDF" virtual printer product (`products/ChuvadiPdfPrinter`:
  the Print Support App, the named-pipe queue on Microsoft Print To PDF, the agent service, installer and
  self-test scripts) is removed completely. It never shipped and nothing in the library depended on it.
  Chuvadi.Print.Pdf.Raster's `PdfFileTarget` (print a job to a PDF file) is removed too; saving as PDF belongs to the
  Chuvadi.Pdf writer and Chuvadi.Layout's PDF output.

Release candidate for 1.0.0. Everything from 0.1.0 to 0.5.0 (A1 to A6) is in it. The public API is now frozen
for 1.x: from here on, changes are additive only, and any breaking change waits for 2.0.

- **Version**: 1.0.0-rc.1. No API changes since 0.5.0.
- **Docs**: `docs/Chuvadi.Print/GUIDE.md`, the user guide covering settings, layout and planning, `PrintJob`,
  the targets (PNG, Windows, IPP), the spool envelope, the audit trail, remote printing (hub and
  agent) and Chuvadi.Print.Pdf.Raster. `docs/Chuvadi.Print/PRINTING-REPORTS.md`
  covers printing Docs/Sheets reports through Chuvadi.Layout. `docs/Chuvadi.Print/WINDOWS-VERIFICATION.md` is
  the checklist run on Windows 10 and 11 before tagging print-v1.0.0.
- **Companions**: Chuvadi.Print.Pdf.Raster 0.1.0 lives in `staging/`. It consumes the Chuvadi.Pdf 3.22.1 packages
  from the local feed (D-29) and stays outside `Chuvadi.slnx` and CI until Chuvadi.Pdf moves into the monorepo
  or is on nuget.org (D-28).
- **Known limits for 1.0**: Chuvadi.Pdf's renderer draws the media box. Annotation printing ignores the
  annotation Print flag. Everything verified only on Windows
  is listed in WINDOWS-VERIFICATION.md.

## 0.5.0 (unreleased)

A6: remote printing (`Chuvadi.Print.Remote`, decision D-26). Agents next to the printers dial out to a hub the host application embeds; nothing listens on the agent's machine (P07). Pure BCL (System.Net.Http, System.Text.Json, System.Security.Cryptography).

- **PrintHub**: host-agnostic protocol handler. The host forwards each request as a `HubRequest` to `HandleAsync` and writes back the `HubResponse` (works with ASP.NET Core, HttpListener or anything else; `PrintHubMessageHandler` runs hub and agent in one process). Host API: `Submit(agentId, printer, PrintJob | SpoolEnvelope)`, `GetJob`, `ListJobs`, `ListAgents`, `Cancel`, `ExpireLeases`, events `JobFinished` and `BackgroundError`. `PrintHubOptions`: lease length, long-poll wait, maximum attempts, payload deletion, retention of finished jobs, sweep interval, maximum payload size, `TimeProvider`.
- **Protocol v1** (headers `X-Chuvadi-Protocol`, `X-Chuvadi-Agent`, `Authorization: Bearer`): `POST agents/hello`, `POST agents/poll` (long poll; 204 or a `JobLease`), `GET jobs/{id}/payload` (`Range` resume, 206/416), `POST jobs/{id}/status` (Accepted, Printing, Printed, Failed, Cancelled; progress renews the lease; the reply carries `cancelRequested`). Rotated tokens arrive in `X-Chuvadi-New-Token`. JSON is camelCase with string enums.
- **Jobs**: the job id is the `PrintJob` / envelope JobId, so submitting again is idempotent. Leases expire back into the queue and fail after `MaxAttempts`; cancellation is immediate for queued jobs and reaches a printing agent within a third of the lease. `HubJobState`, `StoredJob`, `AgentInfo`.
- **Exactly one audit event per job** (P06): the agent's own `PrintTo` / `SubmitTo` event is sent with the final status (`AuditRecord`) and recorded once by the hub; jobs that end on the hub, or before printing on the agent, get one synthesized event. Late or repeated final reports are acknowledged and ignored.
- **Storage**: `IPrintJobStore` (persistence only; every rule lives in the hub). `FileJobStore` keeps a JSON record and a payload file per job, written through a flushed temporary file and an atomic rename, reloads after a restart and cleans up unfinished writes, orphan payloads and unreadable records. `InMemoryJobStore` for tests.
- **Authentication**: `TokenAgentAuthenticator` issues 256-bit tokens, stores only SHA-256 hashes (`IAgentTokenStore`: `FileAgentTokenStore`, `InMemoryAgentTokenStore`), compares in constant time, revokes at once, and rotates without reinstalling (`BeginRotation`: the pending token is handed to the agent on its next response and replaces the old one when first used).
- **PrintAgent**: `RunAsync` (reconnects with exponential back-off up to `MaxBackoff`) and `RunOnceAsync`. Downloads to the journal folder, resumes interrupted downloads with `Range`, verifies SHA-256, prints on the target chosen by `PrinterResolver` / `DocumentTargetResolver`, sends heartbeats with progress, stops on cancel. A crash-safe journal (`AgentJournalEntry`) records each job before printing and its outcome after; a job seen again is never printed twice (reported as interrupted instead), and outcomes the hub has not acknowledged are resent. Tokens live in an `IAgentCredentialStore` (`FileAgentCredentialStore`, `InMemoryAgentCredentialStore`) and rotated tokens are saved automatically. HTTPS is required except to loopback or with `AllowInsecureHttp`. `RemotePrintException` carries the HTTP status.
- **Verification**: twelve groups run hub and agent end to end over real HTTP (`HubHttpListenerHost`, which can cut a download to prove resume) and in process: printing with one audit event, idempotent submit, download resume, lease renewal/expiry/retry limit, crash without reprint, cancel queued and in flight, token rotation and revocation, file store restart and recovery, protocol errors, HTTPS rule, PDF passthrough, unknown printer. Unit tests cover the run loop, reconnection after network failures, resending a final report, and the stores.
- **ManualTests**: the in-process IPP printer and the hub host now listen on `localhost` (Windows lets a normal user listen there without a URL reservation).

## 0.4.0 (unreleased)

A4: driverless printing over IPP (`Chuvadi.Print.Ipp`), on any OS, pure BCL (System.Net.Http only).

- **IPP encoding** (RFC 8010): `IppMessage` (requests and responses, any version), `IppAttributeGroup` (with `Set` / `Remove`), `IppAttribute` (1setOf), `IppValue` for every value type — integer, boolean, enum, octetString, dateTime, resolution, rangeOfInteger, collections (nested, 1setOf), text/name with and without language, keyword, uri, uriScheme, charset, naturalLanguage, mimeMediaType, out-of-band values, and unknown or extension tags kept as raw bytes so a decoded message re-encodes byte for byte. `IppTag`, `IppOperation`, `IppStatusCode`, `IppJobState`, `IppPrinterState`, `IppPrintQuality`.
- **IppClient**: ipp://, ipps://, http://, https:// (ipp/ipps default to port 631); Get-Printer-Attributes, Print-Job, Validate-Job, Create-Job, Send-Document, Get-Job-Attributes, Cancel-Job, Get-Jobs, and raw `Send` for any operation. `IppClientOptions`: user name, natural language, IPP version, timeout, credentials, acceptance of self-signed printer certificates. `IppException` carries the IPP status and response.
- **IppPrinterCapabilities**: formats, sides, media (with sources and types), colour modes, copies, collation, scaling, quality, orientations, resolutions, PWG Raster types/resolutions/sheet-back, margins, state; every raw attribute is kept.
- **PWG Raster** (PWG 5102.4): `PwgRasterWriter` and `PwgRasterReader` (black_1, sgray_8, srgb_8; PackBits-style line compression with line repeats and white fill), `PwgPageHeader`.
- **IppPrinterTarget** (`IPrintTarget`): prints rendered pages as PWG Raster. Chooses the raster resolution under `MaxRasterDpi`, turns landscape sheets to portrait feed, applies the printer's duplex back-side transform (`PwgSheetBack`, overridable), spools to a temporary file (one sheet in memory), then one Print-Job.
- **IppDocumentTarget** (`IDocumentTarget`): passes the original PDF through to printers that accept it, with the settings as job attributes.
- **Job ticket mapping** (`IppJobTicket`, `IppJobOptions`): copies and collation, sides, PWG media names (`PwgMediaNames`) or `media-col` with tray and media type, colour mode (bi-level for monochrome when offered), quality, resolution; in document mode also orientation, page-ranges and print-scaling. Settings IPP cannot express are listed in `LastApproximations`, or fail the job with `Strict`.
- **Core**: `IDocumentTarget`, `DocumentSubmission` and `PrintJob.SubmitTo(target)` send the document itself (no rasterising) with exactly one audit event; a configured rasteriser is used only to count pages.
- **Verification**: codec and raster round trips, header offsets, mapping rules, and end-to-end jobs against an in-process IPP printer (`FakeIppPrinter`); ManualTests `--preview` also writes the decoded IPP pages as PNG; `--ipp-printer <uri> [--ipp-pdf <file>] [--ipp-insecure]` sends the proof set (and a PDF) to a real printer.

## 0.3.0 (unreleased)

A3: printing to Windows printers through the GDI spooler (`Chuvadi.Print.Windows`).

- **Single assembly, plain net10.0** (BASELINE P08 amended, decision D-12): Windows code lives in the `Chuvadi.Print.Windows` namespace, is marked `[SupportedOSPlatform("windows")]`, and reaches `winspool.drv` / `gdi32.dll` through P/Invoke only. No native binaries, no `net10.0-windows` target; Linux builds and tests are unchanged.
- **Discovery**: `WindowsPrinters.List(includeConnections)`, `Get`, `Exists`, `DefaultPrinterName`, `SetDefault`; `PrinterInfo` with server, share, port, driver, comment, location, `PrinterAttributes`, `PrinterStatus` (every spooler flag) and job count.
- **Capabilities**: `WindowsPrinters.GetCapabilities(name, settings)` returns every driver paper (`PrinterPaper`), tray (`PrinterBin`), media type (`PrinterMediaType`) and resolution (`PrinterResolution`), maximum copies, collation, duplex, colour, landscape rotation, custom-size limits, and the device geometry for those settings (resolution, sheet, hard margins). `ToTargetCapabilities(maxRasterDpi)` feeds the A2 planner.
- **WindowsPrinterTarget** (`IPrintTarget`): maps `PrintSettings` to the driver's DEVMODE (paper, orientation, duplex, colour, copies and collation when the driver makes them) plus tray, media type, resolution or `PrintQuality` preset; `PaperMatching` (ExactOrCustom, ExactOnly, CustomOnly); `OutputFileName` (print to file; with "Microsoft Print to PDF" writes a PDF without a dialog); `MaxRasterDpi` (default 300) bounds page bitmaps on high-resolution devices; `StrictCapabilities` fails the job when the driver cannot honour the paper or duplex, otherwise prints and drops blank backs on simplex printers; `LastSpoolJobId`, `LastDeviceSheet`. Fluent `With*` for each.
- **Drawing**: pages are placed at their planned position with `StretchDIBits` from top-down 32-, 24-, 8- (gray palette) or 1-bit (white/black palette) DIBs, in bands of about 4 MB; HALFTONE stretching for colour and gray, COLORONCOLOR for bi-level.
- **Errors**: `WindowsPrintException` (with `NativeErrorCode`), `PrinterNotFoundException`; on non-Windows `PlatformNotSupportedException`.
- **Verification**: portable unit tests for DEVMODE editing (offsets, flags, short DEVMODEs, driver-private bytes), paper matching, geometry and DIB banding; Windows-only tests and a verification group that print through "Microsoft Print to PDF" to a file; ManualTests `--windows-printer "<name>" [--windows-output <folder>]` prints an 11-job proof set; groups that do not apply report SKIP.

## 0.2.0 (unreleased)

A2: `PrintJob` orchestration with pluggable rasterisation.

- **PrintJob**: `new PrintJob(bytes | path | stream)`, `PrintJob.LoadSpool(path | stream | bytes)`, `PrintJob.FromEnvelope(...)`; property setters plus a fluent `With*` method for every setting, piece of metadata and option; `Plan(...)`, `PrintTo(target, cancellationToken)`, `SaveSpoolTo(path | stream)`, `ToSpoolArray()`, `ToEnvelope()`.
- **Pluggable rasterisation**: `IPageRasterizer` / `IRasterDocument`, `RasterRequest` (DPI per axis, pixel format, rotation, annotations, form fields, anti-aliasing), `RasterPage` (pooled buffers via `RasterPage.Rent`), `PageDimensions`, `RasterPixelFormat` (Bgra32, Rgb24, Gray8, BlackWhite1), `PageRotation`. Print takes no dependency on any renderer; the PDF adapter ships separately as Chuvadi.Print.Pdf.
- **Layout**: `PageLayout.Compute` places a page on the sheet for every orientation (including the reverse ones), every scale mode, all nine alignments and both `MarginReference` modes (`SheetEdge` = larger of requested and hard margin; `PrintableArea` = requested plus hard margin). Reports printable area, content bounds, scale, rotation and clipping (`PagePlacement`, `RectanglePoints`).
- **Auto-rotate**: new `PrintSettings.AutoRotate` turns a page whose orientation differs from the printable area's by 90 degrees.
- **Planning**: `PrintPlan` / `PlannedImpression` expand page selection, copies and collation, pair duplex sides onto sheets, and insert blank backs so each duplex copy (or repeated sheet) starts on a fresh sheet. Uncollated duplex repeats whole sheets.
- **Targets**: `IPrintTarget` with `PrintTargetCapabilities` (DPI, hard margins, copies, collate, duplex, colour, preferred pixel format), `PrintJobContext` and `PrintedPage`. `CopyHandling` (Auto, Target, Software) decides who makes copies.
- **ImageFileTarget**: portable target that writes each impression as a PNG of the whole sheet (pure-BCL PNG encoder), with configurable DPI, simulated hard margins, file-name pattern and optional layout guides.
- **Colour**: output is Bgra32/Rgb24 for colour, Gray8 for grayscale (or colour on a mono device), BlackWhite1 for monochrome, with `MonochromeConversion` Threshold (adjustable), OrderedDither (8x8 Bayer) or ErrorDiffusion (Floyd-Steinberg).
- **Bounded memory**: one page is rendered at a time and released before the next; consecutive impressions of the same page reuse it.
- **Outcome and audit**: exactly one `PrintAuditEvent` per `PrintTo`, on success, failure or cancellation. Failures and cancellation are returned in `PrintResult` (or rethrown with `ThrowOnFailure`). `PrintResult` gains `ImpressionsPrinted` and `SheetsPrinted`. Progress via `IProgress<PrintProgress>`.
- **Spool envelope v2**: carries `AutoRotate`, `JobId`, `DocumentTitle`, `UserName` and `TenantId`. Version 1 envelopes are still read.

## 0.1.0 (unreleased)

A1: portable core — `PrintSettings`, `PaperSize`, `Margins`, `ContentAlignment`, `PageSelection`, `SpoolEnvelope` (v1), `PrintResult`, audit (`PrintAuditEvent`, `IPrintAuditSink`, `NullAuditSink`, `FileAuditSink`).
