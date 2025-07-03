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
        public ComparisonType Comparison { get; set; }
        public string Details { get; set; }
      
        public TableDefinition SourceDefinition { get; set; }
        public TableDefinition DestinationDefinition { get; set; }
        public string SourceFuncOrProcDefinition { get; set; }
        public string DestinationFuncOrProcDefinition { get; set; }
        public List<ColumnComparisonResult> ColumnComparisonResults { get; set; } = new();
        public List<KeyComparisonResult> PrimaryKeyComparisonResults { get; set; } = new();
        public List<KeyComparisonResult> ForeignKeyComparisonResults { get; set; } = new();
        public List<KeyComparisonResult> IndexComparisonResults { get; set; } = new();

        public string SyncScript { get; set; }

        public bool HasDifferences =>
            Comparison != ComparisonType.Same ||
            ColumnComparisonResults.Any() ||
            PrimaryKeyComparisonResults.Any(r => r.Comparison != ComparisonType.Same) ||
            ForeignKeyComparisonResults.Any(r => r.Comparison != ComparisonType.Same) ||
            IndexComparisonResults.Any(r => r.Comparison != ComparisonType.Same);
    }

}
