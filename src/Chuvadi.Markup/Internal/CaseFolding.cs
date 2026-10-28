using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Markup.Internal
{
    /// <summary>Unicode full case folding, as CommonMark requires for link label matching.</summary>
    internal static partial class CaseFolding
    {
        private static Dictionary<int, string>? s_map;

        private static Dictionary<int, string> Map
        {
            get
            {
                if (s_map is not null) return s_map;
                var map = new Dictionary<int, string>(Sources.Length);
                int t = 0;
                for (int i = 0; i < Sources.Length; i++)
                {
                    int e = Targets.IndexOf('\0', t);
                    map[Sources[i]] = Targets.Substring(t, e - t);
                    t = e + 1;
                }
                return s_map = map;
            }
        }

        public static string Fold(string s)
        {
            StringBuilder? sb = null;
            var map = Map;
            for (int i = 0; i < s.Length; i++)
            {
                int cp = s[i];
                int width = 1;
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    width = 2;
                }
                if (map.TryGetValue(cp, out var folded))
                {
                    sb ??= new StringBuilder(s.Length).Append(s, 0, i);
                    sb.Append(folded);
                }
                else
                {
                    sb?.Append(s, i, width);
                }
                i += width - 1;
            }
            return sb?.ToString() ?? s;
        }
    }
}
