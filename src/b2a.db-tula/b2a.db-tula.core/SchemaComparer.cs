using b2a.db_tula.core.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core
{
    public class SchemaComparer
    {
        public List<ComparisonResult> CompareTables(SchemaFetcher sourceFetcher, SchemaFetcher targetFetcher, DataTable sourceTables, DataTable targetTables, Action<int, int, string> progressLogger)
        {
            var differences = new List<ComparisonResult>();

            var sourceNames = sourceTables.Rows.Cast<DataRow>().Select(r => r["table_name"].ToString()).ToHashSet();
            var targetNames = targetTables.Rows.Cast<DataRow>().Select(r => r["table_name"].ToString()).ToHashSet();
            int total = sourceNames.Count + targetNames.Count;
            int index = 0;

            foreach (var name in sourceNames)
            {
                if (!targetNames.Contains(name))
                {
                    differences.Add(new ComparisonResult { Type = "Table", SourceName = name, Comparison = "Missing in Target" });
                }
                else
                {
                    var src = sourceFetcher.GetTableDefinition(name);
                    var tgt = targetFetcher.GetTableDefinition(name);
                    var result = new ComparisonResult { Type = "Table", SourceName = name, DestinationName = name, Comparison = "Matching" };

                    foreach (var col in src.Columns)
                    {
                        var tgtCol = tgt.Columns.FirstOrDefault(c => c.Name == col.Name);
                        if (tgtCol == null)
                        {
                            result.ColumnComparisonResults.Add(new ColumnComparisonResult { SourceName = col.Name, Comparison = "Missing in Target" });
                            result.Comparison = "Not Matching";
                        }
                        else if (col.DataType != tgtCol.DataType)
                        {
                            result.ColumnComparisonResults.Add(new ColumnComparisonResult { SourceName = col.Name, SourceType = col.DataType, DestinationType = tgtCol.DataType, Comparison = "Type Mismatch" });
                            result.Comparison = "Not Matching";
                        }
                    }

                    foreach (var col in tgt.Columns)
                    {
                        if (!src.Columns.Any(c => c.Name == col.Name))
                        {
                            result.ColumnComparisonResults.Add(new ColumnComparisonResult { DestinationName = col.Name, Comparison = "Missing in Source" });
                            result.Comparison = "Not Matching";
                        }
                    }

                    differences.Add(result);
                }
                index++;
                progressLogger?.Invoke(index, total, name);
            }

            foreach (var name in targetNames)
            {
                if (!sourceNames.Contains(name))
                {
                    differences.Add(new ComparisonResult { Type = "Table", DestinationName = name, Comparison = "Missing in Source" });
                }
                index++;
                progressLogger?.Invoke(index, total, name);
            }

            return differences;
        }

        public List<ComparisonResult> CompareFunctions(SchemaFetcher sourceFetcher, SchemaFetcher targetFetcher, DataTable source, DataTable target, Action<int, int, string> progressLogger)
        {
            return CompareRoutines("Function", true, sourceFetcher, targetFetcher, source, target, progressLogger);
        }

        public List<ComparisonResult> CompareProcedures(SchemaFetcher sourceFetcher, SchemaFetcher targetFetcher, DataTable source, DataTable target, Action<int, int, string> progressLogger)
        {
            return CompareRoutines("Procedure", false, sourceFetcher, targetFetcher, source, target, progressLogger);
        }

        private List<ComparisonResult> CompareRoutines(string type, bool isFunction, SchemaFetcher sourceFetcher, SchemaFetcher targetFetcher, DataTable source, DataTable target, Action<int, int, string> progressLogger)
        {
            var differences = new List<ComparisonResult>();
            var sourceNames = source.Rows.Cast<DataRow>().Select(r => r["routine_name"].ToString()).ToHashSet();
            var targetNames = target.Rows.Cast<DataRow>().Select(r => r["routine_name"].ToString()).ToHashSet();
            int total = sourceNames.Count + targetNames.Count;
            int index = 0;

            foreach (var name in sourceNames)
            {
                if (!targetNames.Contains(name))
                {
                    differences.Add(new ComparisonResult { Type = type, SourceName = name, Comparison = "Missing in Target" });
                }
                else
                {
                    var src = sourceFetcher.GetFunctionOrProcedureDefinitionAsync(name).GetAwaiter().GetResult();
                    var tgt = targetFetcher.GetFunctionOrProcedureDefinitionAsync(name).GetAwaiter().GetResult();
                    var match = NormalizedDefinition.Normalize(src) == NormalizedDefinition.Normalize(tgt);
                    differences.Add(new ComparisonResult { Type = type, SourceName = name, DestinationName = name, Comparison = match ? "Matching" : "Not Matching", SourceDefinition = src, DestinationDefinition = tgt });
                }
                index++;
                progressLogger?.Invoke(index, total, name);
            }

            foreach (var name in targetNames)
            {
                if (!sourceNames.Contains(name))
                {
                    differences.Add(new ComparisonResult { Type = type, DestinationName = name, Comparison = "Missing in Source" });
                }
                index++;
                progressLogger?.Invoke(index, total, name);
            }

            return differences;
        }

        public List<KeyComparisonResult> ComparePrimaryKeys(TableDefinition source, TableDefinition target)
        {
            var results = new List<KeyComparisonResult>();
            var srcKeys = source.PrimaryKeys.ToHashSet();
            var tgtKeys = target.PrimaryKeys.ToHashSet();

            foreach (var key in srcKeys)
            {
                if (!tgtKeys.Contains(key))
                    results.Add(new KeyComparisonResult { SourceName = key, Comparison = "Missing in Target" });
                else
                    results.Add(new KeyComparisonResult { SourceName = key, DestinationName = key, Comparison = "Matching" });
            }

            foreach (var key in tgtKeys.Except(srcKeys))
            {
                results.Add(new KeyComparisonResult { DestinationName = key, Comparison = "Missing in Source" });
            }

            return results;
        }

        public List<KeyComparisonResult> CompareForeignKeys(TableDefinition source, TableDefinition target)
        {
            var results = new List<KeyComparisonResult>();

            foreach (var fk in source.ForeignKeys)
            {
                var exists = target.ForeignKeys.Any(t =>
                    t.ColumnName == fk.ColumnName &&
                    t.ReferencedTable == fk.ReferencedTable &&
                    t.ReferencedColumn == fk.ReferencedColumn);

                results.Add(new KeyComparisonResult
                {
                    SourceName = fk.Name,
                    DestinationName = exists ? fk.Name : null,
                    Comparison = exists ? "Matching" : "Missing in Target"
                });
            }

            foreach (var fk in target.ForeignKeys)
            {
                var exists = source.ForeignKeys.Any(s =>
                    s.ColumnName == fk.ColumnName &&
                    s.ReferencedTable == fk.ReferencedTable &&
                    s.ReferencedColumn == fk.ReferencedColumn);

                if (!exists)
                {
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = null,
                        DestinationName = fk.Name,
                        Comparison = "Missing in Source"
                    });
                }
            }

            return results;
        }

        public List<string> GenerateSyncCommands(TableDefinition source, TableDefinition target)
        {
            var commands = new List<string>();

            foreach (var srcCol in source.Columns)
            {
                var tgtCol = target.Columns.FirstOrDefault(c => c.Name == srcCol.Name);
                if (tgtCol == null)
                {
                    commands.Add($"ALTER TABLE \"{target.Name}\" ADD COLUMN \"{srcCol.Name}\" {srcCol.DataType};");
                }
                else if (srcCol.DataType != tgtCol.DataType)
                {
                    commands.Add($"ALTER TABLE \"{target.Name}\" ALTER COLUMN \"{srcCol.Name}\" TYPE {srcCol.DataType};");
                }
            }

            if (!source.PrimaryKeys.SequenceEqual(target.PrimaryKeys))
            {
                commands.Add($"ALTER TABLE \"{target.Name}\" DROP CONSTRAINT IF EXISTS \"{target.Name}_pkey\";");
                commands.Add($"ALTER TABLE \"{target.Name}\" ADD PRIMARY KEY ({string.Join(", ", source.PrimaryKeys.Select(pk => $"\"{pk}\""))});");

            }

            foreach (var fk in source.ForeignKeys)
            {
                bool exists = target.ForeignKeys.Any(t =>
                    t.ColumnName == fk.ColumnName &&
                    t.ReferencedTable == fk.ReferencedTable &&
                    t.ReferencedColumn == fk.ReferencedColumn);

                if (!exists)
                {
                    commands.Add($"ALTER TABLE \"{target.Name}\" ADD CONSTRAINT \"{fk.Name}\" FOREIGN KEY (\"{fk.ColumnName}\") REFERENCES \"{fk.ReferencedTable}\"(\"{fk.ReferencedColumn}\");");
                }
            }

            return commands;
        }
        public List<KeyComparisonResult> CompareIndexes(List<string> sourceIndexes, List<string> targetIndexes)
        {
            var results = new List<KeyComparisonResult>();
            var src = sourceIndexes.ToHashSet();
            var tgt = targetIndexes.ToHashSet();

            foreach (var i in src)
            {
                if (!tgt.Contains(i))
                    results.Add(new KeyComparisonResult { SourceName = i, Comparison = "Missing in Target" });
                else
                    results.Add(new KeyComparisonResult { SourceName = i, DestinationName = i, Comparison = "Matching" });
            }

            foreach (var i in tgt.Except(src))
            {
                results.Add(new KeyComparisonResult { DestinationName = i, Comparison = "Missing in Source" });
            }

            return results;
        }
        public List<KeyComparisonResult> CompareSequences(List<string> sourceSequences, List<string> targetSequences)
        {
            var results = new List<KeyComparisonResult>();
            var src = sourceSequences.ToHashSet();
            var tgt = targetSequences.ToHashSet();

            foreach (var s in src)
            {
                if (!tgt.Contains(s))
                    results.Add(new KeyComparisonResult { SourceName = s, Comparison = "Missing in Target" });
                else
                    results.Add(new KeyComparisonResult { SourceName = s, DestinationName = s, Comparison = "Matching" });
            }

            foreach (var s in tgt.Except(src))
            {
                results.Add(new KeyComparisonResult { DestinationName = s, Comparison = "Missing in Source" });
            }

            return results;
        }
        public bool CompareDefinitions(string src, string tgt) => NormalizedDefinition.Normalize(src) == NormalizedDefinition.Normalize(tgt);
    }
}
