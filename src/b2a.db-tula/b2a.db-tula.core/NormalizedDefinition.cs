using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core
{
    public static class NormalizedDefinition
    {
        public static string Normalize(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return string.Empty;
            return definition
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Replace("  ", " ")
                .Trim()
                .ToLowerInvariant();
        }
    }
}
