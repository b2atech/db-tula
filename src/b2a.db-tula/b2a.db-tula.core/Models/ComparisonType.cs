using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core.Models
{
    public enum ComparisonType
    {
        Same,
        MissingInTarget,
        MissingInSource,
        ExtraInTarget,
        Changed
    }

    public static class ComparisonTypeExtensions
    {
        public static bool NeedsSync(this ComparisonType type)
        {
            return type == ComparisonType.MissingInTarget || type == ComparisonType.ExtraInTarget || type == ComparisonType.Changed;
        }
    }
}
