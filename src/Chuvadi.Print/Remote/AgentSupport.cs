using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Chuvadi.Print.Remote
{
    /// <summary>Holds the agent's bearer token. Rotation writes the new token through <see cref="SetToken"/>.</summary>
    public interface IAgentCredentialStore
    {
        /// <summary>The current token, or null when none is configured.</summary>
        string? GetToken();

        /// <summary>Replaces the token (rotation). Must be durable before it returns.</summary>
        void SetToken(string token);
    }

    /// <summary>A non-durable <see cref="IAgentCredentialStore"/> (tests; agents whose host persists the token itself).</summary>
    public sealed class InMemoryAgentCredentialStore : IAgentCredentialStore
    {
        private readonly object _gate = new();
        private string? _token;

        public InMemoryAgentCredentialStore(string? token = null)
        {
            _token = token;
        }

        public string? GetToken()
        {
            lock (_gate) return _token;
        }

        public void SetToken(string token)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("A token is required.", nameof(token));
            lock (_gate) _token = token;
        }
    }

    /// <summary>
    /// Keeps the agent's token in a text file (UTF-8, one line), replaced atomically on rotation.
    /// Restrict the file to the agent's account (on Windows: SYSTEM and Administrators).
    /// </summary>
    public sealed class FileAgentCredentialStore : IAgentCredentialStore
    {
        private readonly object _gate = new();

        public FileAgentCredentialStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            Path = System.IO.Path.GetFullPath(path);
        }

        /// <summary>The token file (full path).</summary>
        public string Path { get; }

        public string? GetToken()
        {
            lock (_gate)
            {
                if (!File.Exists(Path)) return null;
                string token = File.ReadAllText(Path, Encoding.UTF8).Trim();
                return token.Length == 0 ? null : token;
            }
        }

        public void SetToken(string token)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("A token is required.", nameof(token));
            lock (_gate)
            {
                string? dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                AtomicFile.Write(Path, new UTF8Encoding(false).GetBytes(token));
            }
        }
    }

    /// <summary>A failed exchange with the hub (HTTP status other than the expected ones).</summary>
    public sealed class RemotePrintException : Exception
    {
        public RemotePrintException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>The HTTP status the hub answered with.</summary>
        public int StatusCode { get; }
    }

    /// <summary>One job as the agent's journal records it.</summary>
    public sealed class AgentJournalEntry
    {
        public Guid JobId { get; set; }

        /// <summary>When printing started; null when the job never reached the printer.</summary>
        public DateTimeOffset? StartedAt { get; set; }

        /// <summary>When the outcome was decided.</summary>
        public DateTimeOffset? FinishedAt { get; set; }

        /// <summary>The final report (sent, or still to be sent).</summary>
        public StatusReport? FinalReport { get; set; }

        /// <summary>True once the hub acknowledged the final report.</summary>
        public bool Reported { get; set; }
    }

    /// <summary>
    /// The agent's crash-safe memory of what it printed: one small JSON file per job, written atomically
    /// before printing starts and again when the outcome is known. A job that started but has no outcome
    /// was interrupted (crash, power loss) and is never printed again; its outcome is reported as Failed.
    /// </summary>
    internal sealed class AgentJournal
    {
        private const string Extension = ".journal.json";
        private readonly object _gate = new();

        public AgentJournal(string folder)
        {
            Folder = System.IO.Path.GetFullPath(folder);
            Directory.CreateDirectory(Folder);
        }

        public string Folder { get; }

        public string SpoolPath(Guid jobId) => System.IO.Path.Combine(Folder, jobId.ToString("N") + ".cvpr");
        public string PartialPath(Guid jobId) => SpoolPath(jobId) + ".partial";
        private string EntryPath(Guid jobId) => System.IO.Path.Combine(Folder, jobId.ToString("N") + Extension);

        public AgentJournalEntry? Get(Guid jobId)
        {
            lock (_gate)
            {
                string path = EntryPath(jobId);
                if (!File.Exists(path)) return null;
                try { return JsonSerializer.Deserialize<AgentJournalEntry>(File.ReadAllBytes(path), RemoteProtocol.Json); }
                catch (JsonException) { return new AgentJournalEntry { JobId = jobId, StartedAt = File.GetLastWriteTimeUtc(path) }; }
            }
        }

        public void Save(AgentJournalEntry entry)
        {
            lock (_gate) AtomicFile.Write(EntryPath(entry.JobId), JsonSerializer.SerializeToUtf8Bytes(entry, RemoteProtocol.Json));
        }

        public IReadOnlyList<AgentJournalEntry> List()
        {
            lock (_gate)
            {
                foreach (string tmp in Directory.EnumerateFiles(Folder, "*.tmp").ToList())
                {
                    try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                var list = new List<AgentJournalEntry>();
                foreach (string path in Directory.EnumerateFiles(Folder, "*" + Extension))
                {
                    string name = System.IO.Path.GetFileName(path);
                    if (!Guid.TryParseExact(name.Substring(0, name.Length - Extension.Length), "N", out Guid id)) continue;
                    AgentJournalEntry? e = Get(id);
                    if (e is not null) list.Add(e);
                }
                return list;
            }
        }

        public void Delete(Guid jobId)
        {
            lock (_gate)
            {
                DeleteQuietly(EntryPath(jobId));
                DeleteFiles(jobId);
            }
        }

        public void DeleteFiles(Guid jobId)
        {
            DeleteQuietly(SpoolPath(jobId));
            DeleteQuietly(PartialPath(jobId));
        }

        private static void DeleteQuietly(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Captures the single audit event a PrintJob records, so the agent can send it to the hub.</summary>
    internal sealed class CapturingAuditSink : IPrintAuditSink
    {
        private readonly object _gate = new();
        private readonly List<PrintAuditEvent> _events = new();

        public void Record(PrintAuditEvent auditEvent)
        {
            lock (_gate) _events.Add(auditEvent);
        }

        public PrintAuditEvent? Last
        {
            get { lock (_gate) return _events.Count == 0 ? null : _events[_events.Count - 1]; }
        }

        public int Count
        {
            get { lock (_gate) return _events.Count; }
        }
    }

    /// <summary>Thread-safe latest progress, read by the heartbeat.</summary>
    internal sealed class LatestProgress : IProgress<PrintProgress>
    {
        private readonly IProgress<PrintProgress>? _forward;
        private int _impressions;
        private int _page;

        public LatestProgress(IProgress<PrintProgress>? forward)
        {
            _forward = forward;
        }

        public int Impressions => System.Threading.Volatile.Read(ref _impressions);
        public int CurrentPage => System.Threading.Volatile.Read(ref _page);

        public void Report(PrintProgress value)
        {
            System.Threading.Volatile.Write(ref _impressions, value.ImpressionsCompleted);
            System.Threading.Volatile.Write(ref _page, value.CurrentPageNumber);
            _forward?.Report(value);
        }
    }
}
