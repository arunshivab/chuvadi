using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Chuvadi.Print.Remote
{
    /// <summary>
    /// Where a <see cref="PrintHub"/> keeps its jobs and their payloads (spool envelopes). The store only
    /// persists; every rule (leases, retries, cancellation, audit) lives in the hub, so a store behaves the
    /// same whichever backing it uses. Implementations must be thread-safe. One hub process owns a store.
    /// </summary>
    /// <remarks>
    /// Ships with <see cref="FileJobStore"/> (durable, the default for hosts) and <see cref="InMemoryJobStore"/>
    /// (tests, throw-away hubs). A host can add a database store by implementing this interface.
    /// </remarks>
    public interface IPrintJobStore
    {
        /// <summary>
        /// Adds a job and its payload. Idempotent: when a job with the same id exists, nothing is written and
        /// the existing job is returned; otherwise returns null.
        /// </summary>
        StoredJob? Add(StoredJob job, ReadOnlyMemory<byte> payload);

        /// <summary>The job, or null.</summary>
        StoredJob? Get(Guid jobId);

        /// <summary>All jobs, oldest submission first.</summary>
        IReadOnlyList<StoredJob> List();

        /// <summary>Persists a changed job (the whole record). The job must exist.</summary>
        void Update(StoredJob job);

        /// <summary>Opens the payload for reading (seekable), or returns null when it is gone. The caller disposes it.</summary>
        Stream? OpenPayload(Guid jobId);

        /// <summary>Deletes the payload (the job record stays). No-op when it is gone.</summary>
        void DeletePayload(Guid jobId);

        /// <summary>Deletes the job and its payload. No-op when it is gone.</summary>
        void Remove(Guid jobId);
    }

    /// <summary>A non-durable <see cref="IPrintJobStore"/> for tests and throw-away hubs.</summary>
    public sealed class InMemoryJobStore : IPrintJobStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, StoredJob> _jobs = new();
        private readonly Dictionary<Guid, byte[]> _payloads = new();
        private readonly List<Guid> _order = new();

        public StoredJob? Add(StoredJob job, ReadOnlyMemory<byte> payload)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            lock (_gate)
            {
                if (_jobs.TryGetValue(job.JobId, out StoredJob? existing)) return existing.Clone();
                _jobs[job.JobId] = job.Clone();
                _payloads[job.JobId] = payload.ToArray();
                _order.Add(job.JobId);
                return null;
            }
        }

        public StoredJob? Get(Guid jobId)
        {
            lock (_gate) return _jobs.TryGetValue(jobId, out StoredJob? job) ? job.Clone() : null;
        }

        public IReadOnlyList<StoredJob> List()
        {
            lock (_gate) return _order.Select(id => _jobs[id].Clone()).ToList();
        }

        public void Update(StoredJob job)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            lock (_gate)
            {
                if (!_jobs.ContainsKey(job.JobId)) throw new KeyNotFoundException("Job " + job.JobId + " is not in the store.");
                _jobs[job.JobId] = job.Clone();
            }
        }

        public Stream? OpenPayload(Guid jobId)
        {
            lock (_gate)
                return _payloads.TryGetValue(jobId, out byte[]? data) ? new MemoryStream(data, writable: false) : null;
        }

        public void DeletePayload(Guid jobId)
        {
            lock (_gate) _payloads.Remove(jobId);
        }

        public void Remove(Guid jobId)
        {
            lock (_gate)
            {
                _jobs.Remove(jobId);
                _payloads.Remove(jobId);
                _order.Remove(jobId);
            }
        }
    }

    /// <summary>
    /// The durable <see cref="IPrintJobStore"/>: one JSON record per job (<c>jobs/&lt;id&gt;.json</c>) and one
    /// payload file per job (<c>payloads/&lt;id&gt;.cvpr</c>) under a root folder. Every write goes to a
    /// temporary file that is flushed to disk and then renamed over the target, so a crash leaves either the
    /// old or the new version, never a torn one. The payload is written before the job record; a payload
    /// without a record is an unfinished submission and is removed at startup, as are leftover temporary
    /// files. Survives restarts: the constructor reloads every job.
    /// </summary>
    /// <remarks>Protect the root folder (host account only): payloads are the documents being printed.</remarks>
    public sealed class FileJobStore : IPrintJobStore
    {
        private const string JobExtension = ".json";
        private const string PayloadExtension = ".cvpr";
        private const string TempExtension = ".tmp";

        private readonly object _gate = new();
        private readonly string _jobsDir;
        private readonly string _payloadsDir;
        private readonly Dictionary<Guid, StoredJob> _jobs = new();

        /// <summary>Opens (creating when needed) the store under <paramref name="rootFolder"/> and loads its jobs.</summary>
        public FileJobStore(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder)) throw new ArgumentException("A folder is required.", nameof(rootFolder));
            RootFolder = Path.GetFullPath(rootFolder);
            _jobsDir = Path.Combine(RootFolder, "jobs");
            _payloadsDir = Path.Combine(RootFolder, "payloads");
            Directory.CreateDirectory(_jobsDir);
            Directory.CreateDirectory(_payloadsDir);
            Load();
        }

        /// <summary>The store's root folder (full path).</summary>
        public string RootFolder { get; }

        /// <summary>Job records that could not be read at startup; they were renamed to <c>*.json.corrupt</c>.</summary>
        public IReadOnlyList<string> CorruptRecords { get; private set; } = Array.Empty<string>();

        public StoredJob? Add(StoredJob job, ReadOnlyMemory<byte> payload)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            lock (_gate)
            {
                if (_jobs.TryGetValue(job.JobId, out StoredJob? existing)) return existing.Clone();
                WriteAtomic(PayloadPath(job.JobId), payload.Span);
                WriteAtomic(JobPath(job.JobId), JsonSerializer.SerializeToUtf8Bytes(job, RemoteProtocol.Json));
                _jobs[job.JobId] = job.Clone();
                return null;
            }
        }

        public StoredJob? Get(Guid jobId)
        {
            lock (_gate) return _jobs.TryGetValue(jobId, out StoredJob? job) ? job.Clone() : null;
        }

        public IReadOnlyList<StoredJob> List()
        {
            lock (_gate)
                return _jobs.Values.OrderBy(j => j.SubmittedAt).ThenBy(j => j.JobId).Select(j => j.Clone()).ToList();
        }

        public void Update(StoredJob job)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));
            lock (_gate)
            {
                if (!_jobs.ContainsKey(job.JobId)) throw new KeyNotFoundException("Job " + job.JobId + " is not in the store.");
                WriteAtomic(JobPath(job.JobId), JsonSerializer.SerializeToUtf8Bytes(job, RemoteProtocol.Json));
                _jobs[job.JobId] = job.Clone();
            }
        }

        public Stream? OpenPayload(Guid jobId)
        {
            try
            {
                return new FileStream(PayloadPath(jobId), FileMode.Open, FileAccess.Read,
                                      FileShare.Read | FileShare.Delete, 81920, FileOptions.SequentialScan);
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        public void DeletePayload(Guid jobId)
        {
            lock (_gate) DeleteQuietly(PayloadPath(jobId));
        }

        public void Remove(Guid jobId)
        {
            lock (_gate)
            {
                DeleteQuietly(JobPath(jobId));
                DeleteQuietly(PayloadPath(jobId));
                _jobs.Remove(jobId);
            }
        }

        // ------------------------------------------------------------------ internals

        private string JobPath(Guid id) => Path.Combine(_jobsDir, id.ToString("N") + JobExtension);
        private string PayloadPath(Guid id) => Path.Combine(_payloadsDir, id.ToString("N") + PayloadExtension);

        private void Load()
        {
            foreach (string tmp in Directory.EnumerateFiles(_jobsDir, "*" + TempExtension)
                         .Concat(Directory.EnumerateFiles(_payloadsDir, "*" + TempExtension)).ToList())
                DeleteQuietly(tmp);

            var corrupt = new List<string>();
            foreach (string path in Directory.EnumerateFiles(_jobsDir, "*" + JobExtension))
            {
                StoredJob? job = null;
                try { job = JsonSerializer.Deserialize<StoredJob>(File.ReadAllBytes(path), RemoteProtocol.Json); }
                catch (JsonException) { }
                if (job is null || job.JobId == Guid.Empty ||
                    !string.Equals(Path.GetFileNameWithoutExtension(path), job.JobId.ToString("N"), StringComparison.OrdinalIgnoreCase))
                {
                    string moved = path + ".corrupt";
                    File.Move(path, moved, overwrite: true);
                    corrupt.Add(moved);
                    continue;
                }
                _jobs[job.JobId] = job;
            }
            CorruptRecords = corrupt;

            foreach (string payload in Directory.EnumerateFiles(_payloadsDir, "*" + PayloadExtension).ToList())
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(payload), "N", out Guid id) || !_jobs.ContainsKey(id))
                    DeleteQuietly(payload);
            }
        }

        private static void WriteAtomic(string path, ReadOnlySpan<byte> data) => AtomicFile.Write(path, data);

        private static void DeleteQuietly(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
