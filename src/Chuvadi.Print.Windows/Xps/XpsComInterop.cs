using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Threading;

// COM declarations for submitting XPS through the Print Document Package API (documenttarget.h) and the XPS Object
// Model (xpsobjectmodel.h, xpsobjectmodel_1.h), plus IStream (objidlbase.h) and connection points (ocidl.h).
//
// Provenance (D-53, D-238): method order and signatures were read from the public-domain mingw-w64 headers of the same
// names and checked against Microsoft's API reference on learn.microsoft.com (see docs/Chuvadi.Print/PROVENANCE-XPS-COM.md).
// No header code is used: these are our own declarations. A vtable slot this library never calls is declared as a
// Reserved method so the slots after it keep their positions; slots after the last one called are left out.
// Every COM pointer passed or returned is an nint, released explicitly (ComRef), so lifetimes are deterministic.
namespace Chuvadi.Print.Windows
{
    internal static class XpsComIds
    {
        internal const string IStream = "0000000c-0000-0000-c000-000000000046";
        internal const string IDispatch = "00020400-0000-0000-c000-000000000046";
        internal const string IPrintDocumentPackageTargetFactory = "d2959bf7-b31b-4a3d-9600-712eb1335ba4";
        internal const string IPrintDocumentPackageTarget = "1b8efec4-3019-4c27-964e-367202156906";
        internal const string IPrintDocumentPackageStatusEvent = "ed90c8ad-5c34-4d05-a1ec-0e8a9b3ad7af";
        internal const string IXpsDocumentPackageTarget = "3b0b6d38-53ad-41da-b212-d37637a6714e";
        internal const string IXpsOMObjectFactory = "f9b2a685-a50d-4fc2-b764-b56e093ea0ca";
        internal const string IXpsOMPackage = "18c3df65-81e1-4674-91dc-fc452f5a416f";
        internal const string IXpsOMDocumentSequence = "56492eb4-d8d5-425e-8256-4c2b64ad0264";
        internal const string IXpsOMDocumentCollection = "d1c87f0d-e947-4754-8a25-971478f7e83e";
        internal const string IXpsOMDocument = "2c2c94cb-ac5f-4254-8ee9-23948309d9f0";
        internal const string IXpsOMPageReferenceCollection = "ca16ba4d-e7b9-45c5-958b-f98022473745";
        internal const string IXpsOMPageReference = "ed360180-6f92-4998-890d-2f208531a0a0";
        internal const string IXpsOMPackageWriter = "4e2aa182-a443-42c6-b41b-4f8e9de73ff9";
        internal const string IConnectionPointContainer = "b196b284-bab4-101a-b69c-00aa00341d07";
        internal const string IConnectionPoint = "b196b286-bab4-101a-b69c-00aa00341d07";

        /// <summary>CLSID_PrintDocumentPackageTargetFactory.</summary>
        internal static readonly Guid PrintDocumentPackageTargetFactoryClass = new("348ef17d-6c81-4982-92b4-ee188a43867a");

        /// <summary>ID_DOCUMENTPACKAGETARGET_MSXPS: the package target for XPS 1.0 (the Microsoft XPS namespace, which XpsWriter writes).</summary>
        internal static readonly Guid TargetTypeMsXps = new("9cae40a8-ded1-41c9-a9fd-d735ef33aeda");

        /// <summary>ID_DOCUMENTPACKAGETARGET_OPENXPS.</summary>
        internal static readonly Guid TargetTypeOpenXps = new("0056bb72-8c9c-4612-bd0f-93012a87099d");
    }

    /// <summary>XPS_SIZE: a width and height in XPS units (1/96 inch).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XpsSize
    {
        public float Width;
        public float Height;
    }

    /// <summary>PrintDocumentPackageStatus.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PrintDocumentPackageStatus
    {
        public uint JobId;
        public int CurrentDocument;
        public int CurrentPage;
        public int CurrentPageTotal;
        public int Completion;     // PrintDocumentPackageCompletion: 0 InProgress, 1 Completed, 2 Canceled, 3 Failed
        public int PackageStatus;  // HRESULT
    }

