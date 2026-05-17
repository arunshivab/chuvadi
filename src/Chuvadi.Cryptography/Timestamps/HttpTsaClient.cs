// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3161 §3.4 — Time-Stamp Protocol via HTTP
// PHASE: Phase 1.2.3 — TSA fetching

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// An <see cref="ITsaClient"/> that POSTs RFC 3161 requests over HTTP(S)
/// using <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per RFC 3161 §3.4, the request body has Content-Type
/// <c>application/timestamp-query</c> and the response Content-Type is
/// <c>application/timestamp-reply</c>.
/// </para>
/// <para>
/// Both a TSA URL and an <see cref="HttpClient"/> are required. Callers
/// own the <see cref="HttpClient"/> and are responsible for its
/// disposal; the constructor does not take ownership. This allows
/// reusing one <see cref="HttpClient"/> across many TSA fetches (which
/// is the recommended .NET pattern) and lets callers configure
/// timeouts, proxies, authentication, and so on.
/// </para>
/// </remarks>
public sealed class HttpTsaClient : ITsaClient
{
    private static readonly MediaTypeHeaderValue QueryContentType =
        new("application/timestamp-query");

    private readonly HttpClient _httpClient;
    private readonly Uri _tsaUri;

    /// <summary>Initialises a new HTTP TSA client.</summary>
    /// <param name="httpClient">The HTTP client to use. Not owned.</param>
    /// <param name="tsaUri">The TSA endpoint URL.</param>
    public HttpTsaClient(HttpClient httpClient, Uri tsaUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tsaUri);
        _httpClient = httpClient;
        _tsaUri = tsaUri;
    }

    /// <summary>Initialises a new HTTP TSA client.</summary>
    /// <param name="httpClient">The HTTP client to use. Not owned.</param>
    /// <param name="tsaUrl">The TSA endpoint URL as a string.</param>
    public HttpTsaClient(HttpClient httpClient, string tsaUrl)
        : this(httpClient, new Uri(tsaUrl ?? throw new ArgumentNullException(nameof(tsaUrl))))
    {
    }

    /// <inheritdoc />
    public TimeStampResponse Fetch(TimeStampRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[] body = request.Encode();
        using HttpRequestMessage httpReq = new(HttpMethod.Post, _tsaUri);
        ByteArrayContent content = new(body);
        content.Headers.ContentType = QueryContentType;
        httpReq.Content = content;

        // .Result is acceptable here: the surrounding API is synchronous,
        // and an async variant can be added later when async signing lands.
        using HttpResponseMessage httpResp = _httpClient.Send(httpReq);
        if (!httpResp.IsSuccessStatusCode)
        {
            throw new TsaException(
                $"TSA returned HTTP {(int)httpResp.StatusCode} ({httpResp.ReasonPhrase}).");
        }

        using Stream s = httpResp.Content.ReadAsStream();
        using MemoryStream ms = new();
        s.CopyTo(ms);
        return TimeStampResponse.Decode(ms.ToArray());
    }
}

/// <summary>
/// Thrown when a TSA returns a non-success HTTP status or otherwise
/// fails to produce a usable response.
/// </summary>
public sealed class TsaException : Exception
{
    /// <summary>Initialises a new exception with the given message.</summary>
    public TsaException(string message) : base(message) { }

    /// <summary>Initialises a new exception with a message and inner cause.</summary>
    public TsaException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Initialises a new exception with the default message.</summary>
    public TsaException() : base("A TSA error occurred.") { }
}
