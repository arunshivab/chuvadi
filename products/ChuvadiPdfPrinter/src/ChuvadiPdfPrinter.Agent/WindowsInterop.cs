using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ChuvadiPdfPrinter.Agent
{
    /// <summary>Reads a print queue's jobs from the spooler (EnumJobs level 1).</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class SpoolerJobProvider : IJobInfoProvider
    {
        private const uint JOB_STATUS_FIRST = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOB_INFO_1
        {
            public uint JobId;
            public IntPtr pPrinterName;
            public IntPtr pMachineName;
            public IntPtr pUserName;
            public IntPtr pDocument;
            public IntPtr pDatatype;
            public IntPtr pStatus;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint TotalPages;
            public uint PagesPrinted;
            public SYSTEMTIME Submitted;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool OpenPrinterW(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", ExactSpelling = true, SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", ExactSpelling = true, SetLastError = true)]
        private static extern bool EnumJobsW(IntPtr hPrinter, uint firstJob, uint noJobs, uint level, IntPtr pJob,
                                             uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        public IReadOnlyList<JobInfo> GetJobs(string queueName)
        {
            if (!OpenPrinterW(queueName, out IntPtr printer, IntPtr.Zero))
                throw new IOException("Cannot open print queue '" + queueName + "' (Win32 error " + Marshal.GetLastWin32Error() + ").");
            try
            {
                EnumJobsW(printer, JOB_STATUS_FIRST, 255, 1, IntPtr.Zero, 0, out uint needed, out _);
                if (needed == 0) return Array.Empty<JobInfo>();
                IntPtr buffer = Marshal.AllocHGlobal((int)needed);
                try
                {
                    if (!EnumJobsW(printer, JOB_STATUS_FIRST, 255, 1, buffer, needed, out _, out uint returned))
                        throw new IOException("EnumJobs failed (Win32 error " + Marshal.GetLastWin32Error() + ").");
                    int size = Marshal.SizeOf<JOB_INFO_1>();
                    var jobs = new List<JobInfo>((int)returned);
                    for (int i = 0; i < returned; i++)
                    {
                        var info = Marshal.PtrToStructure<JOB_INFO_1>(buffer + i * size);
                        jobs.Add(new JobInfo
                        {
                            JobId = (int)info.JobId,
                            Title = Marshal.PtrToStringUni(info.pDocument) ?? string.Empty,
                            UserName = Marshal.PtrToStringUni(info.pUserName) ?? string.Empty,
                            MachineName = Marshal.PtrToStringUni(info.pMachineName) ?? string.Empty,
                            Status = (SpoolJobStatus)info.Status,
                            TotalPages = (int)info.TotalPages,
                            Submitted = ToLocal(info.Submitted)
                        });
                    }
                    return jobs;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                ClosePrinter(printer);
            }
        }

        private static DateTimeOffset ToLocal(SYSTEMTIME t)
        {
            try
            {
                return new DateTimeOffset(new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, t.Milliseconds, DateTimeKind.Utc)).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTimeOffset.Now;
            }
        }
    }

    /// <summary>Security for the capture pipe and the per-user output folders.</summary>
    [SupportedOSPlatform("windows")]
    internal static class AgentSecurity
    {
        /// <summary>
        /// The capture pipe: SYSTEM and Administrators full control; authenticated users may write (the spooler
        /// may open the port while impersonating the printing user). No anonymous access.
        /// </summary>
        public static NamedPipeServerStream CreatePipe(string pipeName, int maxInstances)
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                                                      PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                                                      PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                                                      PipeAccessRights.ReadWrite, AccessControlType.Allow));
            return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.In, maxInstances, PipeTransmissionMode.Byte,
                                                   PipeOptions.Asynchronous, 0, 0, security);
        }

        /// <summary>Makes a user's output folder private: that user, SYSTEM and Administrators only.</summary>
        public static void MakePrivate(string folder, string userName)
        {
            var account = new NTAccount(userName);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Modify, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                                                            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                                                            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(folder).SetAccessControl(security);
        }
    }

    /// <summary>Minimal Windows service host over the Service Control Manager API (no extra packages).</summary>
    [SupportedOSPlatform("windows")]
    internal static class WindowsService
    {
        private const uint SERVICE_WIN32_OWN_PROCESS = 0x10;
        private const uint SERVICE_STOPPED = 1, SERVICE_START_PENDING = 2, SERVICE_STOP_PENDING = 3, SERVICE_RUNNING = 4;
        private const uint SERVICE_ACCEPT_STOP = 1, SERVICE_ACCEPT_SHUTDOWN = 4;
        private const uint SERVICE_CONTROL_STOP = 1, SERVICE_CONTROL_INTERROGATE = 4, SERVICE_CONTROL_SHUTDOWN = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_TABLE_ENTRY
        {
            public IntPtr lpServiceName;
            public IntPtr lpServiceProc;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void ServiceMainProc(int argc, IntPtr argv);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint HandlerProc(uint control, uint eventType, IntPtr eventData, IntPtr context);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool StartServiceCtrlDispatcherW([In] SERVICE_TABLE_ENTRY[] table);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr RegisterServiceCtrlHandlerExW(string serviceName, HandlerProc handler, IntPtr context);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref SERVICE_STATUS status);

        // Delegates must stay alive for the lifetime of the process.
        private static ServiceMainProc? _main;
        private static HandlerProc? _handler;
        private static IntPtr _statusHandle;
        private static SERVICE_STATUS _status;
        private static string _name = string.Empty;
        private static Action<System.Threading.CancellationToken>? _run;
        private static readonly System.Threading.CancellationTokenSource Stop = new();

        /// <summary>Runs <paramref name="run"/> as the service until the SCM stops it. Blocks.</summary>
        public static void Run(string serviceName, Action<System.Threading.CancellationToken> run)
        {
            _name = serviceName;
            _run = run;
            _main = ServiceMain;
            _handler = Handler;
            IntPtr name = Marshal.StringToHGlobalUni(serviceName);
            try
            {
                var table = new[]
                {
                    new SERVICE_TABLE_ENTRY { lpServiceName = name, lpServiceProc = Marshal.GetFunctionPointerForDelegate(_main) },
                    new SERVICE_TABLE_ENTRY()
                };
                if (!StartServiceCtrlDispatcherW(table))
                    throw new InvalidOperationException("Not started by the Service Control Manager (Win32 error "
                                                        + Marshal.GetLastWin32Error() + "). Use --console to run interactively.");
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }
        }

        private static void ServiceMain(int argc, IntPtr argv)
        {
            _statusHandle = RegisterServiceCtrlHandlerExW(_name, _handler!, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero) return;
            _status = new SERVICE_STATUS { dwServiceType = SERVICE_WIN32_OWN_PROCESS };
            Report(SERVICE_START_PENDING, 0, 10_000);
            Report(SERVICE_RUNNING, 0, 0);
            uint exitCode = 0;
            try
            {
                _run!(Stop.Token);
            }
            catch (Exception)
            {
                exitCode = 1064; // ERROR_EXCEPTION_IN_SERVICE
            }
            Report(SERVICE_STOPPED, exitCode, 0);
        }

        private static uint Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
        {
            switch (control)
            {
                case SERVICE_CONTROL_STOP:
                case SERVICE_CONTROL_SHUTDOWN:
                    Report(SERVICE_STOP_PENDING, 0, 15_000);
                    Stop.Cancel();
                    return 0;
                case SERVICE_CONTROL_INTERROGATE:
                    SetServiceStatus(_statusHandle, ref _status);
                    return 0;
                default:
                    return 120; // ERROR_CALL_NOT_IMPLEMENTED
            }
        }

        private static void Report(uint state, uint exitCode, uint waitHint)
        {
            _status.dwCurrentState = state;
            _status.dwWin32ExitCode = exitCode;
            _status.dwWaitHint = waitHint;
            _status.dwControlsAccepted = state == SERVICE_RUNNING ? SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN : 0;
            _status.dwCheckPoint = state is SERVICE_RUNNING or SERVICE_STOPPED ? 0 : _status.dwCheckPoint + 1;
            SetServiceStatus(_statusHandle, ref _status);
        }
    }
}
