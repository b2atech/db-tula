using System.Data;
using System.Collections.Generic;

namespace b2a.db_tula
{
    public class SchemaComparer
    {
        // Existing methods for table, function, and procedure comparison

        public List<ComparisonResult> CompareTables(DataTable sourceTables, DataTable targetTables)
        {
            var differences = new List<ComparisonResult>();

            var sourceTableNames = new HashSet<string>();
            foreach (DataRow row in sourceTables.Rows)
            {
                sourceTableNames.Add(row["table_name"].ToString());
            }

            var targetTableNames = new HashSet<string>();
            foreach (DataRow row in targetTables.Rows)
            {
                targetTableNames.Add(row["table_name"].ToString());
            }

            foreach (var table in sourceTableNames)
            {
                if (!targetTableNames.Contains(table))
                {
                    differences.Add(new ComparisonResult { Type = "Table", SourceName = table, DestinationName = table, Comparison = "Missing in Target" });
                }
                else
                {
                    differences.Add(new ComparisonResult { Type = "Table", SourceName = table, DestinationName = table, Comparison = "Matching" });
                }
            }

            foreach (var table in targetTableNames)
            {
                if (!sourceTableNames.Contains(table))
                {
                    differences.Add(new ComparisonResult { Type = "Table", SourceName = table, DestinationName = table, Comparison = "Missing in Source" });
                }
            }

            return differences;
        }

        public List<ComparisonResult> CompareFunctions(DataTable sourceFunctions, DataTable targetFunctions, SchemaFetcher sourceSchemaFetcher, SchemaFetcher targetSchemaFetcher)
        {
            var differences = new List<ComparisonResult>();

            var sourceFunctionNames = new HashSet<string>();
            foreach (DataRow row in sourceFunctions.Rows)
            {
                sourceFunctionNames.Add(row["routine_name"].ToString());
            }

            var targetFunctionNames = new HashSet<string>();
            foreach (DataRow row in targetFunctions.Rows)
            {
                targetFunctionNames.Add(row["routine_name"].ToString());
            }

            foreach (var function in sourceFunctionNames)
            {
                if (!targetFunctionNames.Contains(function))
                {
                    differences.Add(new ComparisonResult { Type = "Function", SourceName = function, DestinationName = function, Comparison = "Missing in Target" });
                }
                else
                {
                    var sourceDefinition = sourceSchemaFetcher.GetFunctionDefinition(function);
                    var targetDefinition = targetSchemaFetcher.GetFunctionDefinition(function);
                    bool isMatch = sourceDefinition == targetDefinition;
                    differences.Add(new ComparisonResult { Type = "Function", SourceName = function, DestinationName = function, Comparison = isMatch ? "Matching" : "Not Matching" });
                }
            }

            foreach (var function in targetFunctionNames)
            {
                if (!sourceFunctionNames.Contains(function))
                {
                    differences.Add(new ComparisonResult { Type = "Function", SourceName = function, DestinationName = function, Comparison = "Missing in Source" });
                }
            }

            return differences;
        }

        public List<ComparisonResult> CompareProcedures(DataTable sourceProcedures, DataTable targetProcedures, SchemaFetcher sourceSchemaFetcher, SchemaFetcher targetSchemaFetcher)
        {
            var differences = new List<ComparisonResult>();

            var sourceProcedureNames = new HashSet<string>();
            foreach (DataRow row in sourceProcedures.Rows)
            {
                sourceProcedureNames.Add(row["routine_name"].ToString());
            }

            var targetProcedureNames = new HashSet<string>();
            foreach (DataRow row in targetProcedures.Rows)
            {
                targetProcedureNames.Add(row["routine_name"].ToString());
            }

            foreach (var procedure in sourceProcedureNames)
            {
                if (!targetProcedureNames.Contains(procedure))
                {
                    differences.Add(new ComparisonResult { Type = "Procedure", SourceName = procedure, DestinationName = procedure, Comparison = "Missing in Target" });
                }
                else
                {
                    var sourceDefinition = sourceSchemaFetcher.GetProcedureDefinition(procedure);
                    var targetDefinition = targetSchemaFetcher.GetProcedureDefinition(procedure);
                    bool isMatch = sourceDefinition == targetDefinition;
                    differences.Add(new ComparisonResult { Type = "Procedure", SourceName = procedure, DestinationName = procedure, Comparison = isMatch ? "Matching" : "Not Matching" });
                }
            }

            foreach (var procedure in targetProcedureNames)
            {
                if (!sourceProcedureNames.Contains(procedure))
                {
                    differences.Add(new ComparisonResult { Type = "Procedure", SourceName = procedure, DestinationName = procedure, Comparison = "Missing in Source" });
                }
            }

            return differences;
        }

