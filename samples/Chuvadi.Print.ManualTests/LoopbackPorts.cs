using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Loopback ports for the fake printers and hubs in Print's tests, without races between tests that run in
    /// parallel.
    /// </summary>
    /// <remarks>
    /// <para>A <see cref="TcpListener"/> fake binds port 0 and reads back the port it got; it is listening (and so
    /// accepting into its backlog) as soon as <c>Start</c> returns, before the port is handed to the test.</para>
    /// <para><see cref="HttpListener"/> cannot bind port 0, so <see cref="StartHttpListener"/> starts it on a free
    /// candidate port. If another socket took that port in the meantime, <c>Start</c> fails and it tries the next
    /// one. A port held by another live fake in this process is never handed out (HttpListener would otherwise
    /// let two fakes share a port, and the first one disposed takes it from the other); the fake gives it back
    /// with <see cref="Release"/> when it is disposed. It returns only once a TCP connection to the port
    /// succeeds.</para>
    /// <para>A test that needs a port where nothing answers takes a <see cref="ReservedPort"/>: a socket bound
    /// to the port and never listening, so no other test can take the port while connections to it are refused
    /// (TCP) or never answered (UDP).</para>
    /// </remarks>
    public static class LoopbackPorts
    {
        /// <summary>How many candidate ports <see cref="StartHttpListener"/> tries before giving up.</summary>
        public const int Attempts = 50;

        /// <summary>How long <see cref="WaitUntilAccepting"/> waits by default.</summary>
        public static readonly TimeSpan AcceptTimeout = TimeSpan.FromSeconds(10);

        private static readonly object Gate = new();
        private static readonly HashSet<int> InUse = new();

        /// <summary>
        /// Starts an <see cref="HttpListener"/> on a free loopback port. <paramref name="prefixFor"/> makes the
        /// listener prefix for a port (for example <c>p =&gt; "http://localhost:" + p + "/"</c>).
        /// </summary>
        public static HttpListener StartHttpListener(Func<int, string> prefixFor, out int port)
        {
            ArgumentNullException.ThrowIfNull(prefixFor);
            Exception? last = null;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                int candidate = Candidate();
                var listener = new HttpListener();
                try
                {
                    listener.Prefixes.Add(prefixFor(candidate));
                    listener.Start();
                }
                catch (Exception ex) when (ex is HttpListenerException or SocketException)
                {
                    last = ex;
                    listener.Close();
                    Release(candidate);
                    continue;
                }

                try
                {
                    WaitUntilAccepting(candidate, AcceptTimeout);
                }
                catch
                {
                    listener.Close();
                    Release(candidate);
                    throw;
                }
                port = candidate;
                return listener;
            }
            throw new InvalidOperationException("No free loopback port for an HttpListener after " + Attempts + " attempts.", last);
        }

        /// <summary>Gives back a port from <see cref="StartHttpListener"/> once its listener is closed.</summary>
        public static void Release(int port)
        {
            lock (Gate) InUse.Remove(port);
        }

        /// <summary>Waits until a TCP connection to the loopback port succeeds, then closes it.</summary>
        public static void WaitUntilAccepting(int port, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    probe.Connect(new IPEndPoint(IPAddress.Loopback, port));
                    return;
                }
                catch (SocketException) when (watch.Elapsed < timeout)
                {
                    Thread.Sleep(20);
                }
            }
        }

        /// <summary>A TCP port on loopback that nothing listens on, held until disposed: connections are refused.</summary>
        public static ReservedPort ReserveClosedTcpPort() => Reserve(SocketType.Stream, ProtocolType.Tcp);

        /// <summary>A UDP port on loopback that nothing answers on, held until disposed.</summary>
        public static ReservedPort ReserveSilentUdpPort() => Reserve(SocketType.Dgram, ProtocolType.Udp);

        private static ReservedPort Reserve(SocketType type, ProtocolType protocol)
        {
            var socket = new Socket(AddressFamily.InterNetwork, type, protocol);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                int port = ((IPEndPoint)socket.LocalEndPoint!).Port;
                lock (Gate) InUse.Add(port);
                return new ReservedPort(socket, port);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        /// <summary>A port the kernel says is free now and that no live fake in this process holds.</summary>
        private static int Candidate()
        {
            for (int i = 0; i < 1000; i++)
            {
                int port;
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    port = ((IPEndPoint)probe.LocalEndPoint!).Port;
                }
                lock (Gate)
                {
                    if (InUse.Add(port)) return port;
                }
            }
            throw new InvalidOperationException("No free loopback port that no other fake in this process holds.");
        }
    }

    /// <summary>A loopback port held by a bound socket that never listens or answers. Dispose to release it.</summary>
    public sealed class ReservedPort : IDisposable
    {
        private readonly Socket _socket;

        internal ReservedPort(Socket socket, int port)
        {
            _socket = socket;
            Port = port;
        }

        /// <summary>The port number.</summary>
        public int Port { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            _socket.Dispose();
            LoopbackPorts.Release(Port);
        }
    }
}
