using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chuvadi.Print.Remote
{
    /// <summary>Outcome of authenticating an agent request.</summary>
    public readonly struct AgentAuthResult
    {
        private AgentAuthResult(bool succeeded, string? newToken)
        {
            Succeeded = succeeded;
            NewToken = newToken;
        }

        public bool Succeeded { get; }

        /// <summary>A replacement token to hand to the agent with this response (rotation), or null.</summary>
        public string? NewToken { get; }

        public static AgentAuthResult Fail() => new(false, null);
        public static AgentAuthResult Success(string? newToken = null) => new(true, newToken);
    }

    /// <summary>Decides whether a request really comes from the agent it names. Must be thread-safe.</summary>
    public interface IAgentAuthenticator
    {
        /// <summary>Checks the bearer <paramref name="token"/> presented by <paramref name="agentId"/>.</summary>
        AgentAuthResult Authenticate(string agentId, string token);
    }

    /// <summary>The hub's persisted record of one agent's token. Only the SHA-256 hash is kept.</summary>
    public sealed class AgentTokenRecord
    {
        public string AgentId { get; set; } = string.Empty;

        /// <summary>SHA-256 of the token, lowercase hex.</summary>
        public string TokenHash { get; set; } = string.Empty;

        public DateTimeOffset IssuedAt { get; set; }
    }

    /// <summary>Where <see cref="TokenAgentAuthenticator"/> keeps token hashes. Must be thread-safe.</summary>
    public interface IAgentTokenStore
    {
        AgentTokenRecord? Get(string agentId);
        IReadOnlyList<AgentTokenRecord> List();
        void Set(AgentTokenRecord record);
        void Remove(string agentId);
    }

    /// <summary>A non-durable <see cref="IAgentTokenStore"/>.</summary>
    public sealed class InMemoryAgentTokenStore : IAgentTokenStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, AgentTokenRecord> _records = new(StringComparer.Ordinal);

        public AgentTokenRecord? Get(string agentId)
        {
            lock (_gate) return _records.TryGetValue(agentId, out AgentTokenRecord? r) ? Copy(r) : null;
        }

        public IReadOnlyList<AgentTokenRecord> List()
        {
            lock (_gate) return _records.Values.Select(Copy).OrderBy(r => r.AgentId, StringComparer.Ordinal).ToList();
        }

        public void Set(AgentTokenRecord record)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            lock (_gate) _records[record.AgentId] = Copy(record);
        }

        public void Remove(string agentId)
        {
            lock (_gate) _records.Remove(agentId);
        }

        internal static AgentTokenRecord Copy(AgentTokenRecord r) =>
            new() { AgentId = r.AgentId, TokenHash = r.TokenHash, IssuedAt = r.IssuedAt };
    }

    /// <summary>
    /// A durable <see cref="IAgentTokenStore"/>: one JSON file holding every agent's token hash, rewritten
    /// atomically (temporary file, flush, rename) on each change. Plain-text tokens are never written.
    /// </summary>
    public sealed class FileAgentTokenStore : IAgentTokenStore
    {
        private readonly object _gate = new();
        private readonly string _path;
        private readonly Dictionary<string, AgentTokenRecord> _records = new(StringComparer.Ordinal);

        public FileAgentTokenStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            _path = Path.GetFullPath(path);
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(_path))
            {
                List<AgentTokenRecord>? records =
                    JsonSerializer.Deserialize<List<AgentTokenRecord>>(File.ReadAllBytes(_path), RemoteProtocol.Json);
                foreach (AgentTokenRecord r in records ?? new List<AgentTokenRecord>())
                    if (!string.IsNullOrEmpty(r.AgentId)) _records[r.AgentId] = r;
            }
        }

        public AgentTokenRecord? Get(string agentId)
        {
            lock (_gate) return _records.TryGetValue(agentId, out AgentTokenRecord? r) ? InMemoryAgentTokenStore.Copy(r) : null;
        }

        public IReadOnlyList<AgentTokenRecord> List()
        {
            lock (_gate)
                return _records.Values.Select(InMemoryAgentTokenStore.Copy).OrderBy(r => r.AgentId, StringComparer.Ordinal).ToList();
        }

        public void Set(AgentTokenRecord record)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            lock (_gate)
            {
                _records[record.AgentId] = InMemoryAgentTokenStore.Copy(record);
                Save();
            }
        }

        public void Remove(string agentId)
        {
            lock (_gate)
            {
                if (_records.Remove(agentId)) Save();
            }
        }

        private void Save()
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                _records.Values.OrderBy(r => r.AgentId, StringComparer.Ordinal).ToList(), RemoteProtocol.Json);
            AtomicFile.Write(_path, json);
        }
    }

    /// <summary>
    /// Per-agent bearer tokens issued and revoked by the host. Tokens are 256-bit random values (base64url);
    /// the store keeps only their SHA-256 hashes and comparison is constant-time.
    /// </summary>
    /// <remarks>
    /// Rotation without reinstalling: <see cref="BeginRotation"/> creates a pending token that the hub hands
    /// to the agent on its next authenticated response (header <c>X-Chuvadi-New-Token</c>). The old token keeps
    /// working until the agent first uses the new one, which then replaces it. The pending token is held in
    /// memory only; if the hub restarts before the agent switches, the old token stays valid and the host
    /// simply begins the rotation again.
    /// </remarks>
    public sealed class TokenAgentAuthenticator : IAgentAuthenticator
    {
        private readonly object _gate = new();
        private readonly IAgentTokenStore _store;
        private readonly Dictionary<string, (string Hash, string Token)> _pending = new(StringComparer.Ordinal);
        private readonly TimeProvider _time;

        public TokenAgentAuthenticator(IAgentTokenStore store, TimeProvider? timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _time = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Issues a new token for <paramref name="agentId"/>, replacing any existing one (which stops working
        /// at once) and dropping any pending rotation. Returns the plain token: give it to the agent's
        /// installer; it cannot be read back later.
        /// </summary>
        public string Issue(string agentId)
        {
            ValidateAgentId(agentId);
            string token = NewToken();
            lock (_gate)
            {
                _pending.Remove(agentId);
                _store.Set(new AgentTokenRecord { AgentId = agentId, TokenHash = Hash(token), IssuedAt = _time.GetUtcNow() });
            }
            return token;
        }

        /// <summary>Revokes the agent: its token and any pending rotation stop working immediately.</summary>
        public bool Revoke(string agentId)
        {
            lock (_gate)
            {
                bool had = _store.Get(agentId) is not null;
                _pending.Remove(agentId);
                _store.Remove(agentId);
                return had;
            }
        }

        /// <summary>
        /// Starts a rotation and returns the pending token (also delivered to the agent automatically).
        /// Calling it again replaces the pending token.
        /// </summary>
        /// <exception cref="InvalidOperationException">The agent has no token.</exception>
        public string BeginRotation(string agentId)
        {
            lock (_gate)
            {
                if (_store.Get(agentId) is null) throw new InvalidOperationException("Agent '" + agentId + "' has no token; issue one first.");
                string token = NewToken();
                _pending[agentId] = (Hash(token), token);
                return token;
            }
        }

        /// <summary>True while a rotation is waiting for the agent to switch.</summary>
        public bool IsRotationPending(string agentId)
        {
            lock (_gate) return _pending.ContainsKey(agentId);
        }

        /// <summary>Agents that have a token.</summary>
        public IReadOnlyList<AgentTokenRecord> ListAgents() => _store.List();

        public AgentAuthResult Authenticate(string agentId, string token)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(token)) return AgentAuthResult.Fail();
            string presented = Hash(token);
            lock (_gate)
            {
                AgentTokenRecord? record = _store.Get(agentId);
                if (record is null) return AgentAuthResult.Fail();

                if (_pending.TryGetValue(agentId, out var pending))
                {
                    if (HashEquals(presented, pending.Hash))
                    {
                        _pending.Remove(agentId);
                        _store.Set(new AgentTokenRecord { AgentId = agentId, TokenHash = pending.Hash, IssuedAt = _time.GetUtcNow() });
                        return AgentAuthResult.Success();
                    }
                    return HashEquals(presented, record.TokenHash) ? AgentAuthResult.Success(pending.Token) : AgentAuthResult.Fail();
                }
                return HashEquals(presented, record.TokenHash) ? AgentAuthResult.Success() : AgentAuthResult.Fail();
            }
        }

        /// <summary>A new random token: 32 bytes, base64url without padding.</summary>
        public static string NewToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>SHA-256 of the token's UTF-8 bytes, lowercase hex.</summary>
        public static string Hash(string token) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static bool HashEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

        internal static void ValidateAgentId(string agentId)
        {
            if (string.IsNullOrWhiteSpace(agentId)) throw new ArgumentException("An agent id is required.", nameof(agentId));
            if (agentId.Length > 128) throw new ArgumentException("An agent id has at most 128 characters.", nameof(agentId));
            foreach (char c in agentId)
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                    throw new ArgumentException("An agent id uses only ASCII letters, digits, '-', '_' and '.'.", nameof(agentId));
        }
    }

    internal static class AtomicFile
    {
        public static void Write(string path, ReadOnlySpan<byte> data)
        {
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data);
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
