using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace B2A.DbTula.Core.Models;
public class DbFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string? Definition { get; set; } // Optional, if you want to load the full script later
}