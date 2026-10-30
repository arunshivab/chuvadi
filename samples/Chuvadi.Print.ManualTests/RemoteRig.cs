using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Chuvadi.Print.Remote;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>A clock the tests move by hand (lease expiry).</summary>
    public sealed class ManualClock : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }

        public void Advance(TimeSpan by)
        {
            lock (_gate) _now += by;
        }
    }

    /// <summary>An <see cref="IDocumentTarget"/> that keeps what it was given.</summary>
    public sealed class RecordingDocumentTarget : IDocumentTarget
    {
        public string Name { get; set; } = "Recording document target";
        public List<DocumentSubmission> Submissions { get; } = new();
        public void Submit(DocumentSubmission submission, CancellationToken cancellationToken) => Submissions.Add(submission);
    }

    /// <summary>
    /// One hub (with its store, authenticator and audit sink) plus everything an agent needs, wired either over
    /// real HTTP (<see cref="HubHttpListenerHost"/>) or in process (<see cref="PrintHubMessageHandler"/>).
    /// </summary>
    public sealed class RemoteRig : IDisposable
    {
        public const string AgentId = "branch-01";
        public const string PrinterName = "Office";

        private readonly List<IDisposable> _owned = new();

        public RemoteRig(bool overHttp = false, IPrintJobStore? store = null, Action<PrintHubOptions>? configure = null, string? folder = null)
        {
            Folder = folder ?? Path.Combine(Path.GetTempPath(), "chuvadi-remote-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            Store = store ?? new InMemoryJobStore();
            Auth = new TokenAgentAuthenticator(new InMemoryAgentTokenStore(), Clock);
            var options = new PrintHubOptions { TimeProvider = Clock, SweepInterval = TimeSpan.Zero, LeaseSeconds = 30 };
            configure?.Invoke(options);
            Hub = new PrintHub(Store, Auth, HubAudit, options);
            Token = Auth.Issue(AgentId);
            Credentials = new InMemoryAgentCredentialStore(Token);
            if (overHttp)
            {
                Http = new HubHttpListenerHost(Hub);
                HubUri = Http.BaseUri;
            }
            else
            {
                HubUri = new Uri("https://hub.test/print-hub/");
            }
        }

        public string Folder { get; }
        public ManualClock Clock { get; } = new();
        public IPrintJobStore Store { get; }
        public TokenAgentAuthenticator Auth { get; }
        public MemoryAuditSink HubAudit { get; } = new();
        public PrintHub Hub { get; }
        public HubHttpListenerHost? Http { get; }
        public Uri HubUri { get; }
        public string Token { get; }
        public InMemoryAgentCredentialStore Credentials { get; }
        public RecordingTarget Target { get; } = new() { Name = PrinterName };
        public MemoryAuditSink AgentAudit { get; } = new();
        public FakeRasterizer Rasterizer { get; } = new();
        public List<string> AgentLog { get; } = new();

        public string JournalFolder => Path.Combine(Folder, "agent-journal");

        /// <summary>A new agent for this hub; the default resolver knows only <see cref="PrinterName"/>.</summary>
        public PrintAgent NewAgent(Action<PrintAgentOptions>? configure = null)
        {
            var options = new PrintAgentOptions
            {
                HubUri = HubUri,
                AgentId = AgentId,
                Credentials = Credentials,
                PrinterResolver = name => name == PrinterName ? Target : null,
                PrinterListProvider = () => new[] { PrinterName },
                Rasterizer = Rasterizer,
                JournalFolder = JournalFolder,
                PollWaitSeconds = 0,
                InitialBackoff = TimeSpan.FromMilliseconds(20),
                MaxBackoff = TimeSpan.FromMilliseconds(200),
                LocalAuditSink = AgentAudit,
                MachineName = "BRANCH-PC",
                Log = line => { lock (AgentLog) AgentLog.Add(line); }
            };
            if (Http is null)
            {
                options.HttpHandler = new PrintHubMessageHandler(Hub, HubUri);
            }
            configure?.Invoke(options);
            var agent = new PrintAgent(options);
            _owned.Add(agent);
            return agent;
        }

        /// <summary>Queues an A4 fake document of <paramref name="pages"/> pages for the agent's printer.</summary>
        public StoredJob Submit(int pages = 2, int copies = 1, Guid? jobId = null, string printer = PrinterName)
        {
            var job = new PrintJob(FakeDocument.Uniform(pages, PageDimensions.FromPaper(PaperSize.A4)))
                .WithCopies(copies).WithTitle("Invoice 42").WithUser("arun").WithTenant("lipi");
            if (jobId is Guid id) job.WithJobId(id);
            return Hub.Submit(AgentId, printer, job);
        }

        /// <summary>Sends a hand-made request to the hub (bypassing the agent).</summary>
        public HubResponse Raw(string method, string path, object? body = null, string? token = null, string? protocol = "1",
                               string? agentId = AgentId, string? range = null)
        {
            var request = new HubRequest(method, path);
            if (protocol is not null) request.Headers[RemoteProtocol.ProtocolHeader] = protocol;
            if (agentId is not null) request.Headers[RemoteProtocol.AgentHeader] = agentId;
            string? t = token ?? Credentials.GetToken();
            if (t is not null && t.Length > 0) request.Headers["Authorization"] = "Bearer " + t;
            if (range is not null) request.Headers["Range"] = range;
            if (body is not null) request.Body = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), JsonOptions);
            return Hub.HandleAsync(request).GetAwaiter().GetResult();
        }

        public static T ReadJson<T>(HubResponse response) =>
            JsonSerializer.Deserialize<T>(response.Body!, JsonOptions)!;

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public void Dispose()
        {
            foreach (IDisposable d in _owned) d.Dispose();
            Http?.Dispose();
            Hub.Dispose();
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
