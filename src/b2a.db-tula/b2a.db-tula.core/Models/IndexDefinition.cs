using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core.Models
{
    public class IndexDefinition
    {
        public string IndexName { get; set; }
        public string TableName { get; set; }
        public List<string> Columns { get; set; }
        public bool IsUnique { get; set; }
        public string IndexType { get; set; }
    }

}
