using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace ChuvadiPdfPrinter
{
    /// <summary>
    /// Receives print jobs on a named pipe. The fallback queue's port is the pipe (\\.\pipe\&lt;name&gt;), and the
    /// spooler opens the port once per job, so every connection is exactly one job. That is what removes the
    /// fixed-output-file race: there is no shared file, each job has its own connection and its own staging file.
    /// </summary>
    public sealed class PipeCaptureServer
    {
        private readonly Func<NamedPipeServerStream> _createServer;
        private readonly Func<Stream, CancellationToken, Task> _onJob;
        private readonly ILog _log;

        /// <param name="pipeName">Pipe name (without \\.\pipe\).</param>
        /// <param name="onJob">Handles one job's data; the stream ends when the spooler closes the port.</param>
        /// <param name="log">Diagnostics.</param>
        /// <param name="createServer">
        /// Creates one listening pipe instance. Default: an inbound byte pipe with the OS default security; the
        /// Windows agent supplies one with an explicit ACL.
        /// </param>
        /// <param name="maxInstances">Concurrent connections (the spooler uses one per port at a time).</param>
        public PipeCaptureServer(string pipeName, Func<Stream, CancellationToken, Task> onJob, ILog log,
                                 Func<NamedPipeServerStream>? createServer = null, int maxInstances = 4)
        {
            if (string.IsNullOrWhiteSpace(pipeName)) throw new ArgumentException("Pipe name required.", nameof(pipeName));
            PipeName = pipeName;
            _onJob = onJob ?? throw new ArgumentNullException(nameof(onJob));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            MaxInstances = maxInstances;
            _createServer = createServer ?? (() => new NamedPipeServerStream(pipeName, PipeDirection.In, maxInstances,
                                                                             PipeTransmissionMode.Byte, PipeOptions.Asynchronous));
        }

        public string PipeName { get; }
        public int MaxInstances { get; }

        /// <summary>Accepts connections until cancelled; each job is handled on its own task.</summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var running = new List<Task>();
            _log.Info("Listening on pipe " + PipeName);
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream server;
                try
                {
                    server = _createServer();
                }
                catch (IOException ex)
                {
                    // All instances busy (MaxInstances jobs in flight); wait for one to finish.
                    running.RemoveAll(t => t.IsCompleted);
                    if (running.Count < MaxInstances) _log.Error("Could not create pipe instance", ex);
                    try
                    {
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                    break;
                }
                catch (IOException ex)
                {
                    _log.Error("Pipe connection failed", ex);
                    await server.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                running.RemoveAll(t => t.IsCompleted);
                running.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _onJob(server, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Job handler failed", ex);
                    }
                    finally
                    {
                        await server.DisposeAsync().ConfigureAwait(false);
                    }
                }, CancellationToken.None));
            }
            await Task.WhenAll(running).ConfigureAwait(false);
            _log.Info("Stopped listening on pipe " + PipeName);
        }
    }

    /// <summary>The fallback-queue capture: pipe connection -> spooler job metadata -> <see cref="AgentEngine"/>.</summary>
    public sealed class QueueCaptureService
    {
        private readonly AgentEngine _engine;
        private readonly IJobInfoProvider _jobs;
        private readonly Func<NamedPipeServerStream>? _createServer;

        public QueueCaptureService(AgentEngine engine, IJobInfoProvider jobs, Func<NamedPipeServerStream>? createServer = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _createServer = createServer;
        }

        /// <summary>Raised after every job (for the console and tests).</summary>
        public event Action<JobInfo, CaptureResult>? JobCaptured;

        public Task RunAsync(CancellationToken cancellationToken)
            => new PipeCaptureServer(_engine.Config.PipeName, HandleAsync, _engine.Log, _createServer).RunAsync(cancellationToken);

        private Task HandleAsync(Stream stream, CancellationToken cancellationToken)
        {
            // Ask the spooler which job is printing now, while the port is open for it.
            JobInfo job;
            try
            {
                job = SpoolerJobSelector.SelectPrinting(_jobs.GetJobs(_engine.Config.QueueName)) ?? JobInfo.Unknown(DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                _engine.Log.Error("Could not read the queue's jobs; saving with default metadata", ex);
                job = JobInfo.Unknown(DateTimeOffset.Now);
            }
            CaptureResult result = _engine.CaptureFromStream(stream, job, cancellationToken);
            JobCaptured?.Invoke(job, result);
            return Task.CompletedTask;
        }
    }
}
