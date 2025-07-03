using b2a.db_tula.core.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Xml.Linq;

namespace b2a.db_tula.core
{
    public class SchemaComparer
    {
        //public List<ComparisonResult> CompareTables(SchemaFetcher sourceFetcher, SchemaFetcher targetFetcher, DataTable sourceTables, DataTable targetTables, Action<int, int, string> progressLogger)
        //{
        //    var differences = new List<ComparisonResult>();

        //    var sourceNames = sourceTables.Rows.Cast<DataRow>().Select(r => r["table_name"].ToString()).ToHashSet();
        //    var targetNames = targetTables.Rows.Cast<DataRow>().Select(r => r["table_name"].ToString()).ToHashSet();
        //    int total = sourceNames.Count + targetNames.Count;
        //    int index = 0;

        //    foreach (var name in sourceNames)
        //    {
        //        if (!targetNames.Contains(name))
        //        {
        //            differences.Add(new ComparisonResult
        //            {
        //                Type = "Table",
        //                SourceName = name,
        //                Comparison = ComparisonType.MissingInTarget
        //            });
        //        }
        //        else
        //        {
        //            var src = sourceFetcher.GetTableDefinition(name);
        //            var tgt = targetFetcher.GetTableDefinition(name);
        //            var result = new ComparisonResult
        //            {
        //                Type = "Table",
        //                SourceName = name,
        //                DestinationName = name,
        //                Comparison = ComparisonType.Same
        //            };

        //            foreach (var col in src.Columns)
        //            {
        //                var tgtCol = tgt.Columns.FirstOrDefault(c => c.Name == col.Name);
        //                if (tgtCol == null)
        //                {
        //                    result.ColumnComparisonResults.Add(new ColumnComparisonResult
        //                    {
        //                        SourceName = col.Name,
        //                        Comparison = ComparisonType.MissingInTarget
        //                    });
        //                    result.Comparison = ComparisonType.Changed;
        //                }
        //                else if (col.DataType != tgtCol.DataType)
        //                {
        //                    result.ColumnComparisonResults.Add(new ColumnComparisonResult
        //                    {
        //                        SourceName = col.Name,
        //                        SourceType = col.DataType,
        //                        DestinationType = tgtCol.DataType,
        //                        Comparison = ComparisonType.Changed
        //                    });
        //                    result.Comparison = ComparisonType.Changed;
        //                }
        //            }

        //            foreach (var col in tgt.Columns)
        //            {
        //                if (!src.Columns.Any(c => c.Name == col.Name))
        //                {
        //                    result.ColumnComparisonResults.Add(new ColumnComparisonResult
        //                    {
        //                        DestinationName = col.Name,
        //                        Comparison = ComparisonType.MissingInSource
        //                    });
        //                    result.Comparison = ComparisonType.Changed;
        //                }
        //            }

        //            differences.Add(result);
        //        }

        //        index++;
        //        progressLogger?.Invoke(index, total, name);
        //    }

        //    foreach (var name in targetNames)
        //    {
        //        if (!sourceNames.Contains(name))
        //        {
        //            differences.Add(new ComparisonResult
        //            {
        //                Type = "Table",
        //                DestinationName = name,
        //                Comparison = ComparisonType.MissingInSource
        //            });
        //        }
        //        index++;
        //        progressLogger?.Invoke(index, total, name);
        //    }

        //    return differences;
        //}


        public async Task<List<ComparisonResult>> CompareTablesAsync(
    SchemaFetcher sourceFetcher,
    SchemaFetcher targetFetcher,
    DataTable sourceTables,
    DataTable targetTables,
    Action<int, int, string> progressLogger)
        {
            var differences = new List<ComparisonResult>();

            var sourceNames = sourceTables.Rows.Cast<DataRow>().Select(r => r["table_name"].ToString()).ToHashSet();
            var targetNames = targetTables.Rows.Cast<DataRow>().Select(r => r["table_name"].ToString()).ToHashSet();
            int total = sourceNames.Count + targetNames.Count;
            int index = 0;

            foreach (var name in sourceNames)
            {
                var src = await sourceFetcher.GetTableDefinitionAsync(name);
                var tgt = await targetFetcher.GetTableDefinitionAsync(name);

                if (!targetNames.Contains(name))
                {
                    var result = CreateMissingInTargetResult(name);
                    differences.Add(result);
                    result.SourceDefinition = src;
                    result.DestinationDefinition = tgt; 
                }
                else
                {
                    var result = CompareTableStructure(name, src, tgt, sourceFetcher, targetFetcher);
                    differences.Add(result);
                    result.SourceDefinition = src;
                    result.DestinationDefinition = tgt;
                }

                index++;
                progressLogger?.Invoke(index, total, name);
            }

            foreach (var name in targetNames.Except(sourceNames))
            {
                differences.Add(CreateMissingInSourceResult(name));
                index++;
                progressLogger?.Invoke(index, total, name);
            }

            return differences;
        }

