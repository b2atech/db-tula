using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using System.Text;

namespace B2A.DbTula.Cli;

public class SchemaComparer : ISchemaComparer
{
    public async Task<IList<ComparisonResult>> CompareAsync(
     IDatabaseSchemaProvider sourceProvider,
     IDatabaseSchemaProvider targetProvider,
     Action<int, int, string, bool>? progressLogger = null,
     bool runForTest = false, int testObjectLimit = 10)
    {
        var results = new List<ComparisonResult>();

        progressLogger?.Invoke(0, 0, "🔍 Fetching schema objects...", false);

        var sourceTables = await sourceProvider.GetTablesAsync();
        var targetTables = await targetProvider.GetTablesAsync();

        var sourceTableMap = sourceTables.ToDictionary(t => t, StringComparer.OrdinalIgnoreCase);
        var targetTableMap = targetTables.ToDictionary(t => t, StringComparer.OrdinalIgnoreCase);

        var limitedSourceTables = runForTest ? sourceTables.Take(testObjectLimit).ToList() : sourceTables;
        var limitedTargetTables = runForTest
            ? targetTables.Where(t => limitedSourceTables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList()
            : targetTables;


        progressLogger?.Invoke(0, 0, $"📄 {(runForTest ? $"Test mode: comparing top {testObjectLimit} tables..." : "Comparing tables...")}", false);

        await CompareTablesAsync(
            sourceProvider,
            targetProvider,
            limitedSourceTables,
            limitedTargetTables,
            results,
            progressLogger);

        string GetSignatureKey(DbFunctionDefinition def) =>
                $"{def.Name}({string.Join(",", def.Arguments)})";

        var sourceFunctions = await sourceProvider.GetFunctionsAsync();
        var targetFunctions = await targetProvider.GetFunctionsAsync();
        var sourceFunctionMap = sourceFunctions.ToDictionary(f => GetSignatureKey(f), StringComparer.OrdinalIgnoreCase);

        var targetFunctionMap = targetFunctions.ToDictionary(f => GetSignatureKey(f), StringComparer.OrdinalIgnoreCase);

        var limitedSourceFunctions = runForTest
                        ? sourceFunctionMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                        : sourceFunctionMap;

        var limitedTargetFunctionMap = targetFunctionMap
         .Where(kvp => limitedSourceFunctions.ContainsKey(kvp.Key))
         .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"⚙️ {(runForTest ? $"Test mode: comparing top {testObjectLimit} functions..." : "Comparing functions...")}", false);

        await CompareFunctionsAsync(
           sourceProvider,
           targetProvider,
           limitedSourceFunctions,
           limitedTargetFunctionMap,
           results,
           progressLogger);

        var sourceProcedures = await sourceProvider.GetProceduresAsync();
        var targetProcedures = await targetProvider.GetProceduresAsync();

        // Build signature-keyed dictionaries for both
        var sourceProcedureMap = sourceProcedures.ToDictionary(p => GetSignatureKey(p), StringComparer.OrdinalIgnoreCase);
        var targetProcedureMap = targetProcedures.ToDictionary(p => GetSignatureKey(p), StringComparer.OrdinalIgnoreCase);

        // Limit to top 10 for testing, using signature keys
        var limitedSourceProcedureMap = runForTest
            ? sourceProcedureMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceProcedureMap;

        var limitedTargetProcedureMap = targetProcedureMap
            .Where(kvp => limitedSourceProcedureMap.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"🛠 {(runForTest ? $"Test mode: comparing top {testObjectLimit} procedures..." : "Comparing procedures...")}", false);

        // Pass signature-based maps instead of a list
        await CompareProceduresAsync(
            sourceProvider,
            targetProvider,
            limitedSourceProcedureMap,
            limitedTargetProcedureMap,
            results,
            progressLogger);
        progressLogger?.Invoke(0, 0, "✅ Schema comparison completed.", false);
        return results;
    }



