using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chuvadi.Print.Remote
{
    /// <summary>Protocol constants shared by <see cref="PrintHub"/> and <see cref="PrintAgent"/>.</summary>
    public static class RemoteProtocol
    {
        /// <summary>Wire protocol version (header X-Chuvadi-Protocol).</summary>
        public const int Version = 1;

        public const string AgentHeader = "X-Chuvadi-Agent";
        public const string ProtocolHeader = "X-Chuvadi-Protocol";

        /// <summary>Sent by the hub when the agent should switch to a new token.</summary>
        public const string NewTokenHeader = "X-Chuvadi-New-Token";

        public const string JsonContentType = "application/json";
        public const string PayloadContentType = "application/vnd.chuvadi.spool";

        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>Lifecycle of a job on the hub.</summary>
    public enum HubJobState
    {
        /// <summary>Waiting for its agent.</summary>
        Queued,
        /// <summary>Handed to the agent; the lease runs.</summary>
        Leased,
        /// <summary>The agent has the payload and has accepted the job.</summary>
        Accepted,
        /// <summary>The agent is printing (lease renewed by its reports).</summary>
        Printing,
        /// <summary>Final: printed.</summary>
        Printed,
        /// <summary>Final: failed.</summary>
        Failed,
        /// <summary>Final: cancelled.</summary>
        Cancelled
    }

    /// <summary>A job as the hub stores it.</summary>
    public sealed class StoredJob
    {
        public Guid JobId { get; set; }
        public string AgentId { get; set; } = string.Empty;

        /// <summary>The agent's local printer to print on.</summary>
        public string Printer { get; set; } = string.Empty;

        public HubJobState State { get; set; } = HubJobState.Queued;
        public DateTimeOffset SubmittedAt { get; set; }
        public string? Title { get; set; }
        public string? UserName { get; set; }
        public string? TenantId { get; set; }
        public int Copies { get; set; } = 1;

        public long PayloadBytes { get; set; }

        /// <summary>SHA-256 of the payload, lowercase hex.</summary>
        public string PayloadSha256 { get; set; } = string.Empty;

        public Guid? LeaseId { get; set; }
        public DateTimeOffset? LeaseExpiresAt { get; set; }

        /// <summary>How many times the job has been leased.</summary>
        public int Attempts { get; set; }

        public bool CancelRequested { get; set; }
        public int PagesPrinted { get; set; }
        public int ImpressionsPrinted { get; set; }
        public int SheetsPrinted { get; set; }
        public string? Error { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>True once the job's single audit event has been recorded.</summary>
        public bool Audited { get; set; }

        [JsonIgnore]
        public bool IsFinal => State is HubJobState.Printed or HubJobState.Failed or HubJobState.Cancelled;

        public StoredJob Clone() => (StoredJob)MemberwiseClone();
    }

    /// <summary>What the hub knows about an agent.</summary>
    public sealed class AgentInfo
    {
        public string AgentId { get; set; } = string.Empty;
        public string? MachineName { get; set; }
        public string? AgentVersion { get; set; }
        public IReadOnlyList<string> Printers { get; set; } = Array.Empty<string>();
        public DateTimeOffset LastSeen { get; set; }
        public DateTimeOffset? FirstSeen { get; set; }
    }

    // ------------------------------------------------------------------ wire messages

    /// <summary>agents/hello request.</summary>
    public sealed class HelloRequest
    {
        public string? MachineName { get; set; }
        public string? AgentVersion { get; set; }
        public List<string> Printers { get; set; } = new();
    }

    /// <summary>agents/hello response.</summary>
    public sealed class HelloResponse
    {
        public int ProtocolVersion { get; set; } = RemoteProtocol.Version;
        public int PollWaitSeconds { get; set; }
        public int LeaseSeconds { get; set; }
        public DateTimeOffset ServerTime { get; set; }
    }

    /// <summary>agents/poll request.</summary>
    public sealed class PollRequest
    {
        /// <summary>How long the agent is willing to wait for a job (the hub may wait less).</summary>
        public int WaitSeconds { get; set; } = 25;
    }

    /// <summary>A job handed to an agent (agents/poll response).</summary>
    public sealed class JobLease
    {
        public Guid JobId { get; set; }
        public Guid LeaseId { get; set; }
        public int LeaseSeconds { get; set; }
        public string Printer { get; set; } = string.Empty;
        public long PayloadBytes { get; set; }
        public string PayloadSha256 { get; set; } = string.Empty;
        public string? Title { get; set; }
        public int Attempt { get; set; }
    }

    /// <summary>jobs/{id}/status request: the agent's progress or final outcome.</summary>
    public sealed class StatusReport
    {
        public Guid LeaseId { get; set; }

        /// <summary>Accepted, Printing, Printed, Failed or Cancelled.</summary>
        public HubJobState State { get; set; }

        public int PagesPrinted { get; set; }
        public int ImpressionsPrinted { get; set; }
        public int SheetsPrinted { get; set; }
        public string? Error { get; set; }

        /// <summary>The job's audit event, with the final state.</summary>
        public AuditRecord? Audit { get; set; }
    }

    /// <summary>jobs/{id}/status response.</summary>
    public sealed class StatusResponse
    {
        /// <summary>The host asked to cancel the job; the agent should stop and report Cancelled.</summary>
        public bool CancelRequested { get; set; }

        /// <summary>The hub already holds a final outcome for the job; further reports are ignored.</summary>
        public bool Final { get; set; }

        public int LeaseSeconds { get; set; }
    }

    /// <summary>Serializable form of <see cref="PrintAuditEvent"/>.</summary>
    public sealed class AuditRecord
    {
        public Guid JobId { get; set; }
        public string? DocumentTitle { get; set; }
        public int PageCount { get; set; }
        public int Copies { get; set; } = 1;
        public string PrinterName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public PrintJobStatus Status { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? TenantId { get; set; }
        public string? Error { get; set; }

        public static AuditRecord From(PrintAuditEvent e) => new()
        {
            JobId = e.JobId,
            DocumentTitle = e.DocumentTitle,
            PageCount = e.PageCount,
            Copies = e.Copies,
            PrinterName = e.PrinterName,
            MachineName = e.MachineName,
            UserName = e.UserName,
            Status = e.Status,
            StartedAt = e.StartedAt,
            CompletedAt = e.CompletedAt,
            TenantId = e.TenantId,
            Error = e.Error
        };

        public PrintAuditEvent ToEvent() => new()
        {
            JobId = JobId,
            DocumentTitle = DocumentTitle,
            PageCount = PageCount,
            Copies = Copies,
            PrinterName = PrinterName,
            MachineName = MachineName,
            UserName = UserName,
            Status = Status,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            TenantId = TenantId,
            Error = Error
        };
    }

    /// <summary>Error body returned with 4xx/5xx responses.</summary>
    public sealed class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }
}
