﻿using System.Data;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using b2a.db_tula.Models;

namespace b2a.db_tula
{
    public class SchemaComparer
    {
        public List<ComparisonResult> CompareTables(DataTable sourceTables, DataTable targetTables, string sourceConnectionString, string targetConnectionString, Action<int, int> reportProgress)
        {
            var differences = new List<ComparisonResult>();

            try
            {
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

                int totalTables = sourceTableNames.Count + targetTableNames.Count; // Total number of tables to compare
                int currentTableIndex = 0; // To track the progress

                // Compare source tables with target tables
                foreach (var table in sourceTableNames)
                {
                    var currentTableComparison = new ComparisonResult { Type = "Table", SourceName = table, DestinationName = table };

                    if (!targetTableNames.Contains(table))
                    {
                        currentTableComparison.Comparison = "Missing in Target";
                    }
                    else
                    {
                        // Compare columns
                        var sourceColumns = GetTableColumns(sourceConnectionString, table);
                        var targetColumns = GetTableColumns(targetConnectionString, table);
                        var columnComparisonResults = CompareColumns(sourceColumns, targetColumns, out bool allColumnsMatch);
                        currentTableComparison.Comparison = allColumnsMatch ? "Matching" : "Not Matching";
                        currentTableComparison.ColumnComparisonResults = columnComparisonResults;
                    }

                    differences.Add(currentTableComparison);

                    // Increment progress and report it
                    currentTableIndex++;
                    reportProgress?.Invoke(currentTableIndex, totalTables); // Invoke the progress callback
                }

                // Check for tables in target that are missing in source
                foreach (var table in targetTableNames)
                {
                    if (!sourceTableNames.Contains(table))
                    {
                        differences.Add(new ComparisonResult { Type = "Table", SourceName = table, DestinationName = table, Comparison = "Missing in Source" });
                    }
                    // Increment progress and report it
                    currentTableIndex++;
                    reportProgress?.Invoke(currentTableIndex, totalTables); // Invoke the progress callback
                }
            }
            catch (Exception ex)
            {
                differences.Add(new ComparisonResult { Type = "Error", SourceName = "Table Comparison", DestinationName = "Table Comparison", Comparison = "Exception", Details = ex.Message });
            }

            return differences;
        }


        private DataTable GetTableColumns(string connectionString, string tableName)
        {
            using (var connection = new Npgsql.NpgsqlConnection(connectionString)) // PostgreSQL specific connection
            {
                connection.Open();
                var query = $"SELECT column_name, data_type, character_maximum_length, is_nullable FROM information_schema.columns WHERE table_name = '{tableName}'";
                using (var command = new Npgsql.NpgsqlCommand(query, connection))
                {
                    using (var adapter = new Npgsql.NpgsqlDataAdapter(command))
                    {
                        var columns = new DataTable();
                        adapter.Fill(columns);
                        return columns;
                    }
                }
            }
        }