    private static async Task CompareTablesAsync(
    IDatabaseSchemaProvider sourceProvider,
    IDatabaseSchemaProvider targetProvider,
    IList<string> sourceTables,
    IList<string> targetTables,
    List<ComparisonResult> results,
    Action<int, int, string, bool>? progressLogger)
    {
        int total = sourceTables.Count;
        var targetTableSet = new HashSet<string>(targetTables, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < total; i++)
        {
            var tableName = sourceTables[i];
            progressLogger?.Invoke(i + 1, total, $"🔄 Comparing table: {tableName}", true);

            var source = await sourceProvider.GetTableDefinitionAsync(tableName);
            var target = targetTableSet.Contains(tableName)
                ? await targetProvider.GetTableDefinitionAsync(tableName)
                : null;

            if (target == null)
            {
                results.Add(CreateResult(tableName, SchemaObjectType.Table, ComparisonStatus.MissingInTarget, "Exists in source, missing in target"));
                continue;
            }

            var subResults = new List<ComparisonSubResult>();
            var overallStatus = ComparisonStatus.Match;

            if (!AreScriptsEqual(source.CreateScript, target.CreateScript))
            {
                overallStatus = ComparisonStatus.Mismatch;
                subResults.Add(new ComparisonSubResult(
                    "CreateScript",
                    ComparisonStatus.Mismatch,
                    "Create script differs",
                    $"-- SOURCE\n{source.CreateScript}\n\n-- TARGET\n{target.CreateScript}"
                ));
            }

            overallStatus |= await ComparePrimaryKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= CompareColumns(source, target, subResults);
            overallStatus |= await CompareForeignKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= await CompareIndexesAsync(sourceProvider, targetProvider, source, target, subResults);

            var diffScript = BuildDiffScript(source, target, subResults, overallStatus);

            var res = new ComparisonResult
            {
                ObjectType = SchemaObjectType.Table,
                Name = source.Name,
                Status = overallStatus,
                DiffScript = diffScript,
                SubResults = subResults
            };
            results.Add(res);
            Console.WriteLine(res.HasPrimaryKeyMismatch);
        }

        var sourceTableSet = new HashSet<string>(sourceTables, StringComparer.OrdinalIgnoreCase);
        foreach (var targetTable in targetTables.Where(t => !sourceTableSet.Contains(t)))
        {
            var result = await HandleTableMissingInSourceAsync(targetProvider, targetTable);
            results.Add(result);
        }
    }

    private static async Task<ComparisonStatus> ComparePrimaryKeysAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var sourcePk = source.PrimaryKeys.FirstOrDefault();
        var targetPk = target.PrimaryKeys.FirstOrDefault();