        private ComparisonResult CreateMissingInTargetResult(string name) => new()
        {
            Type = "Table",
            SourceName = name,
            Comparison = ComparisonType.MissingInTarget
        };

        private ComparisonResult CreateMissingInSourceResult(string name) => new()
        {
            Type = "Table",
            DestinationName = name,
            Comparison = ComparisonType.MissingInSource
        };


        private ComparisonResult CompareTableStructure(
            string tableName,
            TableDefinition src,
            TableDefinition tgt,
            SchemaFetcher sourceFetcher,
            SchemaFetcher targetFetcher)
        {
            var result = new ComparisonResult
            {
                Type = "Table",
                SourceName = tableName,
                DestinationName = tableName,
                Comparison = ComparisonType.Same
            };

            CompareColumns(src, tgt, result);
            result.PrimaryKeyComparisonResults = ComparePrimaryKeys(src, tgt, result);
            result.ForeignKeyComparisonResults = CompareForeignKeys(src, tgt, result);


            var srcIndexes = ParseIndexes(sourceFetcher.GetIndexesAsync(src.Name).GetAwaiter().GetResult());
            var tgtIndexes = ParseIndexes(targetFetcher.GetIndexesAsync(tgt.Name).GetAwaiter().GetResult());

            result.IndexComparisonResults = CompareIndexesByDefinition(srcIndexes, tgtIndexes, result);


            return result;
        }

        private List<IndexDefinition> ParseIndexes(DataTable table)
        {
            return table.AsEnumerable()
                .GroupBy(r => r["indexname"].ToString())
                .Select(g => new IndexDefinition
                {
                    IndexName = g.Key,
                    TableName = g.First()["tablename"].ToString(),
                    IsUnique = Convert.ToBoolean(g.First()["is_unique"]),
                    IndexType = g.First()["index_type"].ToString(),
                    Columns = g.Select(r => r["columnname"].ToString()).ToList()
                })
                .ToList();
        }

        private void CompareColumns(TableDefinition src, TableDefinition tgt, ComparisonResult result)
        {
            foreach (var col in src.Columns)
            {
                var tgtCol = tgt.Columns.FirstOrDefault(c => c.Name == col.Name);
                if (tgtCol == null)
                {
                    result.ColumnComparisonResults.Add(new ColumnComparisonResult
                    {
                        SourceName = col.Name,
                        Comparison = ComparisonType.MissingInTarget
                    });
                    result.Comparison = ComparisonType.Changed;
                }
                else if (col.DataType != tgtCol.DataType)
                {
                    result.ColumnComparisonResults.Add(new ColumnComparisonResult
                    {
                        SourceName = col.Name,
                        SourceType = col.DataType,
                        DestinationType = tgtCol.DataType,
                        Comparison = ComparisonType.Changed
                    });
                    result.Comparison = ComparisonType.Changed;
                }
            }

            foreach (var col in tgt.Columns)
            {
                if (!src.Columns.Any(c => c.Name == col.Name))
                {
                    result.ColumnComparisonResults.Add(new ColumnComparisonResult
                    {
                        DestinationName = col.Name,
                        Comparison = ComparisonType.MissingInSource
                    });
                    result.Comparison = ComparisonType.Changed;
                }
            }
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
                    differences.Add(new ComparisonResult
                    {
                        Type = type,
                        SourceName = name,
                        Comparison = ComparisonType.MissingInTarget
                    });
                }
                else
                {
                    var src = sourceFetcher.GetFunctionOrProcedureDefinitionAsync(name).GetAwaiter().GetResult();
                    var tgt = targetFetcher.GetFunctionOrProcedureDefinitionAsync(name).GetAwaiter().GetResult();
                    var match = NormalizedDefinition.Normalize(src) == NormalizedDefinition.Normalize(tgt);

                    differences.Add(new ComparisonResult
                    {
                        Type = type,
                        SourceName = name,
                        DestinationName = name,
                        SourceFuncOrProcDefinition = src,
                        DestinationFuncOrProcDefinition = tgt,
                        Comparison = match ? ComparisonType.Same : ComparisonType.Changed
                    });
                }

                index++;
                progressLogger?.Invoke(index, total, name);
            }

            foreach (var name in targetNames)
            {
                if (!sourceNames.Contains(name))
                {
                    differences.Add(new ComparisonResult
                    {
                        Type = type,
                        DestinationName = name,
                        Comparison = ComparisonType.MissingInSource
                    });
                }
                index++;
                progressLogger?.Invoke(index, total, name);
            }

