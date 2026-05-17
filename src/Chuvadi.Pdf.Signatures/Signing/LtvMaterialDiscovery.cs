// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.2.1 (AIA), §4.2.1.13 (CRL DPs)
// PHASE: Phase 1.2.6 — LTV material discovery

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Walks a certificate chain and fetches the validation material (CRLs,
/// OCSP responses) advertised by each certificate's extensions. Used to
/// populate <see cref="LtvOptions"/> without making the caller wire up
/// HTTP fetches by hand.
/// </summary>
/// <remarks>
/// <para>
/// For each cert in the chain (leaf first):
/// <list type="bullet">
///   <item>CRL Distribution Points (RFC 5280 §4.2.1.13) HTTP URLs are
///   fetched and decoded.</item>
///   <item>Authority Information Access (RFC 5280 §4.2.2.1) OCSP URLs
///   are POSTed an OCSP request (built by <c>ocspRequestFactory</c>
///   if supplied; otherwise OCSP is skipped) and the response is decoded.</item>
/// </list>
/// </para>
/// <para>
/// Per-URL failures are tolerated: discovery collects what it can and
/// reports failures via <see cref="DiscoveryHooks.OnFetchFailed"/>.
/// </para>
/// </remarks>
public static class LtvMaterialDiscovery
{
    /// <summary>Optional hooks invoked during discovery.</summary>
    public sealed class DiscoveryHooks
    {
        /// <summary>Invoked whenever a fetch fails. Receives the URL and the exception.</summary>
        public Action<string, Exception>? OnFetchFailed { get; init; }
    }

    /// <summary>
    /// Discovers LTV material for the signer's chain.
    /// </summary>
    /// <param name="chain">The chain ordered leaf → intermediates → root.</param>
    /// <param name="httpClient">HTTP client used to fetch CRLs and OCSP responses.</param>
    /// <param name="ocspRequestFactory">Optional. Builds an OCSP request body for (cert, issuer).
    /// When null, OCSP fetching is skipped.</param>
    /// <param name="hooks">Optional discovery hooks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<LtvOptions> DiscoverAsync(
        IReadOnlyList<X509Certificate> chain,
        HttpClient httpClient,
        Func<X509Certificate, X509Certificate, byte[]>? ocspRequestFactory = null,
        DiscoveryHooks? hooks = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(httpClient);

        List<CertificateList> crls = new();
        List<OcspResponse> ocsps = new();

        for (int i = 0; i < chain.Count; i++)
        {
            X509Certificate cert = chain[i];
            cancellationToken.ThrowIfCancellationRequested();

            foreach (string url in ExtractCrlUrls(cert))
            {
                try
                {
                    byte[] der = await httpClient.GetByteArrayAsync(url, cancellationToken)
                        .ConfigureAwait(false);
                    crls.Add(CertificateList.Decode(der));
                }
                catch (Exception ex)
                {
                    hooks?.OnFetchFailed?.Invoke(url, ex);
                }
            }

            if (ocspRequestFactory is null) { continue; }
            if (i + 1 >= chain.Count) { continue; }
            string? ocspUrl = ExtractOcspUrl(cert);
            if (ocspUrl is null) { continue; }

            X509Certificate issuer = chain[i + 1];
            try
            {
                byte[] reqBytes = ocspRequestFactory(cert, issuer);
                using HttpRequestMessage httpReq = new(HttpMethod.Post, ocspUrl);
                ByteArrayContent content = new(reqBytes);
                content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/ocsp-request");
                httpReq.Content = content;
                using HttpResponseMessage httpResp = await httpClient.SendAsync(httpReq, cancellationToken)
                    .ConfigureAwait(false);
                if (httpResp.IsSuccessStatusCode)
                {
                    byte[] respBytes = await httpResp.Content.ReadAsByteArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                    ocsps.Add(OcspResponse.Decode(respBytes));
                }
                else
                {
                    hooks?.OnFetchFailed?.Invoke(ocspUrl,
                        new HttpRequestException($"OCSP responder returned {(int)httpResp.StatusCode}."));
                }
            }
            catch (Exception ex)
            {
                hooks?.OnFetchFailed?.Invoke(ocspUrl, ex);
            }
        }

        List<X509Certificate>? embedCerts = null;
        if (chain.Count > 1)
        {
            embedCerts = new List<X509Certificate>(chain.Count - 1);
            for (int i = 1; i < chain.Count; i++) { embedCerts.Add(chain[i]); }
        }

        return new LtvOptions
        {
            Certificates = embedCerts,
            Crls = crls.Count > 0 ? crls : null,
            OcspResponses = ocsps.Count > 0 ? ocsps : null,
        };
    }

    private static IEnumerable<string> ExtractCrlUrls(X509Certificate cert)
    {
        X509Extension? ext = FindExtension(cert, KnownOids.CrlDistributionPoints);
        if (ext is null) { yield break; }

        CrlDistributionPointsExtension parsed;
        try { parsed = CrlDistributionPointsExtension.Parse(ext.Value); }
        catch { yield break; }

        foreach (DistributionPoint dp in parsed.Points)
        {
            foreach (GeneralName name in dp.FullName)
            {
                if (name.Kind == GeneralNameKind.UniformResourceIdentifier
                    && name.StringValue is string uri
                    && (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return uri;
                }
            }
        }
    }

    private static string? ExtractOcspUrl(X509Certificate cert)
    {
        X509Extension? ext = FindExtension(cert, KnownOids.AuthorityInfoAccess);
        if (ext is null) { return null; }
        try
        {
            AuthorityInformationAccessExtension parsed
                = AuthorityInformationAccessExtension.Parse(ext.Value);
            return parsed.OcspUri;
        }
        catch { return null; }
    }

    private static X509Extension? FindExtension(X509Certificate cert, ObjectIdentifier oid)
    {
        foreach (X509Extension ext in cert.Tbs.Extensions)
        {
            if (ext.Oid.Equals(oid)) { return ext; }
        }
        return null;
    }
}
