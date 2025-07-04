using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Cli;

public class SchemaComparer : ISchemaComparer
{
    public async Task<IList<ComparisonResult>> CompareAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Action<int, int, string>? progressLogger = null, bool runForTest = false)
    {
        var results = new List<ComparisonResult>();

        progressLogger?.Invoke(0, 0, "🔍 Fetching schema objects...");

        var sourceTables = await sourceProvider.GetTablesAsync();
        var targetTables = await targetProvider.GetTablesAsync();

        var sourceTableMap = sourceTables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var targetTableMap = targetTables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        if (runForTest)
        {
            progressLogger?.Invoke(0, 0, "🧪 Running in test mode, comparing top 10 tables only...");

            await CompareTablesAsync(
                sourceTables.Take(10).ToList(),
                targetTables.Take(10).ToList(),
                sourceTableMap,
                targetTableMap,
                results,
                (i, total, tableName) => Console.WriteLine($"Tables compared: {i}/{total} - {tableName}"));

            progressLogger?.Invoke(0, 0, "🧪 Test mode complete. Skipping functions and procedures comparison.");
            return results;
        }

        await CompareTablesAsync(sourceTables, targetTables, sourceTableMap, targetTableMap, results, progressLogger);

        var sourceFunctions = await sourceProvider.GetFunctionsAsync();
        var targetFunctions = await targetProvider.GetFunctionsAsync();
        var sourceFunctionMap = sourceFunctions.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var targetFunctionMap = targetFunctions.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        await CompareFunctionsAsync(sourceProvider, targetProvider, sourceFunctions, targetFunctionMap, results, progressLogger);

        var sourceProcedures = await sourceProvider.GetProceduresAsync();
        var targetProcedures = await targetProvider.GetProceduresAsync();
        var sourceProcedureMap = sourceProcedures.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var targetProcedureMap = targetProcedures.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        await CompareProceduresAsync(sourceProvider, targetProvider, sourceProcedures, targetProcedureMap, results, progressLogger);

        progressLogger?.Invoke(0, 0, "✅ Schema comparison completed.");
        return results;
    }

    private static async Task CompareTablesAsync(
        IList<TableDefinition> sourceTables,
        IList<TableDefinition> targetTables,
        Dictionary<string, TableDefinition> sourceTableMap,
        Dictionary<string, TableDefinition> targetTableMap,
        List<ComparisonResult> results,
        Action<int, int, string>? progressLogger)
    {
        int total = sourceTables.Count;
        for (int i = 0; i < sourceTables.Count; i++)
        {
            var source = sourceTables[i];
            progressLogger?.Invoke(i + 1, total, $"🔄 Comparing table: {source.Name}");

            if (!targetTableMap.TryGetValue(source.Name, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Table, ComparisonStatus.MissingInTarget, "Exists in source, missing in target"));
                continue;
            }

            var subResults = new List<ComparisonSubResult>();
            var overallStatus = ComparisonStatus.Match;

            if (!AreScriptsEqual(source.CreateScript, target.CreateScript))
            {
                overallStatus = ComparisonStatus.Mismatch;
                subResults.Add(new ComparisonSubResult("CreateScript", ComparisonStatus.Mismatch, "Create script differs"));
            }

            var sourcePk = string.Join(",", source.PrimaryKeys);
            var targetPk = string.Join(",", target.PrimaryKeys);
            if (!string.Equals(sourcePk, targetPk, StringComparison.OrdinalIgnoreCase))
            {
                overallStatus = ComparisonStatus.Mismatch;
                subResults.Add(new ComparisonSubResult("PrimaryKeys", ComparisonStatus.Mismatch, $"Primary key mismatch: Source({sourcePk}) vs Target({targetPk})"));
            }

            var sourceCols = source.Columns.OrderBy(c => c.Name).ToList();
            var targetCols = target.Columns.OrderBy(c => c.Name).ToList();
            if (sourceCols.Count != targetCols.Count ||
                !sourceCols.Zip(targetCols, (s, t) =>
                    s.Name == t.Name &&
                    s.DataType == t.DataType &&
                    s.IsNullable == t.IsNullable &&
                    s.DefaultValue == t.DefaultValue).All(match => match))
            {
                overallStatus = ComparisonStatus.Mismatch;
                subResults.Add(new ComparisonSubResult("Columns", ComparisonStatus.Mismatch, "Column definitions differ"));
            }

            var fkMismatch = source.ForeignKeys.Except(target.ForeignKeys).Any() ||
                             target.ForeignKeys.Except(source.ForeignKeys).Any();
            if (fkMismatch)
            {
                overallStatus = ComparisonStatus.Mismatch;
                subResults.Add(new ComparisonSubResult("ForeignKeys", ComparisonStatus.Mismatch, "Foreign key definitions differ"));
            }

            var idxMismatch = source.Indexes.Except(target.Indexes).Any() ||
                              target.Indexes.Except(source.Indexes).Any();
            if (idxMismatch)
            {
                overallStatus = ComparisonStatus.Mismatch;
                subResults.Add(new ComparisonSubResult("Indexes", ComparisonStatus.Mismatch, "Index definitions differ"));
            }

            results.Add(new ComparisonResult
            {
                ObjectType = SchemaObjectType.Table,
                Name = source.Name,
                Status = overallStatus,
                DiffScript = overallStatus == ComparisonStatus.Mismatch ? $"-- SOURCE\n{source.CreateScript}\n\n-- TARGET\n{target.CreateScript}" : null,
                SubResults = subResults
            });
        }

        foreach (var target in targetTables)
        {
            if (!sourceTableMap.TryGetValue(target.Name, out var source))
            {
                var subResults = new List<ComparisonSubResult>
                {
                    new("CreateScript", ComparisonStatus.MissingInSource, "Create script missing in source"),
                    new("PrimaryKeys", ComparisonStatus.MissingInSource, "Primary keys missing in source"),
                    new("Columns", ComparisonStatus.MissingInSource, "Columns missing in source"),
                    new("ForeignKeys", ComparisonStatus.MissingInSource, "Foreign keys missing in source"),
                    new("Indexes", ComparisonStatus.MissingInSource, "Indexes missing in source")
                };

                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Table,
                    Name = target.Name,
                    Status = ComparisonStatus.MissingInSource,
                    Details = "Exists in target, missing in source",
                    SubResults = subResults
                });
            }
        }
    }

    private static async Task CompareFunctionsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        IList<DbFunctionDefinition> sourceFunctions,
        Dictionary<string, DbFunctionDefinition> targetFunctionMap,
        List<ComparisonResult> results,
        Action<int, int, string>? progressLogger)
    {
        int total = sourceFunctions.Count;
        for (int i = 0; i < sourceFunctions.Count; i++)
        {
            var source = sourceFunctions[i];
            progressLogger?.Invoke(i + 1, total, $"⚙️ Comparing function: {source.Name}");

            if (!targetFunctionMap.TryGetValue(source.Name, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Function, ComparisonStatus.MissingInTarget, "Function missing in target"));
                continue;
            }

            var sourceDef = await sourceProvider.GetFunctionDefinitionAsync(source.Name);
            var targetDef = await targetProvider.GetFunctionDefinitionAsync(target.Name);

            if (!AreScriptsEqual(sourceDef, targetDef))
            {
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Function,
                    Name = source.Name,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Function definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                });
            }
            else
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Function, ComparisonStatus.Match));
            }
        }

        foreach (var target in targetFunctionMap.Values)
        {
            if (!sourceFunctions.Any(f => f.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(CreateResult(target.Name, SchemaObjectType.Function, ComparisonStatus.MissingInSource, "Function missing in source"));
            }
        }
    }

    private static async Task CompareProceduresAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        IList<DbFunctionDefinition> sourceProcedures,
        Dictionary<string, DbFunctionDefinition> targetProcedureMap,
        List<ComparisonResult> results,
        Action<int, int, string>? progressLogger)
    {
        int total = sourceProcedures.Count;
        for (int i = 0; i < sourceProcedures.Count; i++)
        {
            var source = sourceProcedures[i];
            progressLogger?.Invoke(i + 1, total, $"🛠 Comparing procedure: {source.Name}");

            if (!targetProcedureMap.TryGetValue(source.Name, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInTarget, "Procedure missing in target"));
                continue;
            }

            var sourceDef = await sourceProvider.GetProcedureDefinitionAsync(source.Name);
            var targetDef = await targetProvider.GetProcedureDefinitionAsync(target.Name);

            if (!AreScriptsEqual(sourceDef, targetDef))
            {
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Procedure,
                    Name = source.Name,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Procedure definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                });
            }
            else
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Procedure, ComparisonStatus.Match));
            }
        }

        foreach (var target in targetProcedureMap.Values)
        {
            if (!sourceProcedures.Any(p => p.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(CreateResult(target.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInSource, "Procedure missing in source"));
            }
        }
    }

    private static ComparisonResult CreateResult(string name, SchemaObjectType type, ComparisonStatus status, string? details = null)
    {
        return new ComparisonResult
        {
            ObjectType = type,
            Name = name,
            Status = status,
            Details = details
        };
    }

    private static bool AreScriptsEqual(string? sourceScript, string? targetScript)
    {
        return string.Equals(sourceScript?.Trim(), targetScript?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
