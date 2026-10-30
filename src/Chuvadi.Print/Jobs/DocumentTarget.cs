using System;
using System.Threading;

namespace Chuvadi.Print
{
    /// <summary>
    /// A destination that takes the document itself (PDF) plus its settings and lays it out on its own:
    /// an IPP printer that accepts PDF (<see cref="Ipp.IppDocumentTarget"/>), a print agent, an archive.
    /// Used with <see cref="PrintJob.SubmitTo"/>; nothing is rasterised.
    /// </summary>
    public interface IDocumentTarget
    {
        /// <summary>Display name of the destination. Recorded in the audit trail.</summary>
        string Name { get; }

        /// <summary>Delivers the document. Throws on failure; honours cancellation where it can.</summary>
        void Submit(DocumentSubmission submission, CancellationToken cancellationToken);
    }

    /// <summary>What <see cref="PrintJob.SubmitTo"/> hands to an <see cref="IDocumentTarget"/>.</summary>
    public sealed class DocumentSubmission
    {
        internal DocumentSubmission(Guid jobId, string? documentTitle, string? userName, string? tenantId,
                                    PrintSettings settings, ReadOnlyMemory<byte> document, string contentType, int? pageCount)
        {
            JobId = jobId;
            DocumentTitle = documentTitle;
            UserName = userName;
            TenantId = tenantId;
            Settings = settings;
            Document = document;
            ContentType = contentType;
            PageCount = pageCount;
        }

        public Guid JobId { get; }
        public string? DocumentTitle { get; }
        public string? UserName { get; }
        public string? TenantId { get; }

        /// <summary>A snapshot of the job's settings. Targets must not modify it.</summary>
        public PrintSettings Settings { get; }

        /// <summary>The document bytes.</summary>
        public ReadOnlyMemory<byte> Document { get; }

        /// <summary>MIME type of <see cref="Document"/>. Default "application/pdf".</summary>
        public string ContentType { get; }

        /// <summary>Page count when a rasteriser was configured to count pages; otherwise null.</summary>
        public int? PageCount { get; }
    }
}
