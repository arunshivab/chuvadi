// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.3 — TSA fetching

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// A client capable of fetching an RFC 3161 timestamp from a TSA.
/// </summary>
/// <remarks>
/// The abstraction lets callers plug in any transport: the supplied
/// <see cref="HttpTsaClient"/> uses HTTP/HTTPS; in-memory mocks for tests
/// or alternative transports (e.g. authenticated channels for private
/// TSAs) implement this interface and can be passed wherever a TSA
/// timestamp is required.
/// </remarks>
public interface ITsaClient
{
    /// <summary>
    /// Sends <paramref name="request"/> to the TSA and returns its response.
    /// </summary>
    /// <param name="request">The DER-encoded request to send.</param>
    /// <returns>The decoded response; check <see cref="TimeStampResponse.IsGranted"/>.</returns>
    TimeStampResponse Fetch(TimeStampRequest request);
}
