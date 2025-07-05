using B2A.DbTula.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace B2A.DbTula.Cli.Reports;
public class SchemaComparisonReport
{
    public string Title { get; set; } = "Schema Comparison Report";
    public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;
    public List<ComparisonResult> Results { get; set; } = new();
}