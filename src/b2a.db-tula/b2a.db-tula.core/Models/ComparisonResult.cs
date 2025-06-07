using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core.Models
{
    public class ComparisonResult
    {
        public string Type { get; set; }
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string Comparison { get; set; }
        public string Details { get; set; } // Additional details for errors or exceptions
        public string SourceDefinition { get; set; }
        public string DestinationDefinition { get; set; }
        public List<ColumnComparisonResult> ColumnComparisonResults { get; set; }
        public string SyncScript { get; set; }
    }
}
