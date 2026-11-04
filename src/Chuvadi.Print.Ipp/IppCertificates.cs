using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>
    /// Certificate pinning helpers for ipps:// printers (<see cref="IppClientOptions.PinnedCertificates"/>). Typical
    /// use: read the printer's certificate once with <see cref="GetServerCertificate"/>, show its
    /// <see cref="Fingerprint"/> to an administrator, who compares it with the one on the printer's own web page or
    /// configuration report, then save the fingerprint and pin it.
    /// </summary>
    public static class IppCertificates
    {
        /// <summary>The certificate's SHA-256 fingerprint as upper-case hex pairs separated by colons.</summary>
        public static string Fingerprint(X509Certificate certificate)
        {
            if (certificate is null) throw new ArgumentNullException(nameof(certificate));
            byte[] hash = SHA256.HashData(certificate.GetRawCertData());
            var sb = new StringBuilder(hash.Length * 3);
            for (int i = 0; i < hash.Length; i++)
            {
                if (i > 0) sb.Append(':');
                sb.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>True when the certificate's SHA-256 fingerprint equals <paramref name="fingerprint"/> (any hex format).</summary>
        public static bool Matches(X509Certificate certificate, string fingerprint)
        {
            if (certificate is null) throw new ArgumentNullException(nameof(certificate));
            if (fingerprint is null) throw new ArgumentNullException(nameof(fingerprint));
            return MatchesAny(certificate, new[] { Normalize(fingerprint) });
        }

        /// <summary>
        /// Connects to an ipps:// or https:// printer, completes the TLS handshake without judging the certificate,
        /// and returns the certificate the printer presented. Nothing is sent after the handshake.
        /// </summary>
        /// <exception cref="ArgumentException">The URI is not ipps:// or https://.</exception>
        /// <exception cref="IppException">The printer could not be reached or the handshake failed.</exception>
        public static X509Certificate2 GetServerCertificate(string printerUri, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Uri http = IppClient.ToHttpUri(printerUri);
            if (!string.Equals(http.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only ipps:// and https:// printers present a certificate: " + printerUri, nameof(printerUri));
            TimeSpan limit = timeout ?? TimeSpan.FromSeconds(30);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(limit);
            X509Certificate2? presented = null;
            try
            {
                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.ConnectAsync(http.IdnHost, http.Port, cts.Token).AsTask().GetAwaiter().GetResult();
                using var network = new NetworkStream(socket, ownsSocket: false);
                using var tls = new SslStream(network, leaveInnerStreamOpen: false);
                var options = new SslClientAuthenticationOptions
                {
                    TargetHost = http.IdnHost,
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        if (certificate is not null) presented = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                        return true;
                    }
                };
                tls.AuthenticateAsClientAsync(options, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                throw new IppException("Timed out reading the certificate of " + http, innerException: ex);
            }
            catch (Exception ex) when (ex is SocketException || ex is System.IO.IOException || ex is System.Security.Authentication.AuthenticationException)
            {
                throw new IppException("Could not read the certificate of " + http + ": " + ex.Message, innerException: ex);
            }
            return presented ?? throw new IppException("The printer at " + http + " presented no certificate.");
        }

        internal static string Normalize(string fingerprint)
        {
            var sb = new StringBuilder(64);
            foreach (char c in fingerprint)
            {
                if (c == ':' || c == ' ' || c == '-' || c == '\t') continue;
                if (!Uri.IsHexDigit(c)) throw new ArgumentException("A certificate fingerprint must be hex: " + fingerprint, nameof(fingerprint));
                sb.Append(char.ToUpperInvariant(c));
            }
            if (sb.Length != 64) throw new ArgumentException("A SHA-256 fingerprint has 64 hex digits: " + fingerprint, nameof(fingerprint));
            return sb.ToString();
        }

        internal static string[] NormalizeAll(IEnumerable<string> fingerprints)
        {
            var list = new List<string>();
            foreach (string f in fingerprints) list.Add(Normalize(f ?? throw new ArgumentException("A pinned certificate fingerprint is null.")));
            return list.ToArray();
        }

        internal static bool MatchesAny(X509Certificate? certificate, string[] normalizedPins)
        {
            if (certificate is null) return false;
            string actual = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
            byte[] actualBytes = Encoding.ASCII.GetBytes(actual);
            foreach (string pin in normalizedPins)
                if (CryptographicOperations.FixedTimeEquals(actualBytes, Encoding.ASCII.GetBytes(pin))) return true;
            return false;
        }
    }
}
