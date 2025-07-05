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
}