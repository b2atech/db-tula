using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core.Models
{
    public class ColumnComparisonResult
    {
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string SourceType { get; set; }
        public string DestinationType { get; set; }
        public string SourceLength { get; set; }
        public string DestinationLength { get; set; }
        public string SourceNullable { get; set; }
        public string DestinationNullable { get; set; }
        public ComparisonType Comparison { get; set; }
        public string Details { get; set; } // Additional details for errors or exceptions
    }
}