        if (sourcePk == null && targetPk != null)
        {
            var script = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInSource, $"Primary key missing in source: {targetPk.Name}", script));
            return ComparisonStatus.Mismatch;
        }
        else if (targetPk == null && sourcePk != null)
        {
            var script = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInTarget, $"Primary key missing in target: {sourcePk.Name}", script));
            return ComparisonStatus.Mismatch;
        }

        return ComparisonStatus.Match;
    }

    private static ComparisonStatus CompareColumns(TableDefinition source, TableDefinition target, List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;
        var sourceCols = source.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetCols = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var col in sourceCols.Values)
        {
            if (!targetCols.TryGetValue(col.Name, out var tgt))
            {
                status = ComparisonStatus.Mismatch;
                subResults.Add(new("Columns", ComparisonStatus.MissingInTarget, $"Column '{col.Name}' is missing in target.",
                    $"ALTER TABLE \"{source.Name}\" ADD COLUMN \"{col.Name}\" {col.DataType} {(col.IsNullable ? "" : "NOT NULL")};"));
            }
            else if (!col.Equals(tgt))
            {
                status = ComparisonStatus.Mismatch;
                subResults.Add(new("Columns", ComparisonStatus.Mismatch,
                    $"Column '{col.Name}' definition differs: source({col.DataType}) vs target({tgt.DataType})", string.Empty));
            }
        }

        foreach (var col in targetCols.Values)
        {
            if (!sourceCols.ContainsKey(col.Name))
            {
                status = ComparisonStatus.Mismatch;
                subResults.Add(new("Columns", ComparisonStatus.MissingInSource,
                    $"Column '{col.Name}' is missing in source (may be deprecated).", string.Empty));
            }
        }

        return status;
    }

    private static async Task<ComparisonStatus> CompareForeignKeysAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;

        var sourceFksByName = source.ForeignKeys
            .GroupBy(fk => fk.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetFksByName = target.ForeignKeys
            .GroupBy(fk => fk.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allFkNames = sourceFksByName.Keys.Union(targetFksByName.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var fkName in allFkNames)
        {
            var inSource = sourceFksByName.TryGetValue(fkName, out var sourceFk);
            var inTarget = targetFksByName.TryGetValue(fkName, out var targetFk);

            if (inSource && inTarget)
            {
                if (!ForeignKeyEquals(sourceFk, targetFk))
                {
                    var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);
                    subResults.Add(new("ForeignKeys", ComparisonStatus.Mismatch, $"Foreign key '{fkName}' differs between source and target", script));
                    status = ComparisonStatus.Mismatch;
                }
            }
            else if (inSource && !inTarget)
            {
                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);
                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInTarget, $"Foreign key '{fkName}' missing in target", script));
                status = ComparisonStatus.Mismatch;
            }
            else if (!inSource && inTarget)
            {
                var script = await targetProvider.GetForeignKeyCreateScriptAsync(target.Name, targetFk.Name);
                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInSource, $"Foreign key '{fkName}' missing in source", script));
                status = ComparisonStatus.Mismatch;
            }
        }
        return status;
    }
    private static bool ForeignKeyEquals(ForeignKeyDefinition a, ForeignKeyDefinition b)
    {
        return
            string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ColumnName, b.ColumnName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ReferencedTable, b.ReferencedTable, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ReferencedColumn, b.ReferencedColumn, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ComparisonStatus> CompareIndexesAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;
        var sourceIndexes = source.Indexes.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var targetIndexes = target.Indexes.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var index in sourceIndexes.Values.Where(i => !targetIndexes.ContainsKey(i.Name)))
        {
            var script = await sourceProvider.GetIndexCreateScriptAsync(index.Name);
            subResults.Add(new("Indexes", ComparisonStatus.MissingInTarget,
                $"Index '{index.Name}' is missing in target.", script));
            status = ComparisonStatus.Mismatch;
        }

        foreach (var index in targetIndexes.Values.Where(i => !sourceIndexes.ContainsKey(i.Name)))
        {
            var script = await targetProvider.GetIndexCreateScriptAsync(index.Name);
            subResults.Add(new("Indexes", ComparisonStatus.MissingInSource,
                $"Index '{index.Name}' exists in target but not in source.",
                $"-- Optionally drop: DROP INDEX IF EXISTS \"{index.Name}\";"));
            status = ComparisonStatus.Mismatch;
        }

        return status;
    }

    private static string? BuildDiffScript(TableDefinition source, TableDefinition target, List<ComparisonSubResult> subResults, ComparisonStatus status)
    {
        if (status == ComparisonStatus.Match) return null;

        var sb = new StringBuilder();
        foreach (var sub in subResults.Where(s => !string.IsNullOrWhiteSpace(s.CreateScript)))
        {
            sb.AppendLine($"-- {sub.Component}: {sub.Status}");
            sb.AppendLine(sub.CreateScript!.Trim());
            sb.AppendLine();
        }

        if (sb.Length == 0)
        {
            sb.AppendLine("-- SOURCE");
            sb.AppendLine(source.CreateScript?.Trim());
            sb.AppendLine("-- TARGET");
            sb.AppendLine(target.CreateScript?.Trim());
        }

        return sb.ToString().Trim();
    }

    private static async Task<ComparisonResult> HandleTableMissingInSourceAsync(IDatabaseSchemaProvider provider, string tableName)
    {
        var table = await provider.GetTableDefinitionAsync(tableName);
        var subResults = new List<ComparisonSubResult>();

        if (!string.IsNullOrWhiteSpace(table.CreateScript))
            subResults.Add(new("CreateScript", ComparisonStatus.MissingInSource, "Create script missing in source", table.CreateScript));

        var pk = table.PrimaryKeys.FirstOrDefault();
        if (pk != null)
        {
            var pkScript = await provider.GetPrimaryKeyCreateScriptAsync(table.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInSource, "Primary key missing in source", pkScript));
        }

        foreach (var col in table.Columns)
        {
            subResults.Add(new("Columns", ComparisonStatus.MissingInSource,
                $"Column '{col.Name}' is missing in source",
                $"ALTER TABLE \"{table.Name}\" ADD COLUMN \"{col.Name}\" {col.DataType} {(col.IsNullable ? "" : "NOT NULL")};"));
        }

        foreach (var fk in table.ForeignKeys)
        {
            var fkScript = await provider.GetForeignKeyCreateScriptAsync(table.Name, fk.Name);
            subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInSource, $"Foreign key '{fk.Name}' is missing in source", fkScript));
        }

        foreach (var idx in table.Indexes)
        {
            var idxScript = await provider.GetIndexCreateScriptAsync(idx.Name);
            subResults.Add(new("Indexes", ComparisonStatus.MissingInSource, $"Index '{idx.Name}' is missing in source", idxScript));
        }

        return new ComparisonResult
        {
            ObjectType = SchemaObjectType.Table,
            Name = table.Name,
            Status = ComparisonStatus.MissingInSource,
            Details = "Exists in target, missing in source",
            DiffScript = BuildDiffScript(table, table, subResults, ComparisonStatus.MissingInSource),
            SubResults = subResults
        };
    }


    private static async Task CompareFunctionsAsync(
    IDatabaseSchemaProvider sourceProvider,
    IDatabaseSchemaProvider targetProvider,
    Dictionary<string, DbFunctionDefinition> sourceFunctions, // Key = signature
    Dictionary<string, DbFunctionDefinition> targetFunctionMap, // Key = signature
    List<ComparisonResult> results,
    Action<int, int, string, bool>? progressLogger)
    {
        int total = sourceFunctions.Count;
        int index = 0;

        foreach (var kvp in sourceFunctions)
        {
            index++;
            var signature = kvp.Key;
            var source = kvp.Value;

            progressLogger?.Invoke(index, total, $"⚙️ Comparing function: {source.Name}", true);

            if (!targetFunctionMap.TryGetValue(signature, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Function, ComparisonStatus.MissingInTarget, source.Definition));
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

        foreach (var kvp in targetFunctionMap)
        {
            if (!sourceFunctions.ContainsKey(kvp.Key))
            {
                var target = kvp.Value;
                results.Add(CreateResult(target.Name, SchemaObjectType.Function, ComparisonStatus.MissingInSource, target.Definition));
            }
        }
    }


    private static async Task CompareProceduresAsync(
    IDatabaseSchemaProvider sourceProvider,
    IDatabaseSchemaProvider targetProvider,
    Dictionary<string, DbFunctionDefinition> sourceProcedureMap,
    Dictionary<string, DbFunctionDefinition> targetProcedureMap,
    List<ComparisonResult> results,
    Action<int, int, string, bool>? progressLogger)
    {
        int total = sourceProcedureMap.Count;
        int index = 0;

        foreach (var kvp in sourceProcedureMap)
        {
            var signatureKey = kvp.Key;
            var source = kvp.Value;
            progressLogger?.Invoke(++index, total, $"🛠 Comparing procedure: {source.Name}", true);

            if (!targetProcedureMap.TryGetValue(signatureKey, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInTarget, source.Definition));
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

        foreach (var kvp in targetProcedureMap)
        {
            if (!sourceProcedureMap.ContainsKey(kvp.Key))
            {
                var target = kvp.Value;
                results.Add(CreateResult(target.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInSource, target.Definition));
            }
        }
    }


    private static ComparisonResult CreateResult(string name, SchemaObjectType type, ComparisonStatus status, string diffScript = "")
    {
        return new ComparisonResult
        {
            ObjectType = type,
            Name = name,
            Status = status,
            DiffScript = diffScript
        };
    }

    private static bool AreScriptsEqual(string? sourceScript, string? targetScript)
    {
        return string.Equals(sourceScript?.Trim(), targetScript?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
