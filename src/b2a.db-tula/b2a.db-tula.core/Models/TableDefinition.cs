using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core.Models
{
    public class TableDefinition
    {
        public string Name { get; set; }
        public List<ColumnDefinition> Columns { get; set; }
        public List<string> PrimaryKeys { get; set; }
        public List<ForeignKeyDefinition> ForeignKeys { get; set; }
        public string? CreateScript { get; set; }
    }
}