        public List<ColumnComparisonResult> CompareColumns(DataTable sourceColumns, DataTable targetColumns, out bool allColumnsMatch)
        {
            var differences = new List<ColumnComparisonResult>();
            allColumnsMatch = true;

            try
            {
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
                        try
                        {
                            differences.Add(new ColumnComparisonResult { SourceName = column, DestinationName = column, SourceType = GetColumnValue(sourceColumns, column, "data_type"), DestinationType = "N/A", SourceLength = GetColumnValue(sourceColumns, column, "character_maximum_length"), DestinationLength = "N/A", Comparison = "Missing in Target" });

                            allColumnsMatch = false;
                        }
                        catch(Exception ex)
                        {
                            Debug.WriteLine(ex);
                        }
                    }
                    else
                    {
                        var sourceColumn = sourceColumns.AsEnumerable().First(r => r["column_name"].ToString() == column);
                        var targetColumn = targetColumns.AsEnumerable().First(r => r["column_name"].ToString() == column);

                        var sourceDataType = sourceColumn["data_type"].ToString();
                        var targetDataType = targetColumn["data_type"].ToString();
                        var sourceLength = sourceColumn["character_maximum_length"]?.ToString();
                        var targetLength = targetColumn["character_maximum_length"]?.ToString();
                        var sourceIsNullable = sourceColumn["is_nullable"].ToString();
                        var targetIsNullable = targetColumn["is_nullable"].ToString();

                        bool isTypeMatch = sourceDataType == targetDataType;
                        bool isLengthMatch = sourceLength == targetLength;
                        bool isNullableMatch = sourceIsNullable == targetIsNullable;

                        string comparisonResult = (isTypeMatch && isLengthMatch && isNullableMatch) ? "Matching" : "Not Matching";

                        if (comparisonResult == "Not Matching")
                        {
                            allColumnsMatch = false;
                        }

                        differences.Add(new ColumnComparisonResult
                        {
                            SourceName = column,
                            DestinationName = column,
                            SourceType = sourceDataType,
                            DestinationType = targetDataType,
                            SourceLength = sourceLength,
                            DestinationLength = targetLength,
                            SourceNullable = sourceIsNullable,
                            DestinationNullable = targetIsNullable,
                            Comparison = comparisonResult
                        });
                    }
                }

                foreach (var column in targetColumnNames)
                {
                    if (!sourceColumnNames.Contains(column))
                    {
                        differences.Add(new ColumnComparisonResult { SourceName = column, DestinationName = column, SourceType = GetColumnValue(sourceColumns, column, "data_type"), DestinationType = "N/A", SourceLength = GetColumnValue(sourceColumns, column, "character_maximum_length"), DestinationLength = "N/A", Comparison = "Missing in Source" });
                        allColumnsMatch = false;
                    }
                }
            }
            catch (Exception ex)
            {
                differences.Add(new ColumnComparisonResult { SourceName = "Column Comparison", DestinationName = "Column Comparison", SourceType = "Exception", DestinationType = "Exception", Comparison = "Exception", Details = ex.Message });
                allColumnsMatch = false;
            }

