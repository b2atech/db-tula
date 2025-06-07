using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core.Models
{
    public class KeyComparisonResult
    {
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string Comparison { get; set; }
        public string Details { get; set; } // Additional details for errors or exceptions
        public string SyncScript { get; set; }
    }
}
