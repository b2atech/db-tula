using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace B2A.DbTula.Core.Models;

public class ComparisonSubResult
{
    public string Component { get; set; } = string.Empty; // e.g., "Columns", "PrimaryKeys", "ForeignKeys"
    public ComparisonStatus Status { get; set; }           // Match, Mismatch, MissingInSource, etc.
    public string Details { get; set; } = string.Empty;
    public string CreateScript { get; set; } = string.Empty;

    public ComparisonSubResult() { }

    public ComparisonSubResult(string component, ComparisonStatus status, string details, string createScript)
    {
        Component = component;
        Status = status;
        Details = details;
        CreateScript = createScript;
    }

    public string DisplayStatus => Status.ToDisplayString();
}