            return differences;
        }
        private string GetColumnValue(DataTable table, string columnName, string fieldName)
        {
            var row = table.AsEnumerable().FirstOrDefault(r => r["column_name"].ToString() == columnName);
            return row?[fieldName]?.ToString() ?? "N/A";
        }

        public int GetUniqueCount(DataTable source, DataTable target, string type)
        {
            var sourceNames = new HashSet<string>();
            foreach (DataRow row in source.Rows)
            {
                sourceNames.Add(row[type].ToString());
            }

            var targetNames = new HashSet<string>();
            foreach (DataRow row in target.Rows)
            {
                targetNames.Add(row[type].ToString());
            }

            return sourceNames.Count + targetNames.Count; // Total number of tables to compare
        }

        public List<ComparisonResult> CompareFunctions(DataTable sourceFunctions, DataTable targetFunctions, SchemaFetcher sourceSchemaFetcher, SchemaFetcher targetSchemaFetcher, Action<int, int> reportProgress)
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

            int totalFunctions = sourceFunctionNames.Count + targetFunctionNames.Count; // Total number of tables to compare
            int currentFunctionIndex = 0; // To track the progress

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
                    differences.Add(new ComparisonResult { Type = "Function", 
                        SourceName = function, 
                        DestinationName = function, Comparison = isMatch ? "Matching" : "Not Matching",
                    SourceDefinition = sourceDefinition,
                    DestinationDefinition = targetDefinition});
                }

                // Increment progress and report it
                currentFunctionIndex++;
                reportProgress?.Invoke(currentFunctionIndex, totalFunctions); // Invoke the progress callback
            }

            foreach (var function in targetFunctionNames)
            {
                if (!sourceFunctionNames.Contains(function))
                {
                    differences.Add(new ComparisonResult { Type = "Function", SourceName = function, DestinationName = function, Comparison = "Missing in Source" });

                }
                // Increment progress and report it
                currentFunctionIndex++;
                reportProgress?.Invoke(currentFunctionIndex, totalFunctions); // Invoke the progress callback
            }

            return differences;
        }

        public List<ComparisonResult> CompareProcedures(DataTable sourceProcedures, DataTable targetProcedures, SchemaFetcher sourceSchemaFetcher, SchemaFetcher targetSchemaFetcher,Action<int, int> reportProgress)
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

            int totalProcedures = sourceProcedureNames.Count + targetProcedureNames.Count; // Total number of tables to compare
            int currentProcedureIndex = 0; // To track the progress

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
                // Increment progress and report it
                currentProcedureIndex++;
                reportProgress?.Invoke(currentProcedureIndex, totalProcedures); // Invoke the progress callback
            }

            foreach (var procedure in targetProcedureNames)
            {
                if (!sourceProcedureNames.Contains(procedure))
                {
                    differences.Add(new ComparisonResult { Type = "Procedure", SourceName = procedure, DestinationName = procedure, Comparison = "Missing in Source" });
                }
                currentProcedureIndex++;
                reportProgress?.Invoke(currentProcedureIndex, totalProcedures); // Invoke the progress callback
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

        public List<string> GenerateSyncCommands(TableDefinition sourceTable, TableDefinition targetTable)
        {
            var commands = new List<string>();

            // Compare columns
            foreach (var sourceColumn in sourceTable.Columns)
            {
                var targetColumn = targetTable.Columns.FirstOrDefault(c => c.Name == sourceColumn.Name);

                if (targetColumn == null)
                {
                    // Column does not exist in target, generate ADD COLUMN command
                    commands.Add($"ALTER TABLE {targetTable.Name} ADD COLUMN {sourceColumn.Name} {sourceColumn.DataType};");
                }
                else if (sourceColumn.DataType != targetColumn.DataType)
                {
                    // Data type mismatch, generate ALTER COLUMN command
                    commands.Add($"ALTER TABLE {targetTable.Name} ALTER COLUMN {sourceColumn.Name} TYPE {sourceColumn.DataType};");
                }
            }

            // Compare primary keys
            if (!sourceTable.PrimaryKeys.SequenceEqual(targetTable.PrimaryKeys))
            {
                commands.Add($"ALTER TABLE {targetTable.Name} DROP CONSTRAINT IF EXISTS {targetTable.Name}_pkey;");
                commands.Add($"ALTER TABLE {targetTable.Name} ADD PRIMARY KEY ({string.Join(",", sourceTable.PrimaryKeys)});");
            }

            // Compare foreign keys
            foreach (var sourceFk in sourceTable.ForeignKeys)
            {
                var targetFk = targetTable.ForeignKeys.FirstOrDefault(fk => fk.Name == sourceFk.Name);
                if (targetFk == null)
                {
                    commands.Add($"ALTER TABLE {targetTable.Name} ADD CONSTRAINT {sourceFk.Name} FOREIGN KEY ({sourceFk.ColumnName}) REFERENCES {sourceFk.ReferencedTable}({sourceFk.ReferencedColumn});");
                }
            }

            return commands;
        }


    }

    public class ComparisonResult
    {
        public string Type { get; set; }
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string Comparison { get; set; }
        public string Details { get; set; } // Additional details for errors or exceptions
        public string SourceDefinition { get; set; }
        public string DestinationDefinition { get; set; }
        public List<ColumnComparisonResult> ColumnComparisonResults { get; set; }
    }

    public class ColumnComparisonResult
    {
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string SourceType { get; set; }
        public string DestinationType { get; set; }
        public string SourceLength { get; set; }
        public string DestinationLength { get; set; }
        public string SourceNullable { get; set; }
        public string DestinationNullable { get; set; }
        public string Comparison { get; set; }
        public string Details { get; set; } // Additional details for errors or exceptions
    }

    public class KeyComparisonResult
    {
        public string SourceName { get; set; }
        public string DestinationName { get; set; }
        public string Comparison { get; set; }
        public string Details { get; set; } // Additional details for errors or exceptions
    }
}