            return differences;
        }

        public List<KeyComparisonResult> ComparePrimaryKeys(TableDefinition source,TableDefinition target, ComparisonResult result)
        {
            var results = new List<KeyComparisonResult>();
            var srcKeys = source.PrimaryKeys.ToHashSet();
            var tgtKeys = target.PrimaryKeys.ToHashSet();

            foreach (var key in srcKeys)
            {
                if (!tgtKeys.Contains(key))
                {
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = key,
                        Comparison = ComparisonType.MissingInTarget
                    });
                    result.Comparison = ComparisonType.Changed;
                }
                else
                {
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = key,
                        DestinationName = key,
                        Comparison = ComparisonType.Same
                    });
                }
            }

            foreach (var key in tgtKeys.Except(srcKeys))
            {
                results.Add(new KeyComparisonResult
                {
                    DestinationName = key,
                    Comparison = ComparisonType.MissingInSource
                });
                result.Comparison = ComparisonType.Changed;
            }

            return results;
        }


        public List<KeyComparisonResult> CompareForeignKeys(
    TableDefinition source,
    TableDefinition target,
    ComparisonResult result)
        {
            var results = new List<KeyComparisonResult>();

            foreach (var fk in source.ForeignKeys)
            {
                var exists = target.ForeignKeys.Any(t =>
                    t.ColumnName == fk.ColumnName &&
                    t.ReferencedTable == fk.ReferencedTable &&
                    t.ReferencedColumn == fk.ReferencedColumn);

                if (exists)
                {
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = fk.Name,
                        DestinationName = fk.Name,
                        Comparison = ComparisonType.Same
                    });
                }
                else
                {
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = fk.Name,
                        DestinationName = null,
                        Comparison = ComparisonType.MissingInTarget
                    });

                    result.Comparison = ComparisonType.Changed;
                }
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
                        Comparison = ComparisonType.MissingInSource
                    });

                    result.Comparison = ComparisonType.Changed;
                }
            }

            return results;
        }


        public List<KeyComparisonResult> CompareIndexesByDefinition(
     List<IndexDefinition> sourceIndexes,
     List<IndexDefinition> targetIndexes,
     ComparisonResult result)
        {
            var results = new List<KeyComparisonResult>();
            var matchedTargetIndexes = new HashSet<string>();

            foreach (var src in sourceIndexes)
            {
                var match = targetIndexes.FirstOrDefault(tgt =>
                    tgt.TableName == src.TableName &&
                    tgt.IsUnique == src.IsUnique &&
                    tgt.IndexType == src.IndexType &&
                    tgt.Columns.SequenceEqual(src.Columns, StringComparer.OrdinalIgnoreCase));

                if (match != null)
                {
                    matchedTargetIndexes.Add(match.IndexName);
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = src.IndexName,
                        DestinationName = match.IndexName,
                        Comparison = ComparisonType.Same
                    });
                }
                else
                {
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = src.IndexName,
                        DestinationName = null,
                        Comparison = ComparisonType.MissingInTarget
                    });
                    result.Comparison = ComparisonType.Changed;
                }
            }

            foreach (var tgt in targetIndexes)
            {
                if (!matchedTargetIndexes.Contains(tgt.IndexName))
                {
                    results.Add(new KeyComparisonResult
                    {
                        SourceName = null,
                        DestinationName = tgt.IndexName,
                        Comparison = ComparisonType.ExtraInTarget
                    });
                    result.Comparison = ComparisonType.Changed;
                }
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
                    results.Add(new KeyComparisonResult { SourceName = s, Comparison = ComparisonType.MissingInTarget });
                else
                    results.Add(new KeyComparisonResult { SourceName = s, DestinationName = s, Comparison = ComparisonType.Same });
            }

            foreach (var s in tgt.Except(src))
            {
                results.Add(new KeyComparisonResult { DestinationName = s, Comparison = ComparisonType.MissingInSource });
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

            if (!source.PrimaryKeyColumns.SequenceEqual(target.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase))
            {
                commands.Add($"ALTER TABLE \"{target.Name}\" DROP CONSTRAINT IF EXISTS \"{target.Name}_pkey\";");
                commands.Add($"ALTER TABLE \"{target.Name}\" ADD PRIMARY KEY ({string.Join(", ", source.PrimaryKeyColumns.Select(pk => $"\"{pk}\""))});");
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

        public bool CompareDefinitions(string src, string tgt) =>
            NormalizedDefinition.Normalize(src) == NormalizedDefinition.Normalize(tgt);
    }
}
