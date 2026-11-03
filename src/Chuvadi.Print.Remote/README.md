# Chuvadi.Print.Remote

Part of [Chuvadi.Print](../Chuvadi.Print/README.md): pure-BCL printing for .NET, zero NuGet dependencies, in the [Chuvadi](https://github.com/arunshivab/chuvadi) family. It needs the Chuvadi.Print package.

- **Remote printing** (`Chuvadi.Print.Remote`): your web application embeds a `PrintHub`; a `PrintAgent` on each branch PC dials out to it over HTTPS (no inbound port), fetches jobs, prints them on local or IPP printers and reports back. Per-agent tokens with rotation and revocation, a durable file-based job store, resumable downloads, and a journal that never prints a job twice. Still one audit event per job.

The agent example prints through Chuvadi.Print.Windows; any `IPrintTarget` or `IDocumentTarget` works, including the IPP targets of Chuvadi.Print.Ipp.

Remote printing: the hub inside your web application (here ASP.NET Core; any web stack works)…

```csharp
using Chuvadi.Print.Remote;

var tokens = new TokenAgentAuthenticator(new FileAgentTokenStore(@"D:\print\agents.json"));
var hub = new PrintHub(new FileJobStore(@"D:\print\jobs"), tokens, new FileAuditSink(@"D:\print\audit.log"));

string token = tokens.Issue("ward3-pc");      // give it to the agent's installer, once

app.Map("/print-hub/{**path}", async (HttpContext http, string? path) =>
{
    var request = new HubRequest(http.Request.Method, path ?? "");
    foreach (var h in http.Request.Headers) request.Headers[h.Key] = h.Value.ToString();
    using (var body = new MemoryStream()) { await http.Request.Body.CopyToAsync(body); request.Body = body.ToArray(); }

    using HubResponse response = await hub.HandleAsync(request, http.RequestAborted);
    http.Response.StatusCode = response.StatusCode;
    foreach (var h in response.Headers) http.Response.Headers[h.Key] = h.Value;
    if (response.ContentType is not null) http.Response.ContentType = response.ContentType;
    http.Response.ContentLength = response.ContentLength;
    await response.CopyBodyToAsync(http.Response.Body, http.RequestAborted);
});

hub.Submit("ward3-pc", "Ward-3 Laser", job);     // idempotent per job.JobId
hub.JobFinished += (_, e) => Console.WriteLine(e.Job.JobId + " " + e.Job.State);
```

…and the agent next to the printers (for example inside a Windows service):

```csharp
using var agent = new PrintAgent(new PrintAgentOptions
{
    HubUri = new Uri("https://hub.example.com/print-hub/"),
    AgentId = "ward3-pc",
    Credentials = new FileAgentCredentialStore(@"C:\ProgramData\Chuvadi\Agent\token.txt"),
    JournalFolder = @"C:\ProgramData\Chuvadi\Agent\journal",
    PrinterResolver = name => new WindowsPrinterTarget(name),
    PrinterListProvider = () => WindowsPrinters.List().Select(p => p.Name).ToList(),
    Rasterizer = rasterizer,                        // e.g. from Chuvadi.Print.Pdf.Raster
    LocalAuditSink = new FileAuditSink(@"C:\ProgramData\Chuvadi\Agent\audit.log")
});
await agent.RunAsync(stoppingToken);              // reconnects on its own
```

Full guide: [docs/Chuvadi.Print/GUIDE.md](https://github.com/arunshivab/chuvadi/blob/main/docs/Chuvadi.Print/GUIDE.md).

MIT licensed.
