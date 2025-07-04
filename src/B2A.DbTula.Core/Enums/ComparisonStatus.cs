using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace B2A.DbTula.Core.Enums;

public enum ComparisonStatus
{
    Match,
    MissingInSource,
    MissingInTarget,
    Mismatch
}
