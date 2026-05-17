// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — async signing path

using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>
/// An asynchronous TSA client. Same contract as <see cref="ITsaClient"/>
/// but exposing an async fetch method, useful when signing happens on a
/// thread that should not block on network I/O.
/// </summary>
/// <remarks>
/// Implementations that wrap real network transports (HTTP, etc.) should
/// implement this in preference to <see cref="ITsaClient"/>; Chuvadi's
/// <see cref="HttpTsaClient"/> implements both so callers can choose
/// either style at their seam.
/// </remarks>
public interface IAsyncTsaClient
{
    /// <summary>Sends <paramref name="request"/> to the TSA asynchronously.</summary>
    Task<TimeStampResponse> FetchAsync(TimeStampRequest request, CancellationToken cancellationToken = default);
}
