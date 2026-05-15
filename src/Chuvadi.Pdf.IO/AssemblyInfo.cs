// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Expose internal types to the test assembly so unit tests can target the
// bit-packed primitives and the hint table codec directly.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Chuvadi.Pdf.IO.Tests")]
