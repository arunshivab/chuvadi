// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography OID registry

using System;
using System.Collections.Generic;
using System.Reflection;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.Oids;

/// <summary>
/// Maps an <see cref="ObjectIdentifier"/> to the friendly name from
/// <see cref="KnownOids"/> for diagnostics and error messages.
/// </summary>
public static class OidNameLookup
{
    private static readonly Dictionary<string, string> Names = BuildNameMap();

    /// <summary>
    /// Returns the friendly name (e.g. "Sha256WithRsa") for a known OID,
    /// or the dotted form if the OID isn't in the registry.
    /// </summary>
    public static string GetName(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        return Names.TryGetValue(oid.Dotted, out string? name) ? name : oid.Dotted;
    }

    /// <summary>
    /// Returns true when the OID is one of Chuvadi's recognised constants.
    /// </summary>
    public static bool IsKnown(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        return Names.ContainsKey(oid.Dotted);
    }

    private static Dictionary<string, string> BuildNameMap()
    {
        // Reflect over the static ObjectIdentifier fields on KnownOids.
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        FieldInfo[] fields = typeof(KnownOids).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (FieldInfo f in fields)
        {
            if (f.FieldType == typeof(ObjectIdentifier))
            {
                object? value = f.GetValue(null);
                if (value is ObjectIdentifier oid)
                {
                    map[oid.Dotted] = f.Name;
                }
            }
        }
        return map;
    }
}
