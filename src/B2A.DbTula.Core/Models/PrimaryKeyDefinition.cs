using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace B2A.DbTula.Core.Models;

public class PrimaryKeyDefinition
{
    public string Name { get; set; } = string.Empty;

    // List of columns in the primary key (usually one or more)
    public List<string> Columns { get; set; } = new();

    // Optional: Full CREATE or ALTER TABLE script to add this PK
    public string? CreateScript { get; set; }

    /// <summary>
    /// Gets the structural key for semantic comparison (independent of PK name)
    /// </summary>
    public string GetStructuralKey()
    {
        return string.Join(",", Columns.Select(c => c.ToLower()));
    }

    /// <summary>
    /// Compares PKs by structure (column list), not by name
    /// </summary>
    public bool StructuralEquals(PrimaryKeyDefinition other)
    {
        if (other == null) return false;
        
        return Columns.SequenceEqual(other.Columns, StringComparer.OrdinalIgnoreCase);
    }
}