    /// <summary>IStream (with ISequentialStream's Read and Write first, as in its vtable).</summary>
    [GeneratedComInterface]
    [Guid(XpsComIds.IStream)]
    internal partial interface IComStream
    {
        [PreserveSig] int Read(nint buffer, uint count, out uint read);
        [PreserveSig] int Write(nint buffer, uint count, out uint written);
        [PreserveSig] int Seek(long move, uint origin, out ulong newPosition);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid(XpsComIds.IPrintDocumentPackageTargetFactory)]
    internal partial interface IPrintDocumentPackageTargetFactory
    {
        void CreateDocumentPackageTargetForPrintJob(string printerName, string jobName, nint jobOutputStream,
                                                    nint jobPrintTicketStream, out nint docPackageTarget);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IPrintDocumentPackageTarget)]
    internal partial interface IPrintDocumentPackageTarget
    {
        void GetPackageTargetTypes(out uint targetCount, out nint targetTypes);
        void GetPackageTarget(in Guid guidTargetType, in Guid riid, out nint target);
        void Cancel();
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsDocumentPackageTarget)]
    internal partial interface IXpsDocumentPackageTarget
    {
        void GetXpsOMPackageWriter(nint documentSequencePartName, nint discardControlPartName, out nint packageWriter);
        void GetXpsOMFactory(out nint xpsFactory);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid(XpsComIds.IXpsOMObjectFactory)]
    internal partial interface IXpsOMObjectFactory
    {
        [PreserveSig] int Reserved00CreatePackage();
        [PreserveSig] int Reserved01CreatePackageFromFile();
        void CreatePackageFromStream(nint stream, int reuseObjects, out nint package);
        [PreserveSig] int Reserved03();
        [PreserveSig] int Reserved04();
        [PreserveSig] int Reserved05();
        [PreserveSig] int Reserved06();
        [PreserveSig] int Reserved07();
        [PreserveSig] int Reserved08();
        [PreserveSig] int Reserved09();
        [PreserveSig] int Reserved10();
        [PreserveSig] int Reserved11();
        [PreserveSig] int Reserved12();
        [PreserveSig] int Reserved13();
        [PreserveSig] int Reserved14();
        [PreserveSig] int Reserved15();
        [PreserveSig] int Reserved16();
        [PreserveSig] int Reserved17();
        [PreserveSig] int Reserved18();
        [PreserveSig] int Reserved19();
        [PreserveSig] int Reserved20();
        [PreserveSig] int Reserved21();
        [PreserveSig] int Reserved22();
        [PreserveSig] int Reserved23();
        [PreserveSig] int Reserved24();
        [PreserveSig] int Reserved25();
        [PreserveSig] int Reserved26();
        [PreserveSig] int Reserved27();
        [PreserveSig] int Reserved28();
        [PreserveSig] int Reserved29();
        [PreserveSig] int Reserved30();
        [PreserveSig] int Reserved31();
        [PreserveSig] int Reserved32();
        [PreserveSig] int Reserved33CreatePackageWriterOnFile();
        [PreserveSig] int Reserved34CreatePackageWriterOnStream();
        void CreatePartUri(string uri, out nint partUri);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsOMPackage)]
    internal partial interface IXpsOMPackage
    {
        void GetDocumentSequence(out nint documentSequence);
    }

    /// <summary>IXpsOMDocumentSequence, with the two IXpsOMPart methods it inherits first.</summary>
    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsOMDocumentSequence)]
    internal partial interface IXpsOMDocumentSequence
    {
        void GetPartName(out nint partUri);
        [PreserveSig] int Reserved01SetPartName();
        [PreserveSig] int Reserved02GetOwner();
        void GetDocuments(out nint documents);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsOMDocumentCollection)]
    internal partial interface IXpsOMDocumentCollection
    {
        void GetCount(out uint count);
        void GetAt(uint index, out nint document);
    }

    /// <summary>IXpsOMDocument, with the two IXpsOMPart methods it inherits first.</summary>
    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsOMDocument)]
    internal partial interface IXpsOMDocument
    {
        void GetPartName(out nint partUri);
        [PreserveSig] int Reserved01SetPartName();
        [PreserveSig] int Reserved02GetOwner();
        void GetPageReferences(out nint pageReferences);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsOMPageReferenceCollection)]
    internal partial interface IXpsOMPageReferenceCollection
    {
        void GetCount(out uint count);
        void GetAt(uint index, out nint pageReference);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsOMPageReference)]
    internal partial interface IXpsOMPageReference
    {
        [PreserveSig] int Reserved00GetOwner();
        void GetPage(out nint page);
        [PreserveSig] int Reserved02SetPage();
        void DiscardPage();
        [PreserveSig] int Reserved04IsPageLoaded();
        void GetAdvisoryPageDimensions(out XpsSize pageDimensions);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IXpsOMPackageWriter)]
    internal partial interface IXpsOMPackageWriter
    {
        void StartNewDocument(nint documentPartName, nint documentPrintTicket, nint documentStructure,
                              nint signatureBlockResources, nint restrictedFonts);
        void AddPage(nint page, in XpsSize advisoryPageDimensions, nint discardableResourceParts, nint storyFragments,
                     nint pagePrintTicket, nint pageThumbnail);
        [PreserveSig] int Reserved02AddResource();
        void Close();
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IConnectionPointContainer)]
    internal partial interface IConnectionPointContainer
    {
        [PreserveSig] int Reserved00EnumConnectionPoints();
        void FindConnectionPoint(in Guid riid, out nint connectionPoint);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IConnectionPoint)]
    internal partial interface IConnectionPoint
    {
        [PreserveSig] int Reserved00GetConnectionInterface();
        [PreserveSig] int Reserved01GetConnectionPointContainer();
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
    }

    /// <summary>IDispatch's four methods, the base of IPrintDocumentPackageStatusEvent.</summary>
    [GeneratedComInterface]
    [Guid(XpsComIds.IDispatch)]
    internal partial interface IDispatchSlots
    {
        [PreserveSig] int GetTypeInfoCount(out uint count);
        [PreserveSig] int GetTypeInfo(uint index, uint localeId, out nint typeInfo);
        [PreserveSig] int GetIDsOfNames(in Guid riid, nint names, uint nameCount, uint localeId, nint dispIds);
        [PreserveSig] int Invoke(int dispId, in Guid riid, uint localeId, ushort flags, nint parameters, nint result,
                                 nint exceptionInfo, nint argumentError);
    }

    [GeneratedComInterface]
    [Guid(XpsComIds.IPrintDocumentPackageStatusEvent)]
    internal partial interface IPrintDocumentPackageStatusEvent : IDispatchSlots
    {
        [PreserveSig] int PackageStatusUpdated(nint packageStatus);
    }

    /// <summary>
    /// Receives the job's status from the print system. Called on a thread of the print system's choosing; keeps the latest
    /// status and signals when the job is no longer in progress. IDispatch is not supported (late binding is not used by
    /// this library); the print system calls <see cref="PackageStatusUpdated"/> through the vtable.
    /// </summary>
    [GeneratedComClass]
    [SupportedOSPlatform("windows")]
    internal sealed partial class PackageStatusSink : IPrintDocumentPackageStatusEvent, IDisposable
    {
        private const int S_OK = 0;
        private const int E_NOTIMPL = unchecked((int)0x80004001);
        private const int E_POINTER = unchecked((int)0x80004003);

        private readonly object _lock = new();
        private readonly ManualResetEventSlim _finished = new(false);
        private PrintDocumentPackageStatus _latest;
        private int _updates;

        /// <summary>Set once the completion is anything but in progress.</summary>
        public WaitHandle Finished => _finished.WaitHandle;

        /// <summary>How many status updates have arrived.</summary>
        public int Updates
        {
            get { lock (_lock) return _updates; }
        }

        /// <summary>The latest status, or null before the first update.</summary>
        public PrintDocumentPackageStatus? Latest
        {
            get { lock (_lock) return _updates == 0 ? null : _latest; }
        }

        public int GetTypeInfoCount(out uint count)
        {
            count = 0;
            return S_OK;
        }

        public int GetTypeInfo(uint index, uint localeId, out nint typeInfo)
        {
            typeInfo = 0;
            return E_NOTIMPL;
        }

        public int GetIDsOfNames(in Guid riid, nint names, uint nameCount, uint localeId, nint dispIds) => E_NOTIMPL;

        public int Invoke(int dispId, in Guid riid, uint localeId, ushort flags, nint parameters, nint result,
                          nint exceptionInfo, nint argumentError) => E_NOTIMPL;

        public unsafe int PackageStatusUpdated(nint packageStatus)
        {
            if (packageStatus == 0) return E_POINTER;
            PrintDocumentPackageStatus status = *(PrintDocumentPackageStatus*)packageStatus;
            lock (_lock)
            {
                _latest = status;
                _updates++;
            }
            if (status.Completion != 0) _finished.Set();
            return S_OK;
        }

        public void Dispose() => _finished.Dispose();
    }

    /// <summary>
    /// One reference to a COM object: the raw pointer (passed to other COM calls) and a typed wrapper (for calling it).
    /// Dispose releases both.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class ComRef<T> : IDisposable where T : class
    {
        private static readonly StrategyBasedComWrappers Wrappers = new();
        private nint _pointer;
        private T? _object;

        private ComRef(nint pointer, T obj)
        {
            _pointer = pointer;
            _object = obj;
        }

        /// <summary>Takes over one reference the caller holds on <paramref name="pointer"/>. A null pointer is an error.</summary>
        public static ComRef<T> Attach(nint pointer, string what)
        {
            if (pointer == 0) throw new InvalidOperationException(what + " returned no object.");
            try
            {
                var obj = (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
                return new ComRef<T>(pointer, obj);
            }
            catch
            {
                Marshal.Release(pointer);
                throw;
            }
        }

        /// <summary>The same object seen through another interface (QueryInterface); null when it does not have it.</summary>
        public ComRef<TOther>? As<TOther>(Guid iid) where TOther : class
        {
            if (Marshal.QueryInterface(Pointer, in iid, out nint other) != 0 || other == 0) return null;
            return ComRef<TOther>.Attach(other, typeof(TOther).Name);
        }

        public nint Pointer => _pointer != 0 ? _pointer : throw new ObjectDisposedException(typeof(T).Name);

        public T Object => _object ?? throw new ObjectDisposedException(typeof(T).Name);

        public void Dispose()
        {
            T? obj = _object;
            nint pointer = _pointer;
            _object = null;
            _pointer = 0;
            if (obj is ComObject wrapper) wrapper.FinalRelease();
            if (pointer != 0) Marshal.Release(pointer);
        }
    }
    /// <summary>One reference to a COM object that is only passed on to other calls, never called itself. Dispose releases it.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class ComPointer : IDisposable
    {
        private nint _pointer;

        private ComPointer(nint pointer) => _pointer = pointer;

        /// <summary>Takes over one reference the caller holds on <paramref name="pointer"/>. A null pointer is an error.</summary>
        public static ComPointer Attach(nint pointer, string what)
            => pointer != 0 ? new ComPointer(pointer) : throw new InvalidOperationException(what + " returned no object.");

        public nint Pointer => _pointer != 0 ? _pointer : throw new ObjectDisposedException(nameof(ComPointer));

        public void Dispose()
        {
            nint pointer = _pointer;
            _pointer = 0;
            if (pointer != 0) Marshal.Release(pointer);
        }
    }
}
