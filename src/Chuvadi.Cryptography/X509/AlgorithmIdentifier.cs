// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.1.1.2 — AlgorithmIdentifier
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// An ASN.1 AlgorithmIdentifier as defined by RFC 5280 §4.1.1.2.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// AlgorithmIdentifier ::= SEQUENCE {
///   algorithm   OBJECT IDENTIFIER,
///   parameters  ANY DEFINED BY algorithm OPTIONAL
/// }
/// </code>
/// The parameters field is algorithm-specific. For RSA encryption the
/// parameters are explicit NULL (RFC 3279); for ECDSA they are absent (RFC 5480);
/// for RSA-PSS they are a complex SEQUENCE (RFC 8017). Chuvadi preserves the
/// parameters as raw encoded bytes so each algorithm can decode them on demand.
/// </remarks>
public sealed class AlgorithmIdentifier : IEquatable<AlgorithmIdentifier>
{
    /// <summary>Initialises a new AlgorithmIdentifier.</summary>
    /// <param name="algorithm">The algorithm OID.</param>
    /// <param name="parameters">The raw parameter bytes (the complete TLV), or null/empty for absent.</param>
    public AlgorithmIdentifier(ObjectIdentifier algorithm, byte[]? parameters)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        Algorithm = algorithm;
        Parameters = parameters ?? Array.Empty<byte>();
    }

    /// <summary>The algorithm OID.</summary>
    public ObjectIdentifier Algorithm { get; }

    /// <summary>The raw parameter bytes (empty when parameters are absent).</summary>
    public byte[] Parameters { get; }

    /// <summary>
    /// True when the parameters field is explicit ASN.1 NULL (the typical encoding
    /// for RSA signature algorithms and SHA-* hash AlgorithmIdentifiers).
    /// </summary>
    public bool ParametersAreNull
        => Parameters.Length == 2 && Parameters[0] == 0x05 && Parameters[1] == 0x00;

    /// <summary>True when parameters are absent (ECDSA and most modern key types).</summary>
    public bool ParametersAreAbsent => Parameters.Length == 0;

    /// <inheritdoc/>
    public override string ToString()
    {
        string name = OidNameLookup.GetName(Algorithm);
        if (ParametersAreAbsent) { return name; }
        if (ParametersAreNull) { return $"{name} (NULL)"; }
        return $"{name} ({Parameters.Length} param bytes)";
    }

    /// <inheritdoc/>
    public bool Equals(AlgorithmIdentifier? other)
    {
        if (other is null) { return false; }
        if (!Algorithm.Equals(other.Algorithm)) { return false; }
        if (Parameters.Length != other.Parameters.Length) { return false; }
        for (int i = 0; i < Parameters.Length; i++)
        {
            if (Parameters[i] != other.Parameters[i]) { return false; }
        }
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as AlgorithmIdentifier);

    /// <inheritdoc/>
    public override int GetHashCode() => Algorithm.GetHashCode();

    /// <summary>Reads an AlgorithmIdentifier from a reader positioned at its SEQUENCE.</summary>
    public static AlgorithmIdentifier Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        ObjectIdentifier algorithm = seq.ReadObjectIdentifier();
        byte[] parameters = seq.IsAtEnd ? Array.Empty<byte>() : seq.ReadEncoded();
        seq.ExpectEnd();
        return new AlgorithmIdentifier(algorithm, parameters);
    }
}