        public List<ColumnComparisonResult> CompareColumns(DataTable sourceColumns, DataTable targetColumns)
        {
            var differences = new List<ColumnComparisonResult>();

            var sourceColumnNames = new HashSet<string>();
            foreach (DataRow row in sourceColumns.Rows)
            {
                sourceColumnNames.Add(row["column_name"].ToString());
            }

            var targetColumnNames = new HashSet<string>();
            foreach (DataRow row in targetColumns.Rows)
            {
                targetColumnNames.Add(row["column_name"].ToString());
            }

            foreach (var column in sourceColumnNames)
            {
                if (!targetColumnNames.Contains(column))
                {
                    differences.Add(new ColumnComparisonResult { SourceName = column, DestinationName = column, SourceType = sourceColumns.Rows.Find(column)["data_type"].ToString(), DestinationType = "N/A", SourceLength = sourceColumns.Rows.Find(column)["character_maximum_length"].ToString(), DestinationLength = "N/A", Comparison = "Missing in Target" });
                }
                else
                {
                    var sourceColumn = sourceColumns.Rows.Find(column);
                    var targetColumn = targetColumns.Rows.Find(column);
                    bool isTypeMatch = sourceColumn["data_type"].ToString() == targetColumn["data_type"].ToString();
                    bool isLengthMatch = sourceColumn["character_maximum_length"].ToString() == targetColumn["character_maximum_length"].ToString();
                    string comparisonResult = (isTypeMatch && isLengthMatch) ? "Matching" : "Not Matching";
                    differences.Add(new ColumnComparisonResult { SourceName = column, DestinationName = column, SourceType = sourceColumn["data_type"].ToString(), DestinationType = targetColumn["data_type"].ToString(), SourceLength = sourceColumn["character_maximum_length"].ToString(), DestinationLength = targetColumn["character_maximum_length"].ToString(), Comparison = comparisonResult });
                }
            }

            foreach (var column in targetColumnNames)
            {
                if (!sourceColumnNames.Contains(column))
                {
                    differences.Add(new ColumnComparisonResult { SourceName = column, DestinationName = column, SourceType = "N/A", DestinationType = targetColumns.Rows.Find(column)["data_type"].ToString(), SourceLength = "N/A", DestinationLength = targetColumns.Rows.Find(column)["character_maximum_length"].ToString(), Comparison = "Missing in Source" });
                }
            }

            return differences;
        }

        public List<KeyComparisonResult> ComparePrimaryKeys(DataTable sourcePrimaryKeys, DataTable targetPrimaryKeys)
        {
            var differences = new List<KeyComparisonResult>();

            var sourceKeyNames = new HashSet<string>();
            foreach (DataRow row in sourcePrimaryKeys.Rows)
            {
                sourceKeyNames.Add(row["column_name"].ToString());
            }

            var targetKeyNames = new HashSet<string>();
            foreach (DataRow row in targetPrimaryKeys.Rows)
            {
                targetKeyNames.Add(row["column_name"].ToString());
            }

            foreach (var key in sourceKeyNames)
            {
                if (!targetKeyNames.Contains(key))
                {
                    differences.Add(new KeyComparisonResult { SourceName = key, DestinationName = key, Comparison = "Missing in Target" });
                }
                else
                {
                    differences.Add(new KeyComparisonResult { SourceName = key, DestinationName = key, Comparison = "Matching" });
                }
            }

            foreach (var key in targetKeyNames)
            {
                if (!sourceKeyNames.Contains(key))
                {
                    differences.Add(new KeyComparisonResult { SourceName = key, DestinationName = key, Comparison = "Missing in Source" });
                }
            }

            return differences;
        }

        public List<KeyComparisonResult> CompareForeignKeys(DataTable sourceForeignKeys, DataTable targetForeignKeys)
        {
            var differences = new List<KeyComparisonResult>();

            var sourceKeyNames = new HashSet<string>();
            foreach (DataRow row in sourceForeignKeys.Rows)
            {
                sourceKeyNames.Add(row["column_name"].ToString());
            }

            var targetKeyNames = new HashSet<string>();
            foreach (DataRow row in targetForeignKeys.Rows)
            {
                targetKeyNames.Add(row["column_name"].ToString());
            }

            foreach (var key in sourceKeyNames)
            {
                if (!targetKeyNames.Contains(key))
                {
                    differences.Add(new KeyComparisonResult { SourceName = key, DestinationName = key, Comparison = "Missing in Target" });
                }
                else
                {
                    differences.Add(new KeyComparisonResult { SourceName = key, DestinationName = key, Comparison = "Matching" });
                }
            }

            foreach (var key in targetKeyNames)
            {
                if (!sourceKeyNames.Contains(key))
                {
                    differences.Add(new KeyComparisonResult { SourceName = key, DestinationName = key, Comparison = "Missing in Source" });
                }
            }

            return differences;
        }
    }

    public class ColumnComparisonResult
    {
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string SourceType { get; set; }
        public string DestinationType { get; set; }
        public string SourceLength { get; set; }
        public string DestinationLength { get; set; }
        public string Comparison { get; set; }
    }

    public class KeyComparisonResult
    {
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string Comparison { get; set; }
    }
}
