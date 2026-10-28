using System;
using System.Collections.Generic;

namespace Chuvadi.Markup.Internal
{
    /// <summary>Lookup over the WHATWG named character reference table.</summary>
    internal static partial class HtmlEntities
    {
        private static Dictionary<string, string>? s_map;

        private static Dictionary<string, string> Map => s_map ??= Build();

        private static Dictionary<string, string> Build()
        {
            var map = new Dictionary<string, string>(Count, StringComparer.Ordinal);
            int n = 0, v = 0;
            while (n < NameData.Length)
            {
                int ne = NameData.IndexOf('\0', n);
                int ve = ValueData.IndexOf('\0', v);
                map[NameData.Substring(n, ne - n)] = ValueData.Substring(v, ve - v);
                n = ne + 1;
                v = ve + 1;
            }
            return map;
        }

        /// <summary>Looks up a name exactly as given (include the trailing ';' if present).</summary>
        public static bool TryGet(ReadOnlySpan<char> name, out string value)
        {
            var lookup = Map.GetAlternateLookup<ReadOnlySpan<char>>();
            if (lookup.TryGetValue(name, out var v))
            {
                value = v;
                return true;
            }
            value = string.Empty;
            return false;
        }

        /// <summary>True if any entity name starts with the given prefix (used by the tokenizer to stop early).</summary>
        public static bool AnyStartsWith(ReadOnlySpan<char> prefix)
        {
            var prefixes = s_prefixes ??= BuildPrefixes();
            return prefixes.GetAlternateLookup<ReadOnlySpan<char>>().Contains(prefix);
        }

        private static HashSet<string>? s_prefixes;

        private static HashSet<string> BuildPrefixes()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in Map.Keys)
                for (int i = 1; i <= key.Length; i++)
                    set.Add(key.Substring(0, i));
            return set;
        }
    }
}
