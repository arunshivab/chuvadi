using System;
using System.Threading;

namespace Chuvadi.Print
{
    /// <summary>
    /// Turns a document into <see cref="VectorPage"/>s, for routes that print vectors (the Windows queue through XPS;
    /// PostScript and PCL XL later). The vector counterpart of <see cref="IPageRasterizer"/>: supplied by a PDF package
    /// (the display-list converter in Chuvadi.Print.Pdf.Vector), or by an application that draws its own pages.
    /// </summary>
    public interface IVectorPageSource
    {
        /// <summary>
        /// Opens a document. The returned document may keep a reference to <paramref name="document"/> until it is disposed.
        /// </summary>
        IVectorDocument Open(ReadOnlyMemory<byte> document);
    }

    /// <summary>An opened document that gives one vector page at a time.</summary>
    public interface IVectorDocument : IDisposable
    {
        /// <summary>Number of pages in the document.</summary>
        int PageCount { get; }

        /// <summary>Natural size of a page (1-based), with any intrinsic page rotation applied: the size of the page <see cref="GetPage"/> returns.</summary>
        PageDimensions GetPageSize(int pageNumber);

        /// <summary>
        /// The content of one page (1-based), in points with the origin at the top-left corner. The caller may keep the
        /// page; implementations should not change it afterwards.
        /// </summary>
        VectorPage GetPage(int pageNumber, CancellationToken cancellationToken);
    }
}
