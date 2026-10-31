using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChuvadiPdfPrinter
{
    /// <summary>
    /// The hand-off record the Windows 11 Print Support App writes next to each PDF in its outbox. The JSON is
    /// written last, so its presence means the PDF is complete.
    /// </summary>
    public sealed class OutboxEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public DateTimeOffset Submitted { get; set; } = DateTimeOffset.Now;

        /// <summary>The virtual printer the job was printed to.</summary>
        public string Printer { get; set; } = string.Empty;

        /// <summary>PDF file name in the outbox.</summary>
        public string PdfFile { get; set; } = string.Empty;

        /// <summary>Where the user saved it (Save As printer); null when the agent should place it.</summary>
        public string? SavedTo { get; set; }

        public JobInfo ToJobInfo() => new()
        {
            Title = Title,
            UserName = UserName,
            MachineName = MachineName,
            Submitted = Submitted
        };
    }

    /// <summary>Writes outbox entries (used by the Print Support App).</summary>
    public static class OutboxWriter
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        /// <summary>Copies <paramref name="pdf"/> into the outbox and commits it with its JSON record.</summary>
        public static OutboxEntry Write(string outboxFolder, Stream pdf, OutboxEntry entry)
        {
            if (outboxFolder is null) throw new ArgumentNullException(nameof(outboxFolder));
            if (pdf is null) throw new ArgumentNullException(nameof(pdf));
            if (entry is null) throw new ArgumentNullException(nameof(entry));
            Directory.CreateDirectory(outboxFolder);
            entry.PdfFile = entry.Id + ".pdf";
            string partial = Path.Combine(outboxFolder, entry.Id + ".pdf.partial");
            using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write))
                pdf.CopyTo(file);
            File.Move(partial, Path.Combine(outboxFolder, entry.PdfFile));
            Commit(outboxFolder, entry);
            return entry;
        }

        /// <summary>Writes only the JSON record for a PDF already placed in the outbox as &lt;Id&gt;.pdf.</summary>
        public static void Commit(string outboxFolder, OutboxEntry entry)
        {
            if (string.IsNullOrEmpty(entry.PdfFile)) entry.PdfFile = entry.Id + ".pdf";
            string partial = Path.Combine(outboxFolder, entry.Id + ".json.partial");
            File.WriteAllText(partial, JsonSerializer.Serialize(entry, Json));
            File.Move(partial, Path.Combine(outboxFolder, entry.Id + ".json"));
        }

        internal static OutboxEntry? Read(string jsonPath)
        {
            try
            {
                return JsonSerializer.Deserialize<OutboxEntry>(File.ReadAllText(jsonPath), Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>Collects committed outbox entries and hands them to the <see cref="AgentEngine"/>.</summary>
    public sealed class OutboxCollector
    {
        private readonly AgentEngine _engine;
        private readonly Func<IEnumerable<string>> _folders;

        public OutboxCollector(AgentEngine engine, Func<IEnumerable<string>> folders)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _folders = folders ?? throw new ArgumentNullException(nameof(folders));
        }

        public event Action<OutboxEntry, CaptureResult>? EntryCollected;

        /// <summary>Processes every committed entry once. Returns how many were processed.</summary>
        public int ScanOnce()
        {
            int processed = 0;
            foreach (string folder in _folders())
            {
                if (!Directory.Exists(folder)) continue;
                string[] records;
                try { records = Directory.GetFiles(folder, "*.json"); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                Array.Sort(records, StringComparer.Ordinal);
                foreach (string json in records)
                {
                    OutboxEntry? entry = OutboxWriter.Read(json);
                    if (entry is null)
                    {
                        _engine.Log.Error("Unreadable outbox record " + json + "; moved aside.");
                        TryMove(json, json + ".bad");
                        continue;
                    }
                    string pdf = Path.Combine(folder, Path.GetFileName(entry.PdfFile));
                    if (!File.Exists(pdf))
                    {
                        _engine.Log.Error("Outbox record " + json + " has no PDF; moved aside.");
                        TryMove(json, json + ".bad");
                        continue;
                    }
                    CaptureResult result = _engine.CompleteFile(pdf, entry.ToJobInfo(), entry.SavedTo);
                    if (result.Succeeded) TryDelete(json);
                    else TryMove(json, json + ".failed");
                    EntryCollected?.Invoke(entry, result);
                    processed++;
                }
            }
            return processed;
        }

        public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try { ScanOnce(); }
                catch (Exception ex) { _engine.Log.Error("Outbox scan failed", ex); }
                try { await Task.Delay(interval, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>
        /// Every user's Print Support App outbox: &lt;usersRoot&gt;\&lt;user&gt;\AppData\Local\Packages\&lt;family&gt;\LocalState\outbox.
        /// </summary>
        public static IEnumerable<string> PsaOutboxes(string usersRoot, string packageFamilyName)
        {
            if (!Directory.Exists(usersRoot) || string.IsNullOrWhiteSpace(packageFamilyName)) yield break;
            string[] users;
            try { users = Directory.GetDirectories(usersRoot); }
            catch (UnauthorizedAccessException) { yield break; }
            foreach (string user in users)
            {
                string outbox = Path.Combine(user, "AppData", "Local", "Packages", packageFamilyName, "LocalState", "outbox");
                if (Directory.Exists(outbox)) yield return outbox;
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static void TryMove(string from, string to)
        {
            try { File.Move(from, to, overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
