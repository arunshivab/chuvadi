namespace Chuvadi.Print.Ipp
{
    /// <summary>IPP delimiter and value tags (RFC 8010 section 3.5, IANA IPP registry).</summary>
    public enum IppTag : byte
    {
        // Delimiters (attribute groups)
        OperationAttributes = 0x01,
        JobAttributes = 0x02,
        EndOfAttributes = 0x03,
        PrinterAttributes = 0x04,
        UnsupportedAttributes = 0x05,
        SubscriptionAttributes = 0x06,
        EventNotificationAttributes = 0x07,
        ResourceAttributes = 0x08,
        DocumentAttributes = 0x09,
        SystemAttributes = 0x0A,

        // Out-of-band values
        Unsupported = 0x10,
        Unknown = 0x12,
        NoValue = 0x13,
        NotSettable = 0x15,
        DeleteAttribute = 0x16,
        AdminDefine = 0x17,

        // Integer values
        Integer = 0x21,
        Boolean = 0x22,
        Enum = 0x23,

        // Octet-string values
        OctetString = 0x30,
        DateTime = 0x31,
        Resolution = 0x32,
        RangeOfInteger = 0x33,
        BeginCollection = 0x34,
        TextWithLanguage = 0x35,
        NameWithLanguage = 0x36,
        EndCollection = 0x37,

        // Character-string values
        TextWithoutLanguage = 0x41,
        NameWithoutLanguage = 0x42,
        Keyword = 0x44,
        Uri = 0x45,
        UriScheme = 0x46,
        Charset = 0x47,
        NaturalLanguage = 0x48,
        MimeMediaType = 0x49,
        MemberAttrName = 0x4A,

        /// <summary>Extension: the real tag follows as four bytes in the value.</summary>
        Extension = 0x7F
    }

    /// <summary>IPP operations (IANA registry; the common printing set plus job management).</summary>
    public enum IppOperation : short
    {
        PrintJob = 0x0002,
        PrintUri = 0x0003,
        ValidateJob = 0x0004,
        CreateJob = 0x0005,
        SendDocument = 0x0006,
        SendUri = 0x0007,
        CancelJob = 0x0008,
        GetJobAttributes = 0x0009,
        GetJobs = 0x000A,
        GetPrinterAttributes = 0x000B,
        HoldJob = 0x000C,
        ReleaseJob = 0x000D,
        RestartJob = 0x000E,
        PausePrinter = 0x0010,
        ResumePrinter = 0x0011,
        PurgeJobs = 0x0012,
        SetPrinterAttributes = 0x0013,
        SetJobAttributes = 0x0014,
        GetPrinterSupportedValues = 0x0015,
        CloseJob = 0x003B,
        IdentifyPrinter = 0x003C,
        ValidateDocument = 0x003D,
        CancelMyJobs = 0x0039
    }

    /// <summary>IPP job states (job-state enum).</summary>
    public enum IppJobState
    {
        Pending = 3,
        PendingHeld = 4,
        Processing = 5,
        ProcessingStopped = 6,
        Canceled = 7,
        Aborted = 8,
        Completed = 9
    }

    /// <summary>IPP printer states (printer-state enum).</summary>
    public enum IppPrinterState
    {
        Idle = 3,
        Processing = 4,
        Stopped = 5
    }

    /// <summary>IPP print-quality enum values.</summary>
    public enum IppPrintQuality
    {
        Draft = 3,
        Normal = 4,
        High = 5
    }

    /// <summary>Units of an IPP resolution value.</summary>
    public enum IppResolutionUnits : byte
    {
        DotsPerInch = 3,
        DotsPerCentimetre = 4
    }

    /// <summary>Well-known IPP status codes. Any 16-bit code can be carried; see <see cref="IppStatus"/>.</summary>
    public enum IppStatusCode : short
    {
        SuccessfulOk = 0x0000,
        SuccessfulOkIgnoredOrSubstitutedAttributes = 0x0001,
        SuccessfulOkConflictingAttributes = 0x0002,
        ClientErrorBadRequest = 0x0400,
        ClientErrorForbidden = 0x0401,
        ClientErrorNotAuthenticated = 0x0402,
        ClientErrorNotAuthorized = 0x0403,
        ClientErrorNotPossible = 0x0404,
        ClientErrorTimeout = 0x0405,
        ClientErrorNotFound = 0x0406,
        ClientErrorGone = 0x0407,
        ClientErrorRequestEntityTooLarge = 0x0408,
        ClientErrorRequestValueTooLong = 0x0409,
        ClientErrorDocumentFormatNotSupported = 0x040A,
        ClientErrorAttributesOrValuesNotSupported = 0x040B,
        ClientErrorUriSchemeNotSupported = 0x040C,
        ClientErrorCharsetNotSupported = 0x040D,
        ClientErrorConflictingAttributes = 0x040E,
        ClientErrorCompressionNotSupported = 0x040F,
        ClientErrorCompressionError = 0x0410,
        ClientErrorDocumentFormatError = 0x0411,
        ClientErrorDocumentAccessError = 0x0412,
        ServerErrorInternalError = 0x0500,
        ServerErrorOperationNotSupported = 0x0501,
        ServerErrorServiceUnavailable = 0x0502,
        ServerErrorVersionNotSupported = 0x0503,
        ServerErrorDeviceError = 0x0504,
        ServerErrorTemporaryError = 0x0505,
        ServerErrorNotAcceptingJobs = 0x0506,
        ServerErrorBusy = 0x0507,
        ServerErrorJobCanceled = 0x0508,
        ServerErrorMultipleDocumentJobsNotSupported = 0x0509
    }

    /// <summary>Helpers for IPP status codes.</summary>
    public static class IppStatus
    {
        /// <summary>True for the successful-ok range (0x0000-0x00FF).</summary>
        public static bool IsSuccess(short code) => code >= 0 && code <= 0x00FF;
    }
}
