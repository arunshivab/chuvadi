using System;
using System.Threading;

namespace Chuvadi.Print
{
    /// <summary>
    /// Turns document bytes (PDF) into page rasters. Chuvadi.Print never rasterises by itself;
    /// an implementation is supplied by an adapter package (Chuvadi.Print.Pdf) or by the host.
    /// </summary>
    public interface IPageRasterizer
    {
        /// <summary>
        /// Opens a document for rendering. The returned document may keep a reference to
        /// <paramref name="document"/> until it is disposed.
        /// </summary>
        IRasterDocument Open(ReadOnlyMemory<byte> document);
    }

    /// <summary>An opened document that renders one page at a time.</summary>
    public interface IRasterDocument : IDisposable
    {
        /// <summary>Number of pages in the document.</summary>
        int PageCount { get; }

        /// <summary>Natural size of a page (1-based), with any intrinsic page rotation applied.</summary>
        PageDimensions GetPageSize(int pageNumber);

        /// <summary>
        /// Renders one page (1-based). The caller owns the returned page and disposes it.
        /// Implementations should render into <see cref="RasterPage.Rent"/> buffers so memory stays bounded.
        /// </summary>
        RasterPage Render(int pageNumber, RasterRequest request, CancellationToken cancellationToken);
    }
}
