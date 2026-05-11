// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.2 — Chuvadi.Pdf.Redaction pattern-based extension
// Pre-built regex patterns for common PHI / PII tokens.

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Pre-built regex strings for common PHI / PII tokens.
/// </summary>
/// <remarks>
/// These are conservative starting points. Real-world documents have many edge
/// cases (whitespace inside identifiers, OCR artefacts, locale-specific formats);
/// production deployments should tune patterns to their corpus.
/// </remarks>
public static class CommonPatterns
{
    /// <summary>
    /// US Social Security Number. Matches the conventional XXX-XX-XXXX format.
    /// </summary>
    public const string UsSsn = @"\b\d{3}-\d{2}-\d{4}\b";

    /// <summary>
    /// US phone number. Matches (XXX) XXX-XXXX, XXX-XXX-XXXX, and XXX.XXX.XXXX.
    /// </summary>
    public const string UsPhone = @"\b(?:\(\d{3}\)\s?|\d{3}[-.])\d{3}[-.]\d{4}\b";

    /// <summary>
    /// Email address. RFC-5322 inspired but conservative enough to avoid false positives.
    /// </summary>
    public const string Email = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b";

    /// <summary>
    /// ICD-10 code prefix. Matches the letter+two-digit prefix of any ICD-10 code,
    /// e.g. "E11" or "J45.901". Intentionally loose; tune downward if you want exact codes.
    /// </summary>
    public const string Icd10Prefix = @"\b[A-TV-Z][0-9][0-9A-Z](?:\.[0-9A-Z]{1,4})?\b";

    /// <summary>
    /// Credit card primary account number. Matches 13-19 digits possibly grouped by
    /// spaces or dashes. Does not validate the Luhn checksum — match precision is
    /// the caller's responsibility.
    /// </summary>
    public const string CreditCard = @"\b(?:\d[ -]?){13,19}\b";

    /// <summary>
    /// ISO-8601 date. Matches YYYY-MM-DD and YYYY/MM/DD.
    /// </summary>
    public const string IsoDate = @"\b\d{4}[-/]\d{2}[-/]\d{2}\b";

    /// <summary>
    /// US ZIP code. Matches 5-digit and ZIP+4 forms.
    /// </summary>
    public const string UsZip = @"\b\d{5}(?:-\d{4})?\b";

    /// <summary>
    /// UK NHS number (10 digits, optionally grouped 3-3-4 with spaces).
    /// </summary>
    public const string UkNhsNumber = @"\b\d{3}\s?\d{3}\s?\d{4}\b";
}
