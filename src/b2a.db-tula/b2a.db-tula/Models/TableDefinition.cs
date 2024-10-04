using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.Models
{
    public class TableDefinition
    {
        public string Name { get; set; }
        public List<ColumnDefinition> Columns { get; set; }
        public List<string> PrimaryKeys { get; set; }
        public List<ForeignKeyDefinition> ForeignKeys { get; set; }
    }

    public class ColumnDefinition
    {
        public string Name { get; set; }
        public string DataType { get; set; }
    }

    public class ForeignKeyDefinition
    {
        public string Name { get; set; }
        public string ColumnName { get; set; }
        public string ReferencedTable { get; set; }
        public string ReferencedColumn { get; set; }
    }

}
