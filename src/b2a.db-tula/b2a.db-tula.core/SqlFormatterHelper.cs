using PoorMansTSqlFormatterRedux.Formatters;
using PoorMansTSqlFormatterRedux.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core;

public static class SqlFormatterHelper
{
    //public static string Format(string rawSql)
    //{
    //    var parser = new TSqlStandardParser();
    //    var formatter = new TSqlStandardFormatter();

    //    using var reader = new StringReader(rawSql);
    //    using var writer = new StringWriter();

    //    var result = formatter.Format(reader, parser);
    //    writer.Write(result);

    //    return writer.ToString();
    //}
}