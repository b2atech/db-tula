using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core.Models
{
    public class SchemaComparisonReport
    {
        public List<ComparisonResult> TableResults { get; set; } = new();
        public List<ComparisonResult> FunctionResults { get; set; } = new();
        public List<ComparisonResult> ProcedureResults { get; set; } = new();
        public List<KeyComparisonResult> IndexResults { get; set; } = new();
        public List<KeyComparisonResult> SequenceResults { get; set; } = new();

        public List<ComparisonResult> AllResults()
        {
            var all = new List<ComparisonResult>();
            all.AddRange(TableResults);
            all.AddRange(FunctionResults);
            all.AddRange(ProcedureResults);

            // Convert KeyComparisonResults to ComparisonResult for unified rendering
            all.AddRange(IndexResults.Select(idx => new ComparisonResult
            {
                Type = "Index",
                SourceName = idx.SourceName,
                DestinationName = idx.DestinationName,
                Comparison = idx.Comparison,
                Details = idx.Details
            }));

            all.AddRange(SequenceResults.Select(seq => new ComparisonResult
            {
                Type = "Sequence",
                SourceName = seq.SourceName,
                DestinationName = seq.DestinationName,
                Comparison = seq.Comparison,
                Details = seq.Details
            }));

            return all;
        }
    }
}
