using B2A.DbTula.Cli.Services;
using B2A.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using B2A.DbTula.Core.Utilities;
using B2a.DbTula.Core.Abstractions;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace B2A.DbTula.Cli;

/// <summary>
/// Enhanced Schema Comparer with Order-Independent Semantic Comparison
/// 
/// Features:
/// - Table comparison is order-independent and semantic using structural signatures
/// - Columns compared by normalized structure (name, type, nullability, default, identity), order does NOT matter
/// - Constraints (PK, FK, Unique, Check) compared by structure, not name/text, order does NOT matter
/// - FKs deduplicated by structural key to avoid double-counting
/// - Indexes compared by method + columns + options + predicate, not name or SQL text
/// - Primary Key and Index Detection with provider-specific logic (Postgres/MySQL)
/// - Robust handling of partitioned tables, inherited tables, materialized views, invalid indexes
/// - Predicate normalization for partial indexes (whitespace-insensitive)
/// - Provider type detection for Postgres vs MySQL specific behavior
/// - Functions, procedures, views, triggers compared by normalized signature
/// - Backward compatible output with maximized correctness and clarity
/// - Well-commented, maintainable, and extensible code structure
/// </summary>
/// 


/// <summary>
/// QA -> PROD focused schema comparer.
/// 
/// Source = QA/reference.
/// Target = PROD.
/// 
/// This comparer answers:
/// "What exists in QA but is missing or different in PROD?"
/// 
/// Important:
/// - Extra objects in PROD are ignored by default.
/// - Extra FK/index/default in PROD will NOT mark the table as mismatch.
/// - Table status is decided only by visible sub-results.
/// - No hidden StructuralEquals mismatch is allowed when IncludeMissingInSource = false.
/// </summary>
/// 
 

/// <summary>
/// QA -> PROD focused schema comparer.
/// 
/// Source = QA/reference.
/// Target = PROD.
/// 
/// This comparer answers:
/// "What exists in QA but is missing or different in PROD?"
/// 
/// Important:
/// - Extra objects in PROD are ignored by default.
/// - Extra FK/index/default in PROD will NOT mark the table as mismatch.
/// - Table status is calculated only from actual visible sub-results.
/// - No hidden StructuralEquals mismatch is used in one-way QA -> PROD mode.
/// </summary>
public class SchemaComparer : ISchemaComparer
{
    private const int TableMaxDegreeOfParallelism = 4;

    /// <summary>
    /// Keep false for your current requirement:
    /// false = show only what exists in QA but is missing/different in PROD.
    /// true  = also show what exists in PROD but not in QA.
    /// </summary>
    private const bool IncludeMissingInSource = false;

    private readonly SqlDiffService _sqlDiffService;

    public SchemaComparer()
    {
        _sqlDiffService = new SqlDiffService();
    }

    public async Task<IList<ComparisonResult>> CompareAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Action<int, int, string, bool>? progressLogger = null,
        bool runForTest = false,
        int testObjectLimit = 10,
        ComparisonOptions? options = null)
    {
        options ??= new ComparisonOptions();

        progressLogger?.Invoke(0, 0, "🔍 Fetching schema objects...", false);

        var sourceTablesTask = sourceProvider.GetTablesAsync();
        var targetTablesTask = targetProvider.GetTablesAsync();

        var sourceFunctionsTask = sourceProvider.GetFunctionsAsync();
        var targetFunctionsTask = targetProvider.GetFunctionsAsync();

        var sourceProceduresTask = sourceProvider.GetProceduresAsync();
        var targetProceduresTask = targetProvider.GetProceduresAsync();

        var sourceViewsTask = sourceProvider.GetViewsAsync();
        var targetViewsTask = targetProvider.GetViewsAsync();

        var sourceTriggersTask = sourceProvider.GetTriggersAsync();
        var targetTriggersTask = targetProvider.GetTriggersAsync();

        await Task.WhenAll(
            sourceTablesTask,
            targetTablesTask,
            sourceFunctionsTask,
            targetFunctionsTask,
            sourceProceduresTask,
            targetProceduresTask,
            sourceViewsTask,
            targetViewsTask,
            sourceTriggersTask);

        var sourceTables = await sourceTablesTask;
        var targetTables = await targetTablesTask;

        var sourceFunctions = await sourceFunctionsTask;
        var targetFunctions = await targetFunctionsTask;

        var sourceProcedures = await sourceProceduresTask;
        var targetProcedures = await targetProceduresTask;

        var sourceViews = await sourceViewsTask;
        var targetViews = await targetViewsTask;

        var sourceTriggers = await sourceTriggersTask;
        var targetTriggers = await targetTriggersTask;

        progressLogger?.Invoke(
            0,
            0,
            $"📦 Fetched objects | Source: Tables={sourceTables.Count}, Functions={sourceFunctions.Count}, Procedures={sourceProcedures.Count}, Views={sourceViews.Count}, Triggers={sourceTriggers.Count} | " +
            $"Target: Tables={targetTables.Count}, Functions={targetFunctions.Count}, Procedures={targetProcedures.Count}, Views={targetViews.Count}, Triggers={targetTriggers.Count}",
            false);

        var limitedSourceTables = runForTest
            ? sourceTables.Take(testObjectLimit).ToList()
            : sourceTables;

        var limitedTargetTables = targetTables;

        var sourceFunctionMap = BuildRoutineMap(sourceFunctions);
        var targetFunctionMap = BuildRoutineMap(targetFunctions);

        var sourceProcedureMap = BuildRoutineMap(sourceProcedures);
        var targetProcedureMap = BuildRoutineMap(targetProcedures);

        var sourceViewMap = BuildViewMap(sourceViews);
        var targetViewMap = BuildViewMap(targetViews);

        var sourceTriggerMap = BuildTriggerMap(sourceTriggers);
        var targetTriggerMap = BuildTriggerMap(targetTriggers);

        if (runForTest)
        {
            sourceFunctionMap = LimitMap(sourceFunctionMap, testObjectLimit);
            sourceProcedureMap = LimitMap(sourceProcedureMap, testObjectLimit);
            sourceViewMap = LimitMap(sourceViewMap, testObjectLimit);
            sourceTriggerMap = LimitMap(sourceTriggerMap, testObjectLimit);
        }

        var tableResults = new List<ComparisonResult>();
        var functionResults = new List<ComparisonResult>();
        var procedureResults = new List<ComparisonResult>();
        var viewResults = new List<ComparisonResult>();
        var triggerResults = new List<ComparisonResult>();

        progressLogger?.Invoke(
            0,
            0,
            "🚀 Comparing tables, functions, procedures, views and triggers in parallel...",
            false);

        await Task.WhenAll(
            CompareTablesAsync(
                sourceProvider,
                targetProvider,
                limitedSourceTables,
                limitedTargetTables,
                tableResults,
                progressLogger,
                options),

            CompareFunctionsAsync(
                sourceProvider,
                targetProvider,
                sourceFunctionMap,
                targetFunctionMap,
                functionResults,
                progressLogger,
                options),

            CompareProceduresAsync(
                sourceProvider,
                targetProvider,
                sourceProcedureMap,
                targetProcedureMap,
                procedureResults,
                progressLogger,
                options),

            CompareViewsAsync(
                sourceProvider,
                targetProvider,
                sourceViewMap,
                targetViewMap,
                viewResults,
                progressLogger,
                options),

            CompareTriggersAsync(
                sourceProvider,
                targetProvider,
                sourceTriggerMap,
                targetTriggerMap,
                triggerResults,
                progressLogger,
                options));

        var results = new List<ComparisonResult>();

        results.AddRange(tableResults.OrderBy(x => x.Name));
        results.AddRange(functionResults.OrderBy(x => x.Name));
        results.AddRange(procedureResults.OrderBy(x => x.Name));
        results.AddRange(viewResults.OrderBy(x => x.Name));
        results.AddRange(triggerResults.OrderBy(x => x.Name));

        progressLogger?.Invoke(
            0,
            0,
            $"✅ Schema comparison completed. Total={results.Count}, Match={results.Count(x => x.Status == ComparisonStatus.Match)}, Mismatch={results.Count(x => x.Status == ComparisonStatus.Mismatch)}, MissingInTarget={results.Count(x => x.Status == ComparisonStatus.MissingInTarget)}, MissingInSource={results.Count(x => x.Status == ComparisonStatus.MissingInSource)}",
            false);

        return results;
    }

    private async Task CompareTablesAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        IList<string> sourceTables,
        IList<string> targetTables,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        progressLogger?.Invoke(
            0,
            0,
            $"📄 Comparing tables with parallelism={TableMaxDegreeOfParallelism}...",
            false);

        var targetTableSet = new HashSet<string>(targetTables, StringComparer.OrdinalIgnoreCase);
        var resultBag = new ConcurrentBag<ComparisonResult>();

        var completed = 0;
        var total = sourceTables.Count;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = TableMaxDegreeOfParallelism
        };

        await Parallel.ForEachAsync(sourceTables, parallelOptions, async (tableName, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref completed);

            if (ShouldLogProgress(current, total))
            {
                progressLogger?.Invoke(current, total, $"🔄 Comparing table: {tableName}", true);
            }

            var source = await sourceProvider.GetTableDefinitionAsync(tableName);

            var target = targetTableSet.Contains(tableName)
                ? await targetProvider.GetTableDefinitionAsync(tableName)
                : null;

            if (target == null)
            {
                resultBag.Add(CreateResult(
                    tableName,
                    SchemaObjectType.Table,
                    ComparisonStatus.MissingInTarget,
                    $"-- Table '{tableName}' exists in source but is missing in target.\n{source.CreateScript}"));

                return;
            }

            var subResults = new List<ComparisonSubResult>();

            await ComparePrimaryKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            CompareColumns(source, target, subResults);
            await CompareForeignKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            await CompareIndexesAsync(sourceProvider, targetProvider, source, target, subResults);
            await CompareConstraintsAsync(sourceProvider, targetProvider, source, target, subResults);

            var overallStatus = GetOverallStatusFromSubResults(subResults);

            var result = new ComparisonResult
            {
                ObjectType = SchemaObjectType.Table,
                Name = source.Name,
                Status = overallStatus,
                DiffScript = BuildDiffScript(subResults, overallStatus),
                SubResults = subResults
            };

            if (overallStatus != ComparisonStatus.Match && subResults.Any(x => x.Status != ComparisonStatus.Match))
            {
                EnhanceComparisonResultWithDiff(result, source.CreateScript, target.CreateScript);
            }

            resultBag.Add(result);
        });

        results.AddRange(resultBag.OrderBy(x => x.Name));

        if (IncludeMissingInSource)
        {
            var sourceTableSet = new HashSet<string>(sourceTables, StringComparer.OrdinalIgnoreCase);

            foreach (var targetTable in targetTables.Where(t => !sourceTableSet.Contains(t)))
            {
                var result = await HandleTableMissingInSourceAsync(targetProvider, targetTable);
                results.Add(result);
            }
        }
    }

    private async Task ComparePrimaryKeysAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var sourceIsMatView = await IsMaterializedViewAsync(sourceProvider, source.Name);
        var targetIsMatView = await IsMaterializedViewAsync(targetProvider, target.Name);

        if (sourceIsMatView || targetIsMatView)
        {
            return;
        }

        var sourcePk = source.PrimaryKeys.FirstOrDefault();
        var targetPk = target.PrimaryKeys.FirstOrDefault();

        var sourceValid = sourcePk != null && await IsValidPrimaryKeyAsync(sourceProvider, source.Name, sourcePk);
        var targetValid = targetPk != null && await IsValidPrimaryKeyAsync(targetProvider, target.Name, targetPk);

        if (sourceValid && !targetValid)
        {
            var script = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);

            subResults.Add(new(
                "PrimaryKeys",
                ComparisonStatus.MissingInTarget,
                $"Primary key is missing/invalid in target. Source columns: ({string.Join(", ", sourcePk!.Columns)})",
                script ?? string.Empty));

            return;
        }

        if (!sourceValid && targetValid)
        {
            if (!IncludeMissingInSource)
            {
                return;
            }

            subResults.Add(new(
                "PrimaryKeys",
                ComparisonStatus.MissingInSource,
                $"Primary key exists in target but not in source. Target columns: ({string.Join(", ", targetPk!.Columns)})",
                string.Empty));

            return;
        }

        if (sourceValid && targetValid && !sourcePk!.StructuralEquals(targetPk!))
        {
            var sourceScript = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
            var targetScript = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);

            subResults.Add(new(
                "PrimaryKeys",
                ComparisonStatus.Mismatch,
                $"Primary key structure differs. Source({string.Join(", ", sourcePk.Columns)}) vs Target({string.Join(", ", targetPk.Columns)})",
                $"-- SOURCE PK\n{sourceScript}\n\n-- TARGET PK\n{targetScript}"));
        }
    }

    private void CompareColumns(
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var sourceCols = source.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var targetCols = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceCol in sourceCols.Values)
        {
            if (!targetCols.TryGetValue(sourceCol.Name, out var targetCol))
            {
                subResults.Add(new(
                    "Columns",
                    ComparisonStatus.MissingInTarget,
                    $"Column '{sourceCol.Name}' is missing in target.",
                    $"ALTER TABLE \"{source.Name}\" ADD COLUMN \"{sourceCol.Name}\" {sourceCol.DataType} {(sourceCol.IsNullable ? string.Empty : "NOT NULL")};"));

                continue;
            }

            if (!sourceCol.Equals(targetCol))
            {
                subResults.Add(new(
                    "Columns",
                    ComparisonStatus.Mismatch,
                    BuildColumnDifferenceMessage(sourceCol, targetCol),
                    BuildColumnAlterScript(source.Name, sourceCol, targetCol)));
            }
        }

        if (!IncludeMissingInSource)
        {
            return;
        }

        foreach (var targetCol in targetCols.Values)
        {
            if (!sourceCols.ContainsKey(targetCol.Name))
            {
                subResults.Add(new(
                    "Columns",
                    ComparisonStatus.MissingInSource,
                    $"Column '{targetCol.Name}' exists in target but not in source.",
                    string.Empty));
            }
        }
    }

    private async Task CompareForeignKeysAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var sourceFksByStructure = source.ForeignKeys
            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetFksByStructure = target.ForeignKeys
            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFkKvp in sourceFksByStructure)
        {
            var structuralKey = sourceFkKvp.Key;
            var sourceFk = sourceFkKvp.Value;

            if (!targetFksByStructure.TryGetValue(structuralKey, out var targetFk))
            {
                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);

                subResults.Add(new(
                    "ForeignKeys",
                    ComparisonStatus.MissingInTarget,
                    $"Foreign key is missing in target. Structure: {structuralKey}",
                    script ?? string.Empty));

                continue;
            }

            if (!sourceFk.StructuralEquals(targetFk))
            {
                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);

                subResults.Add(new(
                    "ForeignKeys",
                    ComparisonStatus.Mismatch,
                    $"Foreign key structure differs. Structure: {structuralKey}",
                    script ?? string.Empty));
            }
        }

        if (!IncludeMissingInSource)
        {
            return;
        }

        foreach (var targetFkKvp in targetFksByStructure)
        {
            if (sourceFksByStructure.ContainsKey(targetFkKvp.Key))
            {
                continue;
            }

            var targetFk = targetFkKvp.Value;

            subResults.Add(new(
                "ForeignKeys",
                ComparisonStatus.MissingInSource,
                $"Foreign key exists in target but not in source. Structure: {targetFkKvp.Key}",
                $"-- Optional drop in target: ALTER TABLE \"{target.Name}\" DROP CONSTRAINT IF EXISTS \"{targetFk.Name}\";"));
        }
    }

    private async Task CompareIndexesAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var validSourceIndexes = new List<IndexDefinition>();

        foreach (var index in source.Indexes)
        {
            if (await IsValidIndexAsync(sourceProvider, index))
            {
                validSourceIndexes.Add(index);
            }
        }

        var validTargetIndexes = new List<IndexDefinition>();

        foreach (var index in target.Indexes)
        {
            if (await IsValidIndexAsync(targetProvider, index))
            {
                validTargetIndexes.Add(index);
            }
        }

        var sourceIndexesByStructure = validSourceIndexes
            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetIndexesByStructure = validTargetIndexes
            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var sourceIndexKvp in sourceIndexesByStructure)
        {
            var structuralKey = sourceIndexKvp.Key;
            var sourceIndex = sourceIndexKvp.Value;

            if (!targetIndexesByStructure.TryGetValue(structuralKey, out var targetIndex))
            {
                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);

                subResults.Add(new(
                    "Indexes",
                    ComparisonStatus.MissingInTarget,
                    $"Index is missing in target. Structure: {structuralKey}",
                    script ?? string.Empty));

                continue;
            }

            if (!sourceIndex.StructuralEquals(targetIndex))
            {
                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);

                subResults.Add(new(
                    "Indexes",
                    ComparisonStatus.Mismatch,
                    $"Index structure differs. Structure: {structuralKey}",
                    script ?? string.Empty));
            }
        }

        if (!IncludeMissingInSource)
        {
            return;
        }

        foreach (var targetIndexKvp in targetIndexesByStructure)
        {
            if (sourceIndexesByStructure.ContainsKey(targetIndexKvp.Key))
            {
                continue;
            }

            var targetIndex = targetIndexKvp.Value;

            subResults.Add(new(
                "Indexes",
                ComparisonStatus.MissingInSource,
                $"Index exists in target but not in source. Structure: {targetIndexKvp.Key}",
                $"-- Optional drop in target: DROP INDEX IF EXISTS \"{targetIndex.Name}\";"));
        }
    }

    private static Task CompareConstraintsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        // Keep this as no-op until unique/check constraints have separate models.
        // Do not use raw CREATE TABLE comparison here because that creates hidden fake mismatches.
        return Task.CompletedTask;
    }

    private string? BuildDiffScript(
        List<ComparisonSubResult> subResults,
        ComparisonStatus status)
    {
        if (status == ComparisonStatus.Match)
        {
            return null;
        }

        var sb = new StringBuilder();

        foreach (var sub in subResults.Where(s =>
                     s.Status != ComparisonStatus.Match &&
                     !string.IsNullOrWhiteSpace(s.CreateScript)))
        {
            sb.AppendLine($"-- {sub.Component}: {sub.Status}");
            sb.AppendLine($"-- {sub.Details}");
            sb.AppendLine(sub.CreateScript!.Trim());
            sb.AppendLine();
        }

        if (sb.Length == 0)
        {
            return null;
        }

        return sb.ToString().Trim();
    }

    private void EnhanceComparisonResultWithDiff(
        ComparisonResult result,
        string? sourceScript,
        string? targetScript)
    {
        result.SourceScript = sourceScript?.Trim();
        result.TargetScript = targetScript?.Trim();

        if (result.Status == ComparisonStatus.Match)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceScript) && string.IsNullOrWhiteSpace(targetScript))
        {
            return;
        }

        var diffResult = _sqlDiffService.ComputeDiff(sourceScript, targetScript);

        if (diffResult.HasDifferences)
        {
            result.SideBySideDiffHtml = _sqlDiffService.GenerateSideBySideHtml(diffResult);
        }
    }

    private async Task<ComparisonResult> HandleTableMissingInSourceAsync(
        IDatabaseSchemaProvider provider,
        string tableName)
    {
        var table = await provider.GetTableDefinitionAsync(tableName);

        var subResults = new List<ComparisonSubResult>();

        if (!string.IsNullOrWhiteSpace(table.CreateScript))
        {
            subResults.Add(new(
                "CreateScript",
                ComparisonStatus.MissingInSource,
                "Table exists in target but not in source.",
                table.CreateScript));
        }

        return new ComparisonResult
        {
            ObjectType = SchemaObjectType.Table,
            Name = table.Name,
            Status = ComparisonStatus.MissingInSource,
            Details = "Exists in target, missing in source",
            DiffScript = BuildDiffScript(subResults, ComparisonStatus.MissingInSource),
            SubResults = subResults
        };
    }

    private Task CompareFunctionsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbFunctionDefinition> sourceFunctions,
        Dictionary<string, DbFunctionDefinition> targetFunctionMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        progressLogger?.Invoke(0, 0, "⚙️ Comparing functions using already-fetched definitions...", false);

        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);

        var total = sourceFunctions.Count;
        var index = 0;

        foreach (var kvp in sourceFunctions)
        {
            index++;

            var signature = kvp.Key;
            var source = kvp.Value;

            if (ShouldLogProgress(index, total))
            {
                progressLogger?.Invoke(index, total, $"⚙️ Comparing function: {source.Name}", true);
            }

            if (!targetFunctionMap.TryGetValue(signature, out var target))
            {
                results.Add(CreateResult(
                    source.Name ?? signature,
                    SchemaObjectType.Function,
                    ComparisonStatus.MissingInTarget,
                    source.Definition ?? string.Empty));

                continue;
            }

            var sourceDef = source.Definition ?? string.Empty;
            var targetDef = target.Definition ?? string.Empty;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Function,
                    Name = source.Name ?? signature,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Function definition differs",
                    DiffScript = $"-- Function differs in target\n-- Signature: {signature}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };

                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
                results.Add(result);
            }
            else
            {
                results.Add(CreateResult(
                    source.Name ?? signature,
                    SchemaObjectType.Function,
                    ComparisonStatus.Match));
            }
        }

        if (!IncludeMissingInSource)
        {
            return Task.CompletedTask;
        }

        foreach (var kvp in targetFunctionMap)
        {
            if (sourceFunctions.ContainsKey(kvp.Key))
            {
                continue;
            }

            var target = kvp.Value;

            results.Add(CreateResult(
                target.Name ?? kvp.Key,
                SchemaObjectType.Function,
                ComparisonStatus.MissingInSource,
                target.Definition ?? string.Empty));
        }

        return Task.CompletedTask;
    }

    private Task CompareProceduresAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbFunctionDefinition> sourceProcedureMap,
        Dictionary<string, DbFunctionDefinition> targetProcedureMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        progressLogger?.Invoke(0, 0, "🛠 Comparing procedures using already-fetched definitions...", false);

        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);

        var total = sourceProcedureMap.Count;
        var index = 0;

        foreach (var kvp in sourceProcedureMap)
        {
            index++;

            var signatureKey = kvp.Key;
            var source = kvp.Value;

            if (ShouldLogProgress(index, total))
            {
                progressLogger?.Invoke(index, total, $"🛠 Comparing procedure: {source.Name}", true);
            }

            if (!targetProcedureMap.TryGetValue(signatureKey, out var target))
            {
                results.Add(CreateResult(
                    source.Name ?? signatureKey,
                    SchemaObjectType.Procedure,
                    ComparisonStatus.MissingInTarget,
                    source.Definition ?? string.Empty));

                continue;
            }

            var sourceDef = source.Definition ?? string.Empty;
            var targetDef = target.Definition ?? string.Empty;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Procedure,
                    Name = source.Name ?? signatureKey,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Procedure definition differs",
                    DiffScript = $"-- Procedure differs in target\n-- Signature: {signatureKey}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };

                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
                results.Add(result);
            }
            else
            {
                results.Add(CreateResult(
                    source.Name ?? signatureKey,
                    SchemaObjectType.Procedure,
                    ComparisonStatus.Match));
            }
        }

        if (!IncludeMissingInSource)
        {
            return Task.CompletedTask;
        }

        foreach (var kvp in targetProcedureMap)
        {
            if (sourceProcedureMap.ContainsKey(kvp.Key))
            {
                continue;
            }

            var target = kvp.Value;

            results.Add(CreateResult(
                target.Name ?? kvp.Key,
                SchemaObjectType.Procedure,
                ComparisonStatus.MissingInSource,
                target.Definition ?? string.Empty));
        }

        return Task.CompletedTask;
    }

    private Task CompareViewsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbViewDefinition> sourceViewMap,
        Dictionary<string, DbViewDefinition> targetViewMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        progressLogger?.Invoke(0, 0, "🔍 Comparing views using already-fetched definitions...", false);

        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);

        var total = sourceViewMap.Count;
        var index = 0;

        foreach (var kvp in sourceViewMap)
        {
            index++;

            var viewKey = kvp.Key;
            var source = kvp.Value;

            if (ShouldLogProgress(index, total))
            {
                progressLogger?.Invoke(index, total, $"🔍 Comparing view: {source.Name}", true);
            }

            if (!targetViewMap.TryGetValue(viewKey, out var target))
            {
                results.Add(CreateResult(
                    source.Name ?? viewKey,
                    SchemaObjectType.View,
                    ComparisonStatus.MissingInTarget,
                    source.Definition ?? string.Empty));

                continue;
            }

            var sourceDef = source.Definition ?? string.Empty;
            var targetDef = target.Definition ?? string.Empty;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.View,
                    Name = source.Name ?? viewKey,
                    Status = ComparisonStatus.Mismatch,
                    Details = "View definition differs",
                    DiffScript = $"-- View differs in target\n-- View: {viewKey}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };

                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
                results.Add(result);
            }
            else
            {
                results.Add(CreateResult(
                    source.Name ?? viewKey,
                    SchemaObjectType.View,
                    ComparisonStatus.Match));
            }
        }

        if (!IncludeMissingInSource)
        {
            return Task.CompletedTask;
        }

        foreach (var kvp in targetViewMap)
        {
            if (sourceViewMap.ContainsKey(kvp.Key))
            {
                continue;
            }

            var target = kvp.Value;

            results.Add(CreateResult(
                target.Name ?? kvp.Key,
                SchemaObjectType.View,
                ComparisonStatus.MissingInSource,
                target.Definition ?? string.Empty));
        }

        return Task.CompletedTask;
    }

    private Task CompareTriggersAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbTriggerDefinition> sourceTriggerMap,
        Dictionary<string, DbTriggerDefinition> targetTriggerMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        progressLogger?.Invoke(0, 0, "⏰ Comparing triggers using already-fetched definitions...", false);

        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);

        var total = sourceTriggerMap.Count;
        var index = 0;

        foreach (var kvp in sourceTriggerMap)
        {
            index++;

            var triggerKey = kvp.Key;
            var source = kvp.Value;

            if (ShouldLogProgress(index, total))
            {
                progressLogger?.Invoke(index, total, $"⏰ Comparing trigger: {source.Name}", true);
            }

            if (!targetTriggerMap.TryGetValue(triggerKey, out var target))
            {
                results.Add(CreateResult(
                    source.Name ?? triggerKey,
                    SchemaObjectType.Trigger,
                    ComparisonStatus.MissingInTarget,
                    source.Definition ?? string.Empty));

                continue;
            }

            var sourceDef = source.Definition ?? string.Empty;
            var targetDef = target.Definition ?? string.Empty;

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                var result = new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Trigger,
                    Name = source.Name ?? triggerKey,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Trigger definition differs",
                    DiffScript = $"-- Trigger differs in target\n-- Trigger: {triggerKey}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                };

                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
                results.Add(result);
            }
            else
            {
                results.Add(CreateResult(
                    source.Name ?? triggerKey,
                    SchemaObjectType.Trigger,
                    ComparisonStatus.Match));
            }
        }

        if (!IncludeMissingInSource)
        {
            return Task.CompletedTask;
        }

        foreach (var kvp in targetTriggerMap)
        {
            if (sourceTriggerMap.ContainsKey(kvp.Key))
            {
                continue;
            }

            var target = kvp.Value;

            results.Add(CreateResult(
                target.Name ?? kvp.Key,
                SchemaObjectType.Trigger,
                ComparisonStatus.MissingInSource,
                target.Definition ?? string.Empty));
        }

        return Task.CompletedTask;
    }

    private ComparisonResult CreateResult(
        string name,
        SchemaObjectType type,
        ComparisonStatus status,
        string diffScript = "")
    {
        var result = new ComparisonResult
        {
            ObjectType = type,
            Name = name,
            Status = status,
            DiffScript = string.IsNullOrWhiteSpace(diffScript) ? null : diffScript
        };

        if (type == SchemaObjectType.Function ||
            type == SchemaObjectType.Procedure ||
            type == SchemaObjectType.View ||
            type == SchemaObjectType.Trigger)
        {
            if (status == ComparisonStatus.MissingInTarget)
            {
                EnhanceComparisonResultWithDiff(result, diffScript, null);
            }
            else if (status == ComparisonStatus.MissingInSource)
            {
                EnhanceComparisonResultWithDiff(result, null, diffScript);
            }
        }

        return result;
    }

    private static ComparisonStatus GetOverallStatusFromSubResults(
        IReadOnlyCollection<ComparisonSubResult> subResults)
    {
        if (subResults.Any(x =>
                x.Status == ComparisonStatus.Mismatch ||
                x.Status == ComparisonStatus.MissingInTarget ||
                x.Status == ComparisonStatus.MissingInSource))
        {
            return ComparisonStatus.Mismatch;
        }

        return ComparisonStatus.Match;
    }

    private static bool AreScriptsEqual(
        string? sourceScript,
        string? targetScript,
        string sourceDbKind,
        string targetDbKind,
        ComparisonOptions options)
    {
        if (options.IgnoreOwnership)
        {
            var canonicalizedSource = DefinitionCanonicalizer.CanonicalizeDefinition(sourceScript, sourceDbKind, options);
            var canonicalizedTarget = DefinitionCanonicalizer.CanonicalizeDefinition(targetScript, targetDbKind, options);

            return string.Equals(
                canonicalizedSource,
                canonicalizedTarget,
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            sourceScript?.Trim(),
            targetScript?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldLogProgress(int current, int total)
    {
        if (current == 1 || current == total)
        {
            return true;
        }

        if (total <= 20)
        {
            return true;
        }

        return current % 10 == 0;
    }

    private static string GetRoutineKey(DbFunctionDefinition def)
    {
        return $"{def.Name}({def.Arguments})";
    }

    private static string GetViewKey(DbViewDefinition def)
    {
        return def.Name ?? string.Empty;
    }

    private static string GetTriggerKey(DbTriggerDefinition def)
    {
        return $"{def.Table}|{def.Name}";
    }

    private static Dictionary<string, DbFunctionDefinition> BuildRoutineMap(
        IList<DbFunctionDefinition> routines)
    {
        return routines
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(GetRoutineKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, DbViewDefinition> BuildViewMap(
        IList<DbViewDefinition> views)
    {
        return views
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(GetViewKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, DbTriggerDefinition> BuildTriggerMap(
        IList<DbTriggerDefinition> triggers)
    {
        return triggers
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(GetTriggerKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, T> LimitMap<T>(
        Dictionary<string, T> map,
        int limit)
    {
        return map
            .Take(limit)
            .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPostgresProvider(IDatabaseSchemaProvider provider)
    {
        return provider.GetType().Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMySqlProvider(IDatabaseSchemaProvider provider)
    {
        return provider.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
               provider.GetType().Name.Contains("MySQL", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDbKind(IDatabaseSchemaProvider provider)
    {
        if (IsPostgresProvider(provider))
        {
            return "postgres";
        }

        if (IsMySqlProvider(provider))
        {
            return "mysql";
        }

        return "unknown";
    }

    private static Task<bool> IsValidPrimaryKeyAsync(
        IDatabaseSchemaProvider provider,
        string tableName,
        PrimaryKeyDefinition pk)
    {
        return Task.FromResult(pk.Columns.Any());
    }

    private static Task<bool> IsValidIndexAsync(
        IDatabaseSchemaProvider provider,
        IndexDefinition index)
    {
        return Task.FromResult(index.Columns.Any());
    }

    private static Task<bool> IsMaterializedViewAsync(
        IDatabaseSchemaProvider provider,
        string tableName)
    {
        if (!IsPostgresProvider(provider))
        {
            return Task.FromResult(false);
        }

        var lower = tableName.ToLowerInvariant();

        return Task.FromResult(
            lower.Contains("_mv") ||
            lower.Contains("matview"));
    }

    private static string BuildColumnDifferenceMessage(
        ColumnDefinition sourceCol,
        ColumnDefinition targetCol)
    {
        var differences = new List<string>();

        if (!string.Equals(sourceCol.DataType, targetCol.DataType, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"type source='{sourceCol.DataType}' target='{targetCol.DataType}'");
        }

        if (sourceCol.IsNullable != targetCol.IsNullable)
        {
            differences.Add($"nullable source='{sourceCol.IsNullable}' target='{targetCol.IsNullable}'");
        }

        var sourceDefault = GetColumnPropertyValue(sourceCol, "DefaultValue", "Default", "ColumnDefault");
        var targetDefault = GetColumnPropertyValue(targetCol, "DefaultValue", "Default", "ColumnDefault");

        if (!string.Equals(
                NormalizeDefault(sourceDefault),
                NormalizeDefault(targetDefault),
                StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"default source='{sourceDefault}' target='{targetDefault}'");
        }

        var sourceMaxLength = GetColumnPropertyValue(sourceCol, "MaxLength", "Length", "CharacterMaximumLength");
        var targetMaxLength = GetColumnPropertyValue(targetCol, "MaxLength", "Length", "CharacterMaximumLength");

        if (!string.Equals(sourceMaxLength, targetMaxLength, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"length source='{sourceMaxLength}' target='{targetMaxLength}'");
        }

        var sourceIdentity = GetColumnPropertyValue(sourceCol, "IsIdentity", "Identity");
        var targetIdentity = GetColumnPropertyValue(targetCol, "IsIdentity", "Identity");

        if (!string.Equals(sourceIdentity, targetIdentity, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"identity source='{sourceIdentity}' target='{targetIdentity}'");
        }

        if (differences.Count == 0)
        {
            differences.Add("column metadata differs");
        }

        return $"Column '{sourceCol.Name}' differs: {string.Join("; ", differences)}.";
    }

    private static string BuildColumnAlterScript(
        string tableName,
        ColumnDefinition sourceCol,
        ColumnDefinition targetCol)
    {
        var sb = new StringBuilder();

        if (!string.Equals(sourceCol.DataType, targetCol.DataType, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" TYPE {sourceCol.DataType};");
        }

        if (sourceCol.IsNullable != targetCol.IsNullable)
        {
            if (sourceCol.IsNullable)
            {
                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" DROP NOT NULL;");
            }
            else
            {
                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" SET NOT NULL;");
            }
        }

        var sourceDefault = GetColumnPropertyValue(sourceCol, "DefaultValue", "Default", "ColumnDefault");
        var targetDefault = GetColumnPropertyValue(targetCol, "DefaultValue", "Default", "ColumnDefault");

        if (!string.Equals(
                NormalizeDefault(sourceDefault),
                NormalizeDefault(targetDefault),
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(sourceDefault))
            {
                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" DROP DEFAULT;");
            }
            else
            {
                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" SET DEFAULT {sourceDefault};");
            }
        }

        return sb.ToString().Trim();
    }

    private static string GetColumnPropertyValue(
        ColumnDefinition column,
        params string[] propertyNames)
    {
        var type = column.GetType();

        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property == null)
            {
                continue;
            }

            var value = property.GetValue(column);

            if (value == null)
            {
                return string.Empty;
            }

            return value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string NormalizeDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("::text", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
// again 
//public class SchemaComparer : ISchemaComparer
//{
//    private const int TableMaxDegreeOfParallelism = 4;

//    /// <summary>
//    /// Keep false for QA -> PROD report.
//    /// false = show only what is missing/different in PROD.
//    /// true  = also show what exists in PROD but not in QA.
//    /// </summary>
//    private const bool IncludeMissingInSource = false;

//    private readonly SqlDiffService _sqlDiffService;

//    public SchemaComparer()
//    {
//        _sqlDiffService = new SqlDiffService();
//    }

//    public async Task<IList<ComparisonResult>> CompareAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Action<int, int, string, bool>? progressLogger = null,
//        bool runForTest = false,
//        int testObjectLimit = 10,
//        ComparisonOptions? options = null)
//    {
//        options ??= new ComparisonOptions();

//        progressLogger?.Invoke(0, 0, "🔍 Fetching schema objects...", false);

//        var sourceTablesTask = sourceProvider.GetTablesAsync();
//        var targetTablesTask = targetProvider.GetTablesAsync();

//        var sourceFunctionsTask = sourceProvider.GetFunctionsAsync();
//        var targetFunctionsTask = targetProvider.GetFunctionsAsync();

//        var sourceProceduresTask = sourceProvider.GetProceduresAsync();
//        var targetProceduresTask = targetProvider.GetProceduresAsync();

//        var sourceViewsTask = sourceProvider.GetViewsAsync();
//        var targetViewsTask = targetProvider.GetViewsAsync();

//        var sourceTriggersTask = sourceProvider.GetTriggersAsync();
//        var targetTriggersTask = targetProvider.GetTriggersAsync();

//        await Task.WhenAll(
//            sourceTablesTask,
//            targetTablesTask,
//            sourceFunctionsTask,
//            targetFunctionsTask,
//            sourceProceduresTask,
//            targetProceduresTask,
//            sourceViewsTask,
//            targetViewsTask,
//            sourceTriggersTask);

//        var sourceTables = await sourceTablesTask;
//        var targetTables = await targetTablesTask;

//        var sourceFunctions = await sourceFunctionsTask;
//        var targetFunctions = await targetFunctionsTask;

//        var sourceProcedures = await sourceProceduresTask;
//        var targetProcedures = await targetProceduresTask;

//        var sourceViews = await sourceViewsTask;
//        var targetViews = await targetViewsTask;

//        var sourceTriggers = await sourceTriggersTask;
//        var targetTriggers = await targetTriggersTask;

//        progressLogger?.Invoke(
//            0,
//            0,
//            $"📦 Fetched objects | Source: Tables={sourceTables.Count}, Functions={sourceFunctions.Count}, Procedures={sourceProcedures.Count}, Views={sourceViews.Count}, Triggers={sourceTriggers.Count} | " +
//            $"Target: Tables={targetTables.Count}, Functions={targetFunctions.Count}, Procedures={targetProcedures.Count}, Views={targetViews.Count}, Triggers={targetTriggers.Count}",
//            false);

//        var limitedSourceTables = runForTest
//            ? sourceTables.Take(testObjectLimit).ToList()
//            : sourceTables;

//        var limitedTargetTables = targetTables;

//        var sourceFunctionMap = BuildRoutineMap(sourceFunctions);
//        var targetFunctionMap = BuildRoutineMap(targetFunctions);

//        var sourceProcedureMap = BuildRoutineMap(sourceProcedures);
//        var targetProcedureMap = BuildRoutineMap(targetProcedures);

//        var sourceViewMap = BuildViewMap(sourceViews);
//        var targetViewMap = BuildViewMap(targetViews);

//        var sourceTriggerMap = BuildTriggerMap(sourceTriggers);
//        var targetTriggerMap = BuildTriggerMap(targetTriggers);

//        if (runForTest)
//        {
//            sourceFunctionMap = LimitMap(sourceFunctionMap, testObjectLimit);
//            sourceProcedureMap = LimitMap(sourceProcedureMap, testObjectLimit);
//            sourceViewMap = LimitMap(sourceViewMap, testObjectLimit);
//            sourceTriggerMap = LimitMap(sourceTriggerMap, testObjectLimit);
//        }

//        var tableResults = new List<ComparisonResult>();
//        var functionResults = new List<ComparisonResult>();
//        var procedureResults = new List<ComparisonResult>();
//        var viewResults = new List<ComparisonResult>();
//        var triggerResults = new List<ComparisonResult>();

//        progressLogger?.Invoke(
//            0,
//            0,
//            "🚀 Comparing tables, functions, procedures, views and triggers in parallel...",
//            false);

//        await Task.WhenAll(
//            CompareTablesAsync(
//                sourceProvider,
//                targetProvider,
//                limitedSourceTables,
//                limitedTargetTables,
//                tableResults,
//                progressLogger,
//                options),

//            CompareFunctionsAsync(
//                sourceProvider,
//                targetProvider,
//                sourceFunctionMap,
//                targetFunctionMap,
//                functionResults,
//                progressLogger,
//                options),

//            CompareProceduresAsync(
//                sourceProvider,
//                targetProvider,
//                sourceProcedureMap,
//                targetProcedureMap,
//                procedureResults,
//                progressLogger,
//                options),

//            CompareViewsAsync(
//                sourceProvider,
//                targetProvider,
//                sourceViewMap,
//                targetViewMap,
//                viewResults,
//                progressLogger,
//                options),

//            CompareTriggersAsync(
//                sourceProvider,
//                targetProvider,
//                sourceTriggerMap,
//                targetTriggerMap,
//                triggerResults,
//                progressLogger,
//                options));

//        var results = new List<ComparisonResult>();

//        results.AddRange(tableResults.OrderBy(x => x.ObjectType).ThenBy(x => x.Name));
//        results.AddRange(functionResults.OrderBy(x => x.Name));
//        results.AddRange(procedureResults.OrderBy(x => x.Name));
//        results.AddRange(viewResults.OrderBy(x => x.Name));
//        results.AddRange(triggerResults.OrderBy(x => x.Name));

//        progressLogger?.Invoke(
//            0,
//            0,
//            $"✅ Schema comparison completed. Total={results.Count}, Match={results.Count(x => x.Status == ComparisonStatus.Match)}, Mismatch={results.Count(x => x.Status == ComparisonStatus.Mismatch)}, MissingInTarget={results.Count(x => x.Status == ComparisonStatus.MissingInTarget)}, MissingInSource={results.Count(x => x.Status == ComparisonStatus.MissingInSource)}",
//            false);

//        return results;
//    }

//    private async Task CompareTablesAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        IList<string> sourceTables,
//        IList<string> targetTables,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(
//            0,
//            0,
//            $"📄 Comparing tables with parallelism={TableMaxDegreeOfParallelism}...",
//            false);

//        var targetTableSet = new HashSet<string>(targetTables, StringComparer.OrdinalIgnoreCase);
//        var resultBag = new ConcurrentBag<ComparisonResult>();

//        var completed = 0;
//        var total = sourceTables.Count;

//        var parallelOptions = new ParallelOptions
//        {
//            MaxDegreeOfParallelism = TableMaxDegreeOfParallelism
//        };

//        await Parallel.ForEachAsync(sourceTables, parallelOptions, async (tableName, cancellationToken) =>
//        {
//            var current = Interlocked.Increment(ref completed);

//            if (ShouldLogProgress(current, total))
//            {
//                progressLogger?.Invoke(current, total, $"🔄 Comparing table: {tableName}", true);
//            }

//            var source = await sourceProvider.GetTableDefinitionAsync(tableName);

//            var target = targetTableSet.Contains(tableName)
//                ? await targetProvider.GetTableDefinitionAsync(tableName)
//                : null;

//            if (target == null)
//            {
//                resultBag.Add(CreateResult(
//                    tableName,
//                    SchemaObjectType.Table,
//                    ComparisonStatus.MissingInTarget,
//                    $"Table '{tableName}' exists in source but is missing in target."));

//                return;
//            }

//            var subResults = new List<ComparisonSubResult>();

//            var primaryKeyStatus = await ComparePrimaryKeysAsync(sourceProvider, targetProvider, source, target, subResults);
//            var columnStatus = CompareColumns(source, target, subResults);
//            var foreignKeyStatus = await CompareForeignKeysAsync(sourceProvider, targetProvider, source, target, subResults);
//            var indexStatus = await CompareIndexesAsync(sourceProvider, targetProvider, source, target, subResults);
//            var constraintStatus = await CompareConstraintsAsync(sourceProvider, targetProvider, source, target, subResults);

//            var overallStatus = CombineStatuses(
//                primaryKeyStatus,
//                columnStatus,
//                foreignKeyStatus,
//                indexStatus,
//                constraintStatus);

//            // IMPORTANT:
//            // Do NOT use full StructuralEquals() to set mismatch for QA -> PROD mode.
//            // StructuralEquals is two-way and detects extra objects in PROD.
//            // That caused rows like company_upis to show Mismatch while all component buttons were green.
//            if (IncludeMissingInSource)
//            {
//                var fullStructuralStatus = CompareFullStructureIfRequired(source, target, subResults, options, sourceProvider, targetProvider);
//                overallStatus = CombineStatuses(overallStatus, fullStructuralStatus);
//            }

//            var result = new ComparisonResult
//            {
//                ObjectType = SchemaObjectType.Table,
//                Name = source.Name,
//                Status = overallStatus,
//                DiffScript = BuildDiffScript(subResults, overallStatus),
//                SubResults = subResults
//            };

//            // Only generate side-by-side table diff when there is a real visible mismatch.
//            if (overallStatus != ComparisonStatus.Match && subResults.Any(x => x.Status != ComparisonStatus.Match))
//            {
//                EnhanceComparisonResultWithDiff(result, source.CreateScript, target.CreateScript);
//            }

//            resultBag.Add(result);
//        });

//        results.AddRange(resultBag.OrderBy(x => x.Name));

//        if (IncludeMissingInSource)
//        {
//            var sourceTableSet = new HashSet<string>(sourceTables, StringComparer.OrdinalIgnoreCase);

//            foreach (var targetTable in targetTables.Where(t => !sourceTableSet.Contains(t)))
//            {
//                var result = await HandleTableMissingInSourceAsync(targetProvider, targetTable);
//                results.Add(result);
//            }
//        }
//    }

//    private ComparisonStatus CompareFullStructureIfRequired(
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults,
//        ComparisonOptions options,
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider)
//    {
//        if (source.StructuralEquals(target))
//        {
//            return ComparisonStatus.Match;
//        }

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        if (AreScriptsEqual(source.CreateScript, target.CreateScript, sourceDbKind, targetDbKind, options))
//        {
//            return ComparisonStatus.Match;
//        }

//        subResults.Add(new ComparisonSubResult(
//            "StructuralDiff",
//            ComparisonStatus.Mismatch,
//            "Full source/target structure differs. This includes extra objects in target because IncludeMissingInSource is enabled.",
//            $"-- SOURCE SIGNATURE\n{source.GetStructuralSignature()}\n\n-- TARGET SIGNATURE\n{target.GetStructuralSignature()}\n\n-- SOURCE SCRIPT\n{source.CreateScript}\n\n-- TARGET SCRIPT\n{target.CreateScript}"
//        ));

//        return ComparisonStatus.Mismatch;
//    }

//    private async Task<ComparisonStatus> ComparePrimaryKeysAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var sourceIsMatView = await IsMaterializedViewAsync(sourceProvider, source.Name);
//        var targetIsMatView = await IsMaterializedViewAsync(targetProvider, target.Name);

//        if (sourceIsMatView || targetIsMatView)
//        {
//            return ComparisonStatus.Match;
//        }

//        var sourcePk = source.PrimaryKeys.FirstOrDefault();
//        var targetPk = target.PrimaryKeys.FirstOrDefault();

//        var sourceValid = sourcePk != null && await IsValidPrimaryKeyAsync(sourceProvider, source.Name, sourcePk);
//        var targetValid = targetPk != null && await IsValidPrimaryKeyAsync(targetProvider, target.Name, targetPk);

//        if (sourceValid && !targetValid)
//        {
//            var script = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);

//            subResults.Add(new(
//                "PrimaryKeys",
//                ComparisonStatus.MissingInTarget,
//                $"Primary key is missing/invalid in target. Source columns: ({string.Join(", ", sourcePk!.Columns)})",
//                script ?? string.Empty));

//            return ComparisonStatus.Mismatch;
//        }

//        if (!sourceValid && targetValid)
//        {
//            if (IncludeMissingInSource)
//            {
//                subResults.Add(new(
//                    "PrimaryKeys",
//                    ComparisonStatus.MissingInSource,
//                    $"Primary key exists in target but not in source. Target columns: ({string.Join(", ", targetPk!.Columns)})",
//                    string.Empty));

//                return ComparisonStatus.Mismatch;
//            }

//            return ComparisonStatus.Match;
//        }

//        if (sourceValid && targetValid && !sourcePk!.StructuralEquals(targetPk!))
//        {
//            var sourceScript = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
//            var targetScript = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);

//            subResults.Add(new(
//                "PrimaryKeys",
//                ComparisonStatus.Mismatch,
//                $"Primary key structure differs. Source({string.Join(", ", sourcePk.Columns)}) vs Target({string.Join(", ", targetPk.Columns)})",
//                $"-- SOURCE PK\n{sourceScript}\n\n-- TARGET PK\n{targetScript}"));

//            return ComparisonStatus.Mismatch;
//        }

//        return ComparisonStatus.Match;
//    }

//    private ComparisonStatus CompareColumns(
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        var sourceCols = source.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
//        var targetCols = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

//        foreach (var sourceCol in sourceCols.Values)
//        {
//            if (!targetCols.TryGetValue(sourceCol.Name, out var targetCol))
//            {
//                status = ComparisonStatus.Mismatch;

//                subResults.Add(new(
//                    "Columns",
//                    ComparisonStatus.MissingInTarget,
//                    $"Column '{sourceCol.Name}' is missing in target.",
//                    $"ALTER TABLE \"{source.Name}\" ADD COLUMN \"{sourceCol.Name}\" {sourceCol.DataType} {(sourceCol.IsNullable ? string.Empty : "NOT NULL")};"));

//                continue;
//            }

//            if (!sourceCol.Equals(targetCol))
//            {
//                status = ComparisonStatus.Mismatch;

//                subResults.Add(new(
//                    "Columns",
//                    ComparisonStatus.Mismatch,
//                    BuildColumnDifferenceMessage(sourceCol, targetCol),
//                    BuildColumnAlterScript(source.Name, sourceCol, targetCol)));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var targetCol in targetCols.Values)
//            {
//                if (!sourceCols.ContainsKey(targetCol.Name))
//                {
//                    status = ComparisonStatus.Mismatch;

//                    subResults.Add(new(
//                        "Columns",
//                        ComparisonStatus.MissingInSource,
//                        $"Column '{targetCol.Name}' exists in target but not in source.",
//                        string.Empty));
//                }
//            }
//        }

//        return status;
//    }

//    private async Task<ComparisonStatus> CompareForeignKeysAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        var sourceFksByStructure = source.ForeignKeys
//            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetFksByStructure = target.ForeignKeys
//            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        foreach (var sourceFkKvp in sourceFksByStructure)
//        {
//            var structuralKey = sourceFkKvp.Key;
//            var sourceFk = sourceFkKvp.Value;

//            if (!targetFksByStructure.TryGetValue(structuralKey, out var targetFk))
//            {
//                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);

//                subResults.Add(new(
//                    "ForeignKeys",
//                    ComparisonStatus.MissingInTarget,
//                    $"Foreign key is missing in target. Structure: {structuralKey}",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;

//                continue;
//            }

//            if (!sourceFk.StructuralEquals(targetFk))
//            {
//                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);

//                subResults.Add(new(
//                    "ForeignKeys",
//                    ComparisonStatus.Mismatch,
//                    $"Foreign key structure differs. Structure: {structuralKey}",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var targetFkKvp in targetFksByStructure)
//            {
//                if (sourceFksByStructure.ContainsKey(targetFkKvp.Key))
//                {
//                    continue;
//                }

//                var targetFk = targetFkKvp.Value;

//                subResults.Add(new(
//                    "ForeignKeys",
//                    ComparisonStatus.MissingInSource,
//                    $"Foreign key exists in target but not in source. Structure: {targetFkKvp.Key}",
//                    $"-- Optional drop in target: ALTER TABLE \"{target.Name}\" DROP CONSTRAINT IF EXISTS \"{targetFk.Name}\";"));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        return status;
//    }

//    private async Task<ComparisonStatus> CompareIndexesAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        var validSourceIndexes = new List<IndexDefinition>();

//        foreach (var index in source.Indexes)
//        {
//            if (await IsValidIndexAsync(sourceProvider, index))
//            {
//                validSourceIndexes.Add(index);
//            }
//        }

//        var validTargetIndexes = new List<IndexDefinition>();

//        foreach (var index in target.Indexes)
//        {
//            if (await IsValidIndexAsync(targetProvider, index))
//            {
//                validTargetIndexes.Add(index);
//            }
//        }

//        var sourceIndexesByStructure = validSourceIndexes
//            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetIndexesByStructure = validTargetIndexes
//            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        foreach (var sourceIndexKvp in sourceIndexesByStructure)
//        {
//            var structuralKey = sourceIndexKvp.Key;
//            var sourceIndex = sourceIndexKvp.Value;

//            if (!targetIndexesByStructure.TryGetValue(structuralKey, out var targetIndex))
//            {
//                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);

//                subResults.Add(new(
//                    "Indexes",
//                    ComparisonStatus.MissingInTarget,
//                    $"Index is missing in target. Structure: {structuralKey}",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;

//                continue;
//            }

//            if (!sourceIndex.StructuralEquals(targetIndex))
//            {
//                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);

//                subResults.Add(new(
//                    "Indexes",
//                    ComparisonStatus.Mismatch,
//                    $"Index structure differs. Structure: {structuralKey}",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var targetIndexKvp in targetIndexesByStructure)
//            {
//                if (sourceIndexesByStructure.ContainsKey(targetIndexKvp.Key))
//                {
//                    continue;
//                }

//                var targetIndex = targetIndexKvp.Value;

//                subResults.Add(new(
//                    "Indexes",
//                    ComparisonStatus.MissingInSource,
//                    $"Index exists in target but not in source. Structure: {targetIndexKvp.Key}",
//                    $"-- Optional drop in target: DROP INDEX IF EXISTS \"{targetIndex.Name}\";"));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        return status;
//    }

//    private static Task<ComparisonStatus> CompareConstraintsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        // Keep this as Match until separate Unique/Check constraint models exist.
//        // Do not infer constraint mismatch from full CREATE TABLE script here.
//        return Task.FromResult(ComparisonStatus.Match);
//    }

//    private string? BuildDiffScript(
//        List<ComparisonSubResult> subResults,
//        ComparisonStatus status)
//    {
//        if (status == ComparisonStatus.Match)
//        {
//            return null;
//        }

//        var sb = new StringBuilder();

//        foreach (var sub in subResults.Where(s =>
//                     s.Status != ComparisonStatus.Match &&
//                     !string.IsNullOrWhiteSpace(s.CreateScript)))
//        {
//            sb.AppendLine($"-- {sub.Component}: {sub.Status}");
//            sb.AppendLine($"-- {sub.Details}");
//            sb.AppendLine(sub.CreateScript!.Trim());
//            sb.AppendLine();
//        }

//        if (sb.Length == 0)
//        {
//            return null;
//        }

//        return sb.ToString().Trim();
//    }

//    private void EnhanceComparisonResultWithDiff(
//        ComparisonResult result,
//        string? sourceScript,
//        string? targetScript)
//    {
//        result.SourceScript = sourceScript?.Trim();
//        result.TargetScript = targetScript?.Trim();

//        if (result.Status == ComparisonStatus.Match)
//        {
//            return;
//        }

//        if (string.IsNullOrWhiteSpace(sourceScript) && string.IsNullOrWhiteSpace(targetScript))
//        {
//            return;
//        }

//        var diffResult = _sqlDiffService.ComputeDiff(sourceScript, targetScript);

//        if (diffResult.HasDifferences)
//        {
//            result.SideBySideDiffHtml = _sqlDiffService.GenerateSideBySideHtml(diffResult);
//        }
//    }

//    private async Task<ComparisonResult> HandleTableMissingInSourceAsync(
//        IDatabaseSchemaProvider provider,
//        string tableName)
//    {
//        var table = await provider.GetTableDefinitionAsync(tableName);
//        var subResults = new List<ComparisonSubResult>();

//        if (!string.IsNullOrWhiteSpace(table.CreateScript))
//        {
//            subResults.Add(new(
//                "CreateScript",
//                ComparisonStatus.MissingInSource,
//                "Table exists in target but not in source.",
//                table.CreateScript));
//        }

//        return new ComparisonResult
//        {
//            ObjectType = SchemaObjectType.Table,
//            Name = table.Name,
//            Status = ComparisonStatus.MissingInSource,
//            Details = "Exists in target, missing in source",
//            DiffScript = BuildDiffScript(subResults, ComparisonStatus.MissingInSource),
//            SubResults = subResults
//        };
//    }

//    private Task CompareFunctionsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbFunctionDefinition> sourceFunctions,
//        Dictionary<string, DbFunctionDefinition> targetFunctionMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "⚙️ Comparing functions using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        var total = sourceFunctions.Count;
//        var index = 0;

//        foreach (var kvp in sourceFunctions)
//        {
//            index++;

//            var signature = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"⚙️ Comparing function: {source.Name}", true);
//            }

//            if (!targetFunctionMap.TryGetValue(signature, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? signature,
//                    SchemaObjectType.Function,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Function,
//                    Name = source.Name ?? signature,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Function definition differs",
//                    DiffScript = $"-- Function differs in target\n-- Signature: {signature}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(
//                    source.Name ?? signature,
//                    SchemaObjectType.Function,
//                    ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetFunctionMap)
//            {
//                if (sourceFunctions.ContainsKey(kvp.Key))
//                {
//                    continue;
//                }

//                var target = kvp.Value;

//                results.Add(CreateResult(
//                    target.Name ?? kvp.Key,
//                    SchemaObjectType.Function,
//                    ComparisonStatus.MissingInSource,
//                    target.Definition ?? string.Empty));
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private Task CompareProceduresAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbFunctionDefinition> sourceProcedureMap,
//        Dictionary<string, DbFunctionDefinition> targetProcedureMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "🛠 Comparing procedures using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        var total = sourceProcedureMap.Count;
//        var index = 0;

//        foreach (var kvp in sourceProcedureMap)
//        {
//            index++;

//            var signatureKey = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"🛠 Comparing procedure: {source.Name}", true);
//            }

//            if (!targetProcedureMap.TryGetValue(signatureKey, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? signatureKey,
//                    SchemaObjectType.Procedure,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Procedure,
//                    Name = source.Name ?? signatureKey,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Procedure definition differs",
//                    DiffScript = $"-- Procedure differs in target\n-- Signature: {signatureKey}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(
//                    source.Name ?? signatureKey,
//                    SchemaObjectType.Procedure,
//                    ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetProcedureMap)
//            {
//                if (sourceProcedureMap.ContainsKey(kvp.Key))
//                {
//                    continue;
//                }

//                var target = kvp.Value;

//                results.Add(CreateResult(
//                    target.Name ?? kvp.Key,
//                    SchemaObjectType.Procedure,
//                    ComparisonStatus.MissingInSource,
//                    target.Definition ?? string.Empty));
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private Task CompareViewsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbViewDefinition> sourceViewMap,
//        Dictionary<string, DbViewDefinition> targetViewMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "🔍 Comparing views using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        var total = sourceViewMap.Count;
//        var index = 0;

//        foreach (var kvp in sourceViewMap)
//        {
//            index++;

//            var viewKey = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"🔍 Comparing view: {source.Name}", true);
//            }

//            if (!targetViewMap.TryGetValue(viewKey, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? viewKey,
//                    SchemaObjectType.View,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.View,
//                    Name = source.Name ?? viewKey,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "View definition differs",
//                    DiffScript = $"-- View differs in target\n-- View: {viewKey}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(
//                    source.Name ?? viewKey,
//                    SchemaObjectType.View,
//                    ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetViewMap)
//            {
//                if (sourceViewMap.ContainsKey(kvp.Key))
//                {
//                    continue;
//                }

//                var target = kvp.Value;

//                results.Add(CreateResult(
//                    target.Name ?? kvp.Key,
//                    SchemaObjectType.View,
//                    ComparisonStatus.MissingInSource,
//                    target.Definition ?? string.Empty));
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private Task CompareTriggersAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbTriggerDefinition> sourceTriggerMap,
//        Dictionary<string, DbTriggerDefinition> targetTriggerMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "⏰ Comparing triggers using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        var total = sourceTriggerMap.Count;
//        var index = 0;

//        foreach (var kvp in sourceTriggerMap)
//        {
//            index++;

//            var triggerKey = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"⏰ Comparing trigger: {source.Name}", true);
//            }

//            if (!targetTriggerMap.TryGetValue(triggerKey, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? triggerKey,
//                    SchemaObjectType.Trigger,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Trigger,
//                    Name = source.Name ?? triggerKey,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Trigger definition differs",
//                    DiffScript = $"-- Trigger differs in target\n-- Trigger: {triggerKey}\n\n-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(
//                    source.Name ?? triggerKey,
//                    SchemaObjectType.Trigger,
//                    ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetTriggerMap)
//            {
//                if (sourceTriggerMap.ContainsKey(kvp.Key))
//                {
//                    continue;
//                }

//                var target = kvp.Value;

//                results.Add(CreateResult(
//                    target.Name ?? kvp.Key,
//                    SchemaObjectType.Trigger,
//                    ComparisonStatus.MissingInSource,
//                    target.Definition ?? string.Empty));
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private ComparisonResult CreateResult(
//        string name,
//        SchemaObjectType type,
//        ComparisonStatus status,
//        string diffScript = "")
//    {
//        var result = new ComparisonResult
//        {
//            ObjectType = type,
//            Name = name,
//            Status = status,
//            DiffScript = string.IsNullOrWhiteSpace(diffScript) ? null : diffScript
//        };

//        if (type == SchemaObjectType.Function ||
//            type == SchemaObjectType.Procedure ||
//            type == SchemaObjectType.View ||
//            type == SchemaObjectType.Trigger)
//        {
//            if (status == ComparisonStatus.MissingInTarget)
//            {
//                EnhanceComparisonResultWithDiff(result, diffScript, null);
//            }
//            else if (status == ComparisonStatus.MissingInSource)
//            {
//                EnhanceComparisonResultWithDiff(result, null, diffScript);
//            }
//        }

//        return result;
//    }

//    private static ComparisonStatus CombineStatuses(params ComparisonStatus[] statuses)
//    {
//        if (statuses.Any(x =>
//                x == ComparisonStatus.Mismatch ||
//                x == ComparisonStatus.MissingInTarget ||
//                x == ComparisonStatus.MissingInSource))
//        {
//            return ComparisonStatus.Mismatch;
//        }

//        return ComparisonStatus.Match;
//    }

//    private static bool AreScriptsEqual(
//        string? sourceScript,
//        string? targetScript,
//        string sourceDbKind,
//        string targetDbKind,
//        ComparisonOptions options)
//    {
//        if (options.IgnoreOwnership)
//        {
//            var canonicalizedSource = DefinitionCanonicalizer.CanonicalizeDefinition(sourceScript, sourceDbKind, options);
//            var canonicalizedTarget = DefinitionCanonicalizer.CanonicalizeDefinition(targetScript, targetDbKind, options);

//            return string.Equals(
//                canonicalizedSource,
//                canonicalizedTarget,
//                StringComparison.OrdinalIgnoreCase);
//        }

//        return string.Equals(
//            sourceScript?.Trim(),
//            targetScript?.Trim(),
//            StringComparison.OrdinalIgnoreCase);
//    }

//    private static bool ShouldLogProgress(int current, int total)
//    {
//        if (current == 1 || current == total)
//        {
//            return true;
//        }

//        if (total <= 20)
//        {
//            return true;
//        }

//        return current % 10 == 0;
//    }

//    private static string GetRoutineKey(DbFunctionDefinition def)
//    {
//        return $"{def.Name}({def.Arguments})";
//    }

//    private static string GetViewKey(DbViewDefinition def)
//    {
//        return def.Name ?? string.Empty;
//    }

//    private static string GetTriggerKey(DbTriggerDefinition def)
//    {
//        return $"{def.Table}|{def.Name}";
//    }

//    private static Dictionary<string, DbFunctionDefinition> BuildRoutineMap(
//        IList<DbFunctionDefinition> routines)
//    {
//        return routines
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetRoutineKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
//    }

//    private static Dictionary<string, DbViewDefinition> BuildViewMap(
//        IList<DbViewDefinition> views)
//    {
//        return views
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetViewKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
//    }

//    private static Dictionary<string, DbTriggerDefinition> BuildTriggerMap(
//        IList<DbTriggerDefinition> triggers)
//    {
//        return triggers
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetTriggerKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
//    }

//    private static Dictionary<string, T> LimitMap<T>(
//        Dictionary<string, T> map,
//        int limit)
//    {
//        return map
//            .Take(limit)
//            .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
//    }

//    private static bool IsPostgresProvider(IDatabaseSchemaProvider provider)
//    {
//        return provider.GetType().Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase);
//    }

//    private static bool IsMySqlProvider(IDatabaseSchemaProvider provider)
//    {
//        return provider.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
//               provider.GetType().Name.Contains("MySQL", StringComparison.OrdinalIgnoreCase);
//    }

//    private static string GetDbKind(IDatabaseSchemaProvider provider)
//    {
//        if (IsPostgresProvider(provider))
//        {
//            return "postgres";
//        }

//        if (IsMySqlProvider(provider))
//        {
//            return "mysql";
//        }

//        return "unknown";
//    }

//    private static Task<bool> IsValidPrimaryKeyAsync(
//        IDatabaseSchemaProvider provider,
//        string tableName,
//        PrimaryKeyDefinition pk)
//    {
//        return Task.FromResult(pk.Columns.Any());
//    }

//    private static Task<bool> IsValidIndexAsync(
//        IDatabaseSchemaProvider provider,
//        IndexDefinition index)
//    {
//        return Task.FromResult(index.Columns.Any());
//    }

//    private static Task<bool> IsMaterializedViewAsync(
//        IDatabaseSchemaProvider provider,
//        string tableName)
//    {
//        if (!IsPostgresProvider(provider))
//        {
//            return Task.FromResult(false);
//        }

//        var lower = tableName.ToLowerInvariant();

//        return Task.FromResult(
//            lower.Contains("_mv") ||
//            lower.Contains("matview"));
//    }

//    private static string BuildColumnDifferenceMessage(
//        ColumnDefinition sourceCol,
//        ColumnDefinition targetCol)
//    {
//        var differences = new List<string>();

//        if (!string.Equals(sourceCol.DataType, targetCol.DataType, StringComparison.OrdinalIgnoreCase))
//        {
//            differences.Add($"type source='{sourceCol.DataType}' target='{targetCol.DataType}'");
//        }

//        if (sourceCol.IsNullable != targetCol.IsNullable)
//        {
//            differences.Add($"nullable source='{sourceCol.IsNullable}' target='{targetCol.IsNullable}'");
//        }

//        if (!string.Equals(
//                NormalizeDefault(sourceCol.DefaultValue),
//                NormalizeDefault(targetCol.DefaultValue),
//                StringComparison.OrdinalIgnoreCase))
//        {
//            differences.Add($"default source='{sourceCol.DefaultValue}' target='{targetCol.DefaultValue}'");
//        }

//        if (differences.Count == 0)
//        {
//            differences.Add("column metadata differs");
//        }

//        return $"Column '{sourceCol.Name}' differs: {string.Join("; ", differences)}.";
//    }

//    private static string BuildColumnAlterScript(
//        string tableName,
//        ColumnDefinition sourceCol,
//        ColumnDefinition targetCol)
//    {
//        var sb = new StringBuilder();

//        if (!string.Equals(sourceCol.DataType, targetCol.DataType, StringComparison.OrdinalIgnoreCase))
//        {
//            sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" TYPE {sourceCol.DataType};");
//        }

//        if (sourceCol.IsNullable != targetCol.IsNullable)
//        {
//            if (sourceCol.IsNullable)
//            {
//                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" DROP NOT NULL;");
//            }
//            else
//            {
//                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" SET NOT NULL;");
//            }
//        }

//        var sourceDefault = NormalizeDefault(sourceCol.DefaultValue);
//        var targetDefault = NormalizeDefault(targetCol.DefaultValue);

//        if (!string.Equals(sourceDefault, targetDefault, StringComparison.OrdinalIgnoreCase))
//        {
//            if (string.IsNullOrWhiteSpace(sourceCol.DefaultValue))
//            {
//                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" DROP DEFAULT;");
//            }
//            else
//            {
//                sb.AppendLine($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{sourceCol.Name}\" SET DEFAULT {sourceCol.DefaultValue};");
//            }
//        }

//        return sb.ToString().Trim();
//    }

//    private static string NormalizeDefault(string? value)
//    {
//        return value?
//            .Trim()
//            .Replace(" ", string.Empty)
//            .Replace("::text", string.Empty, StringComparison.OrdinalIgnoreCase)
//            ?? string.Empty;
//    }
//}

// red green fix
//public class SchemaComparer : ISchemaComparer
//{
//    private const int TableMaxDegreeOfParallelism = 4;

//    // Keep this false for your current requirement:
//    // QA -> PROD: show what is missing/different in PROD only.
//    private const bool IncludeMissingInSource = false;

//    private readonly SqlDiffService _sqlDiffService;

//    public SchemaComparer()
//    {
//        _sqlDiffService = new SqlDiffService();
//    }

//    public async Task<IList<ComparisonResult>> CompareAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Action<int, int, string, bool>? progressLogger = null,
//        bool runForTest = false,
//        int testObjectLimit = 10,
//        ComparisonOptions? options = null)
//    {
//        options ??= new ComparisonOptions();

//        progressLogger?.Invoke(0, 0, "🔍 Fetching schema objects...", false);

//        // Fetch high-level object lists in parallel.
//        var sourceTablesTask = sourceProvider.GetTablesAsync();
//        var targetTablesTask = targetProvider.GetTablesAsync();

//        var sourceFunctionsTask = sourceProvider.GetFunctionsAsync();
//        var targetFunctionsTask = targetProvider.GetFunctionsAsync();

//        var sourceProceduresTask = sourceProvider.GetProceduresAsync();
//        var targetProceduresTask = targetProvider.GetProceduresAsync();

//        var sourceViewsTask = sourceProvider.GetViewsAsync();
//        var targetViewsTask = targetProvider.GetViewsAsync();

//        var sourceTriggersTask = sourceProvider.GetTriggersAsync();
//        var targetTriggersTask = targetProvider.GetTriggersAsync();

//        await Task.WhenAll(
//            sourceTablesTask,
//            targetTablesTask,
//            sourceFunctionsTask,
//            targetFunctionsTask,
//            sourceProceduresTask,
//            targetProceduresTask,
//            sourceViewsTask,
//            targetViewsTask,
//            sourceTriggersTask,
//            targetTriggersTask);

//        var sourceTables = await sourceTablesTask;
//        var targetTables = await targetTablesTask;

//        var sourceFunctions = await sourceFunctionsTask;
//        var targetFunctions = await targetFunctionsTask;

//        var sourceProcedures = await sourceProceduresTask;
//        var targetProcedures = await targetProceduresTask;

//        var sourceViews = await sourceViewsTask;
//        var targetViews = await targetViewsTask;

//        var sourceTriggers = await sourceTriggersTask;
//        var targetTriggers = await targetTriggersTask;

//        progressLogger?.Invoke(0, 0,
//            $"📦 Fetched objects | Source: Tables={sourceTables.Count}, Functions={sourceFunctions.Count}, Procedures={sourceProcedures.Count}, Views={sourceViews.Count}, Triggers={sourceTriggers.Count} | " +
//            $"Target: Tables={targetTables.Count}, Functions={targetFunctions.Count}, Procedures={targetProcedures.Count}, Views={targetViews.Count}, Triggers={targetTriggers.Count}",
//            false);

//        var limitedSourceTables = runForTest
//            ? sourceTables.Take(testObjectLimit).ToList()
//            : sourceTables;

//        var limitedTargetTables = targetTables;

//        string GetFunctionSignatureKey(DbFunctionDefinition def)
//        {
//            return $"{def.Name}({def.Arguments})";
//        }

//        string GetViewKey(DbViewDefinition def)
//        {
//            return def.Name ?? string.Empty;
//        }

//        string GetTriggerKey(DbTriggerDefinition def)
//        {
//            return $"{def.Table}|{def.Name}";
//        }

//        var sourceFunctionMap = sourceFunctions
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetFunctionSignatureKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetFunctionMap = targetFunctions
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetFunctionSignatureKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var sourceProcedureMap = sourceProcedures
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetFunctionSignatureKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetProcedureMap = targetProcedures
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetFunctionSignatureKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var sourceViewMap = sourceViews
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetViewKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetViewMap = targetViews
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetViewKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var sourceTriggerMap = sourceTriggers
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetTriggerKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetTriggerMap = targetTriggers
//            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
//            .GroupBy(GetTriggerKey, StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        if (runForTest)
//        {
//            sourceFunctionMap = sourceFunctionMap
//                .Take(testObjectLimit)
//                .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

//            sourceProcedureMap = sourceProcedureMap
//                .Take(testObjectLimit)
//                .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

//            sourceViewMap = sourceViewMap
//                .Take(testObjectLimit)
//                .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

//            sourceTriggerMap = sourceTriggerMap
//                .Take(testObjectLimit)
//                .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
//        }

//        var tableResults = new List<ComparisonResult>();
//        var functionResults = new List<ComparisonResult>();
//        var procedureResults = new List<ComparisonResult>();
//        var viewResults = new List<ComparisonResult>();
//        var triggerResults = new List<ComparisonResult>();

//        progressLogger?.Invoke(0, 0, "🚀 Comparing tables, functions, procedures, views and triggers in parallel...", false);

//        await Task.WhenAll(
//            CompareTablesAsync(
//                sourceProvider,
//                targetProvider,
//                limitedSourceTables,
//                limitedTargetTables,
//                tableResults,
//                progressLogger,
//                options),

//            CompareFunctionsAsync(
//                sourceProvider,
//                targetProvider,
//                sourceFunctionMap,
//                targetFunctionMap,
//                functionResults,
//                progressLogger,
//                options),

//            CompareProceduresAsync(
//                sourceProvider,
//                targetProvider,
//                sourceProcedureMap,
//                targetProcedureMap,
//                procedureResults,
//                progressLogger,
//                options),

//            CompareViewsAsync(
//                sourceProvider,
//                targetProvider,
//                sourceViewMap,
//                targetViewMap,
//                viewResults,
//                progressLogger,
//                options),

//            CompareTriggersAsync(
//                sourceProvider,
//                targetProvider,
//                sourceTriggerMap,
//                targetTriggerMap,
//                triggerResults,
//                progressLogger,
//                options));

//        var results = new List<ComparisonResult>();

//        results.AddRange(tableResults.OrderBy(x => x.Name));
//        results.AddRange(functionResults.OrderBy(x => x.Name));
//        results.AddRange(procedureResults.OrderBy(x => x.Name));
//        results.AddRange(viewResults.OrderBy(x => x.Name));
//        results.AddRange(triggerResults.OrderBy(x => x.Name));

//        progressLogger?.Invoke(0, 0,
//            $"✅ Schema comparison completed. Total={results.Count}, Match={results.Count(x => x.Status == ComparisonStatus.Match)}, Mismatch={results.Count(x => x.Status == ComparisonStatus.Mismatch)}, MissingInTarget={results.Count(x => x.Status == ComparisonStatus.MissingInTarget)}",
//            false);

//        return results;
//    }

//    private async Task CompareTablesAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        IList<string> sourceTables,
//        IList<string> targetTables,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, $"📄 Comparing tables with parallelism={TableMaxDegreeOfParallelism}...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        var targetTableSet = new HashSet<string>(targetTables, StringComparer.OrdinalIgnoreCase);
//        var resultBag = new ConcurrentBag<ComparisonResult>();

//        var completed = 0;
//        var total = sourceTables.Count;

//        var parallelOptions = new ParallelOptions
//        {
//            MaxDegreeOfParallelism = TableMaxDegreeOfParallelism
//        };

//        await Parallel.ForEachAsync(sourceTables, parallelOptions, async (tableName, cancellationToken) =>
//        {
//            var current = Interlocked.Increment(ref completed);

//            if (ShouldLogProgress(current, total))
//            {
//                progressLogger?.Invoke(current, total, $"🔄 Comparing table: {tableName}", true);
//            }

//            var source = await sourceProvider.GetTableDefinitionAsync(tableName);

//            var target = targetTableSet.Contains(tableName)
//                ? await targetProvider.GetTableDefinitionAsync(tableName)
//                : null;

//            if (target == null)
//            {
//                resultBag.Add(CreateResult(
//                    tableName,
//                    SchemaObjectType.Table,
//                    ComparisonStatus.MissingInTarget,
//                    "Exists in source, missing in target"));

//                return;
//            }

//            var subResults = new List<ComparisonSubResult>();
//            var overallStatus = ComparisonStatus.Match;

//            if (!source.StructuralEquals(target))
//            {
//                overallStatus = ComparisonStatus.Mismatch;

//                if (!AreScriptsEqual(source.CreateScript, target.CreateScript, sourceDbKind, targetDbKind, options))
//                {
//                    subResults.Add(new ComparisonSubResult(
//                        "StructuralDiff",
//                        ComparisonStatus.Mismatch,
//                        "Table structure differs using order-independent semantic comparison",
//                        $"-- SOURCE SIGNATURE\n{source.GetStructuralSignature()}\n\n-- TARGET SIGNATURE\n{target.GetStructuralSignature()}\n\n-- SOURCE SCRIPT\n{source.CreateScript}\n\n-- TARGET SCRIPT\n{target.CreateScript}"
//                    ));
//                }
//            }

//            overallStatus |= await ComparePrimaryKeysAsync(sourceProvider, targetProvider, source, target, subResults);
//            overallStatus |= CompareColumns(source, target, subResults);
//            overallStatus |= await CompareForeignKeysAsync(sourceProvider, targetProvider, source, target, subResults);
//            overallStatus |= await CompareIndexesAsync(sourceProvider, targetProvider, source, target, subResults);
//            overallStatus |= await CompareConstraintsAsync(sourceProvider, targetProvider, source, target, subResults);

//            var result = new ComparisonResult
//            {
//                ObjectType = SchemaObjectType.Table,
//                Name = source.Name,
//                Status = overallStatus,
//                DiffScript = BuildDiffScript(source, target, subResults, overallStatus),
//                SubResults = subResults
//            };

//            EnhanceComparisonResultWithDiff(result, source.CreateScript, target.CreateScript);

//            resultBag.Add(result);
//        });

//        results.AddRange(resultBag.OrderBy(x => x.Name));

//        if (IncludeMissingInSource)
//        {
//            var sourceTableSet = new HashSet<string>(sourceTables, StringComparer.OrdinalIgnoreCase);

//            foreach (var targetTable in targetTables.Where(t => !sourceTableSet.Contains(t)))
//            {
//                var result = await HandleTableMissingInSourceAsync(targetProvider, targetTable);
//                results.Add(result);
//            }
//        }
//    }

//    private async Task<ComparisonStatus> ComparePrimaryKeysAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var sourceIsMatView = await IsMaterializedViewAsync(sourceProvider, source.Name);
//        var targetIsMatView = await IsMaterializedViewAsync(targetProvider, target.Name);

//        if (sourceIsMatView || targetIsMatView)
//        {
//            return ComparisonStatus.Match;
//        }

//        var sourcePk = source.PrimaryKeys.FirstOrDefault();
//        var targetPk = target.PrimaryKeys.FirstOrDefault();

//        var sourceValid = sourcePk != null && await IsValidPrimaryKeyAsync(sourceProvider, source.Name, sourcePk);
//        var targetValid = targetPk != null && await IsValidPrimaryKeyAsync(targetProvider, target.Name, targetPk);

//        if (!targetValid && sourceValid)
//        {
//            var script = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);

//            subResults.Add(new(
//                "PrimaryKeys",
//                ComparisonStatus.MissingInTarget,
//                $"Primary key missing/invalid in target: columns ({string.Join(", ", sourcePk!.Columns)})",
//                script ?? string.Empty));

//            return ComparisonStatus.Mismatch;
//        }

//        if (sourceValid && targetValid && !sourcePk!.StructuralEquals(targetPk!))
//        {
//            var sourceScript = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
//            var targetScript = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);

//            subResults.Add(new(
//                "PrimaryKeys",
//                ComparisonStatus.Mismatch,
//                $"Primary key structure differs: source({string.Join(", ", sourcePk.Columns)}) vs target({string.Join(", ", targetPk.Columns)})",
//                $"-- SOURCE PK\n{sourceScript}\n\n-- TARGET PK\n{targetScript}"));

//            return ComparisonStatus.Mismatch;
//        }

//        return ComparisonStatus.Match;
//    }

//    private ComparisonStatus CompareColumns(
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        var sourceCols = source.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
//        var targetCols = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

//        foreach (var col in sourceCols.Values)
//        {
//            if (!targetCols.TryGetValue(col.Name, out var targetCol))
//            {
//                status = ComparisonStatus.Mismatch;

//                subResults.Add(new(
//                    "Columns",
//                    ComparisonStatus.MissingInTarget,
//                    $"Column '{col.Name}' is missing in target.",
//                    $"ALTER TABLE \"{source.Name}\" ADD COLUMN \"{col.Name}\" {col.DataType} {(col.IsNullable ? string.Empty : "NOT NULL")};"));
//            }
//            else if (!col.Equals(targetCol))
//            {
//                status = ComparisonStatus.Mismatch;

//                subResults.Add(new(
//                    "Columns",
//                    ComparisonStatus.Mismatch,
//                    $"Column '{col.Name}' definition differs: source({col.DataType}, nullable={col.IsNullable}) vs target({targetCol.DataType}, nullable={targetCol.IsNullable})",
//                    string.Empty));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var col in targetCols.Values)
//            {
//                if (!sourceCols.ContainsKey(col.Name))
//                {
//                    status = ComparisonStatus.Mismatch;

//                    subResults.Add(new(
//                        "Columns",
//                        ComparisonStatus.MissingInSource,
//                        $"Column '{col.Name}' exists in target but not in source.",
//                        string.Empty));
//                }
//            }
//        }

//        return status;
//    }

//    private async Task<ComparisonStatus> CompareForeignKeysAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        var sourceFksByStructure = source.ForeignKeys
//            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetFksByStructure = target.ForeignKeys
//            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        foreach (var structuralKey in sourceFksByStructure.Keys)
//        {
//            var sourceFk = sourceFksByStructure[structuralKey];

//            if (!targetFksByStructure.TryGetValue(structuralKey, out var targetFk))
//            {
//                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);

//                subResults.Add(new(
//                    "ForeignKeys",
//                    ComparisonStatus.MissingInTarget,
//                    $"Foreign key '{structuralKey}' missing in target",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;
//            }
//            else if (!sourceFk.StructuralEquals(targetFk))
//            {
//                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);

//                subResults.Add(new(
//                    "ForeignKeys",
//                    ComparisonStatus.Mismatch,
//                    $"Foreign key structure '{structuralKey}' differs",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var structuralKey in targetFksByStructure.Keys.Where(k => !sourceFksByStructure.ContainsKey(k)))
//            {
//                var targetFk = targetFksByStructure[structuralKey];

//                subResults.Add(new(
//                    "ForeignKeys",
//                    ComparisonStatus.MissingInSource,
//                    $"Foreign key '{structuralKey}' exists in target but not in source",
//                    $"-- Optional drop in target: ALTER TABLE \"{target.Name}\" DROP CONSTRAINT IF EXISTS \"{targetFk.Name}\";"));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        return status;
//    }

//    private async Task<ComparisonStatus> CompareIndexesAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        var validSourceIndexes = new List<IndexDefinition>();

//        foreach (var index in source.Indexes)
//        {
//            if (await IsValidIndexAsync(sourceProvider, index))
//            {
//                validSourceIndexes.Add(index);
//            }
//        }

//        var validTargetIndexes = new List<IndexDefinition>();

//        foreach (var index in target.Indexes)
//        {
//            if (await IsValidIndexAsync(targetProvider, index))
//            {
//                validTargetIndexes.Add(index);
//            }
//        }

//        var sourceIndexesByStructure = validSourceIndexes
//            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetIndexesByStructure = validTargetIndexes
//            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        foreach (var structuralKey in sourceIndexesByStructure.Keys)
//        {
//            var sourceIndex = sourceIndexesByStructure[structuralKey];

//            if (!targetIndexesByStructure.TryGetValue(structuralKey, out var targetIndex))
//            {
//                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);

//                subResults.Add(new(
//                    "Indexes",
//                    ComparisonStatus.MissingInTarget,
//                    $"Index '{structuralKey}' is missing in target.",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;
//            }
//            else if (!sourceIndex.StructuralEquals(targetIndex))
//            {
//                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);

//                subResults.Add(new(
//                    "Indexes",
//                    ComparisonStatus.Mismatch,
//                    $"Index structure '{structuralKey}' differs",
//                    script ?? string.Empty));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var structuralKey in targetIndexesByStructure.Keys.Where(k => !sourceIndexesByStructure.ContainsKey(k)))
//            {
//                var targetIndex = targetIndexesByStructure[structuralKey];

//                subResults.Add(new(
//                    "Indexes",
//                    ComparisonStatus.MissingInSource,
//                    $"Index '{structuralKey}' exists in target but not in source.",
//                    $"-- Optional drop in target: DROP INDEX IF EXISTS \"{targetIndex.Name}\";"));

//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        return status;
//    }

//    private static Task<ComparisonStatus> CompareConstraintsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        // Placeholder. Keep existing behavior.
//        // Add unique/check constraint models later if your core models support them.
//        return Task.FromResult(ComparisonStatus.Match);
//    }

//    private string? BuildDiffScript(
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults,
//        ComparisonStatus status)
//    {
//        if (status == ComparisonStatus.Match)
//        {
//            return null;
//        }

//        var sb = new StringBuilder();

//        foreach (var sub in subResults.Where(s => !string.IsNullOrWhiteSpace(s.CreateScript)))
//        {
//            sb.AppendLine($"-- {sub.Component}: {sub.Status}");
//            sb.AppendLine(sub.CreateScript!.Trim());
//            sb.AppendLine();
//        }

//        if (sb.Length == 0)
//        {
//            sb.AppendLine("-- SOURCE");
//            sb.AppendLine(source.CreateScript?.Trim());
//            sb.AppendLine();
//            sb.AppendLine("-- TARGET");
//            sb.AppendLine(target.CreateScript?.Trim());
//        }

//        return sb.ToString().Trim();
//    }

//    private void EnhanceComparisonResultWithDiff(
//        ComparisonResult result,
//        string? sourceScript,
//        string? targetScript)
//    {
//        result.SourceScript = sourceScript?.Trim();
//        result.TargetScript = targetScript?.Trim();

//        if (result.Status == ComparisonStatus.Match)
//        {
//            return;
//        }

//        if (string.IsNullOrWhiteSpace(sourceScript) && string.IsNullOrWhiteSpace(targetScript))
//        {
//            return;
//        }

//        var diffResult = _sqlDiffService.ComputeDiff(sourceScript, targetScript);

//        if (diffResult.HasDifferences)
//        {
//            result.SideBySideDiffHtml = _sqlDiffService.GenerateSideBySideHtml(diffResult);
//        }
//    }

//    private async Task<ComparisonResult> HandleTableMissingInSourceAsync(
//        IDatabaseSchemaProvider provider,
//        string tableName)
//    {
//        var table = await provider.GetTableDefinitionAsync(tableName);
//        var subResults = new List<ComparisonSubResult>();

//        if (!string.IsNullOrWhiteSpace(table.CreateScript))
//        {
//            subResults.Add(new(
//                "CreateScript",
//                ComparisonStatus.MissingInSource,
//                "Create script missing in source",
//                table.CreateScript));
//        }

//        return new ComparisonResult
//        {
//            ObjectType = SchemaObjectType.Table,
//            Name = table.Name,
//            Status = ComparisonStatus.MissingInSource,
//            Details = "Exists in target, missing in source",
//            DiffScript = BuildDiffScript(table, table, subResults, ComparisonStatus.MissingInSource),
//            SubResults = subResults
//        };
//    }

//    private Task CompareFunctionsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbFunctionDefinition> sourceFunctions,
//        Dictionary<string, DbFunctionDefinition> targetFunctionMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "⚙️ Comparing functions using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceFunctions.Count;
//        int index = 0;

//        foreach (var kvp in sourceFunctions)
//        {
//            index++;

//            var signature = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"⚙️ Comparing function: {source.Name}", true);
//            }

//            if (!targetFunctionMap.TryGetValue(signature, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? signature,
//                    SchemaObjectType.Function,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Function,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Function definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name ?? signature, SchemaObjectType.Function, ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetFunctionMap)
//            {
//                if (!sourceFunctions.ContainsKey(kvp.Key))
//                {
//                    var target = kvp.Value;

//                    results.Add(CreateResult(
//                        target.Name ?? kvp.Key,
//                        SchemaObjectType.Function,
//                        ComparisonStatus.MissingInSource,
//                        target.Definition ?? string.Empty));
//                }
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private Task CompareProceduresAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbFunctionDefinition> sourceProcedureMap,
//        Dictionary<string, DbFunctionDefinition> targetProcedureMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "🛠 Comparing procedures using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceProcedureMap.Count;
//        int index = 0;

//        foreach (var kvp in sourceProcedureMap)
//        {
//            index++;

//            var signatureKey = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"🛠 Comparing procedure: {source.Name}", true);
//            }

//            if (!targetProcedureMap.TryGetValue(signatureKey, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? signatureKey,
//                    SchemaObjectType.Procedure,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Procedure,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Procedure definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name ?? signatureKey, SchemaObjectType.Procedure, ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetProcedureMap)
//            {
//                if (!sourceProcedureMap.ContainsKey(kvp.Key))
//                {
//                    var target = kvp.Value;

//                    results.Add(CreateResult(
//                        target.Name ?? kvp.Key,
//                        SchemaObjectType.Procedure,
//                        ComparisonStatus.MissingInSource,
//                        target.Definition ?? string.Empty));
//                }
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private Task CompareViewsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbViewDefinition> sourceViewMap,
//        Dictionary<string, DbViewDefinition> targetViewMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "🔍 Comparing views using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceViewMap.Count;
//        int index = 0;

//        foreach (var kvp in sourceViewMap)
//        {
//            index++;

//            var viewKey = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"🔍 Comparing view: {source.Name}", true);
//            }

//            if (!targetViewMap.TryGetValue(viewKey, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? viewKey,
//                    SchemaObjectType.View,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.View,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "View definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name ?? viewKey, SchemaObjectType.View, ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetViewMap)
//            {
//                if (!sourceViewMap.ContainsKey(kvp.Key))
//                {
//                    var target = kvp.Value;

//                    results.Add(CreateResult(
//                        target.Name ?? kvp.Key,
//                        SchemaObjectType.View,
//                        ComparisonStatus.MissingInSource,
//                        target.Definition ?? string.Empty));
//                }
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private Task CompareTriggersAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbTriggerDefinition> sourceTriggerMap,
//        Dictionary<string, DbTriggerDefinition> targetTriggerMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        progressLogger?.Invoke(0, 0, "⏰ Comparing triggers using already-fetched definitions...", false);

//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceTriggerMap.Count;
//        int index = 0;

//        foreach (var kvp in sourceTriggerMap)
//        {
//            index++;

//            var triggerKey = kvp.Key;
//            var source = kvp.Value;

//            if (ShouldLogProgress(index, total))
//            {
//                progressLogger?.Invoke(index, total, $"⏰ Comparing trigger: {source.Name}", true);
//            }

//            if (!targetTriggerMap.TryGetValue(triggerKey, out var target))
//            {
//                results.Add(CreateResult(
//                    source.Name ?? triggerKey,
//                    SchemaObjectType.Trigger,
//                    ComparisonStatus.MissingInTarget,
//                    source.Definition ?? string.Empty));

//                continue;
//            }

//            var sourceDef = source.Definition ?? string.Empty;
//            var targetDef = target.Definition ?? string.Empty;

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Trigger,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Trigger definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };

//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name ?? triggerKey, SchemaObjectType.Trigger, ComparisonStatus.Match));
//            }
//        }

//        if (IncludeMissingInSource)
//        {
//            foreach (var kvp in targetTriggerMap)
//            {
//                if (!sourceTriggerMap.ContainsKey(kvp.Key))
//                {
//                    var target = kvp.Value;

//                    results.Add(CreateResult(
//                        target.Name ?? kvp.Key,
//                        SchemaObjectType.Trigger,
//                        ComparisonStatus.MissingInSource,
//                        target.Definition ?? string.Empty));
//                }
//            }
//        }

//        return Task.CompletedTask;
//    }

//    private ComparisonResult CreateResult(
//        string name,
//        SchemaObjectType type,
//        ComparisonStatus status,
//        string diffScript = "")
//    {
//        var result = new ComparisonResult
//        {
//            ObjectType = type,
//            Name = name,
//            Status = status,
//            DiffScript = diffScript
//        };

//        if (type == SchemaObjectType.Function ||
//            type == SchemaObjectType.Procedure ||
//            type == SchemaObjectType.View ||
//            type == SchemaObjectType.Trigger)
//        {
//            if (status == ComparisonStatus.MissingInTarget)
//            {
//                EnhanceComparisonResultWithDiff(result, diffScript, null);
//            }
//            else if (status == ComparisonStatus.MissingInSource)
//            {
//                EnhanceComparisonResultWithDiff(result, null, diffScript);
//            }
//        }

//        return result;
//    }

//    private static bool AreScriptsEqual(
//        string? sourceScript,
//        string? targetScript,
//        string sourceDbKind,
//        string targetDbKind,
//        ComparisonOptions options)
//    {
//        if (options.IgnoreOwnership)
//        {
//            var canonicalizedSource = DefinitionCanonicalizer.CanonicalizeDefinition(sourceScript, sourceDbKind, options);
//            var canonicalizedTarget = DefinitionCanonicalizer.CanonicalizeDefinition(targetScript, targetDbKind, options);

//            return string.Equals(canonicalizedSource, canonicalizedTarget, StringComparison.OrdinalIgnoreCase);
//        }

//        return string.Equals(sourceScript?.Trim(), targetScript?.Trim(), StringComparison.OrdinalIgnoreCase);
//    }

//    private static bool ShouldLogProgress(int current, int total)
//    {
//        if (current == 1 || current == total)
//        {
//            return true;
//        }

//        if (total <= 20)
//        {
//            return true;
//        }

//        return current % 10 == 0;
//    }

//    private static bool IsPostgresProvider(IDatabaseSchemaProvider provider)
//    {
//        return provider.GetType().Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase);
//    }

//    private static bool IsMySqlProvider(IDatabaseSchemaProvider provider)
//    {
//        return provider.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
//               provider.GetType().Name.Contains("MySQL", StringComparison.OrdinalIgnoreCase);
//    }

//    private static string GetDbKind(IDatabaseSchemaProvider provider)
//    {
//        if (IsPostgresProvider(provider))
//        {
//            return "postgres";
//        }

//        if (IsMySqlProvider(provider))
//        {
//            return "mysql";
//        }

//        return "unknown";
//    }

//    private static Task<bool> IsValidPrimaryKeyAsync(
//        IDatabaseSchemaProvider provider,
//        string tableName,
//        PrimaryKeyDefinition pk)
//    {
//        return Task.FromResult(pk.Columns.Any());
//    }

//    private static Task<bool> IsValidIndexAsync(
//        IDatabaseSchemaProvider provider,
//        IndexDefinition index)
//    {
//        return Task.FromResult(index.Columns.Any());
//    }

//    private static Task<bool> IsMaterializedViewAsync(
//        IDatabaseSchemaProvider provider,
//        string tableName)
//    {
//        if (!IsPostgresProvider(provider))
//        {
//            return Task.FromResult(false);
//        }

//        var lower = tableName.ToLowerInvariant();

//        return Task.FromResult(
//            lower.Contains("_mv") ||
//            lower.Contains("matview"));
//    }
//}




//*******old code 
//public class SchemaComparer : ISchemaComparer
//{
//    private readonly SqlDiffService _sqlDiffService;
//    private ComparisonOptions _currentOptions = new();

//    public SchemaComparer()
//    {
//        _sqlDiffService = new SqlDiffService();
//    }
//    public async Task<IList<ComparisonResult>> CompareAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Action<int, int, string, bool>? progressLogger = null,
//        bool runForTest = false, int testObjectLimit = 10,
//        ComparisonOptions? options = null)
//    {
//        options ??= new ComparisonOptions();
//        _currentOptions = options;
//        var results = new List<ComparisonResult>();

//        progressLogger?.Invoke(0, 0, "🔍 Fetching schema objects...", false);

//        // --- TABLES ---
//        var sourceTables = await sourceProvider.GetTablesAsync();
//        var targetTables = await targetProvider.GetTablesAsync();

//        var limitedSourceTables = runForTest ? sourceTables.Take(testObjectLimit).ToList() : sourceTables;
//        var limitedTargetTables = runForTest
//            ? targetTables.Where(t => limitedSourceTables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList()
//            : targetTables;

//        progressLogger?.Invoke(0, 0, $"📄 {(runForTest ? $"Test mode: comparing top {testObjectLimit} tables..." : "Comparing tables...")}", false);

//        await CompareTablesAsync(
//            sourceProvider,
//            targetProvider,
//            limitedSourceTables,
//            limitedTargetTables,
//            results,
//            progressLogger,
//            options);

//        // --- FUNCTIONS ---
//        string GetSignatureKey(DbFunctionDefinition def) =>
//            $"{def.Name}({def.Arguments})";

//        var sourceFunctions = await sourceProvider.GetFunctionsAsync();
//        var targetFunctions = await targetProvider.GetFunctionsAsync();
//        var sourceFunctionMap = sourceFunctions.ToDictionary(f => GetSignatureKey(f), StringComparer.OrdinalIgnoreCase);
//        var targetFunctionMap = targetFunctions.ToDictionary(f => GetSignatureKey(f), StringComparer.OrdinalIgnoreCase);

//        var limitedSourceFunctions = runForTest
//            ? sourceFunctionMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
//            : sourceFunctionMap;

//        var limitedTargetFunctionMap = targetFunctionMap
//            .Where(kvp => limitedSourceFunctions.ContainsKey(kvp.Key))
//            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

//        progressLogger?.Invoke(0, 0, $"⚙️ {(runForTest ? $"Test mode: comparing top {testObjectLimit} functions..." : "Comparing functions...")}", false);

//        await CompareFunctionsAsync(
//           sourceProvider,
//           targetProvider,
//           limitedSourceFunctions,
//           limitedTargetFunctionMap,
//           results,
//           progressLogger,
//           options);

//        // --- PROCEDURES ---
//        var sourceProcedures = await sourceProvider.GetProceduresAsync();
//        var targetProcedures = await targetProvider.GetProceduresAsync();

//        var sourceProcedureMap = sourceProcedures.ToDictionary(p => GetSignatureKey(p), StringComparer.OrdinalIgnoreCase);
//        var targetProcedureMap = targetProcedures.ToDictionary(p => GetSignatureKey(p), StringComparer.OrdinalIgnoreCase);

//        var limitedSourceProcedureMap = runForTest
//            ? sourceProcedureMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
//            : sourceProcedureMap;

//        var limitedTargetProcedureMap = targetProcedureMap
//            .Where(kvp => limitedSourceProcedureMap.ContainsKey(kvp.Key))
//            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

//        progressLogger?.Invoke(0, 0, $"🛠 {(runForTest ? $"Test mode: comparing top {testObjectLimit} procedures..." : "Comparing procedures...")}", false);

//        await CompareProceduresAsync(
//            sourceProvider,
//            targetProvider,
//            limitedSourceProcedureMap,
//            limitedTargetProcedureMap,
//            results,
//            progressLogger,
//            options);

//        // --- VIEWS ---
//        var sourceViews = await sourceProvider.GetViewsAsync();
//        var targetViews = await targetProvider.GetViewsAsync();
//        var sourceViewMap = sourceViews.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
//        var targetViewMap = targetViews.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

//        var limitedSourceViewMap = runForTest
//            ? sourceViewMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
//            : sourceViewMap;

//        var limitedTargetViewMap = targetViewMap
//            .Where(kvp => limitedSourceViewMap.ContainsKey(kvp.Key))
//            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

//        progressLogger?.Invoke(0, 0, $"🔍 {(runForTest ? $"Test mode: comparing top {testObjectLimit} views..." : "Comparing views...")}", false);

//        await CompareViewsAsync(
//            sourceProvider,
//            targetProvider,
//            limitedSourceViewMap,
//            limitedTargetViewMap,
//            results,
//            progressLogger,
//            options);

//        // --- TRIGGERS ---
//        var sourceTriggers = await sourceProvider.GetTriggersAsync();
//        var targetTriggers = await targetProvider.GetTriggersAsync();
//        Func<DbTriggerDefinition, string> triggerKeySelector = t => $"{t.Table}|{t.Name}";
//        var sourceTriggerMap = sourceTriggers.ToDictionary(triggerKeySelector, StringComparer.OrdinalIgnoreCase);
//        var targetTriggerMap = targetTriggers.ToDictionary(triggerKeySelector, StringComparer.OrdinalIgnoreCase);

//        var limitedSourceTriggerMap = runForTest
//            ? sourceTriggerMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
//            : sourceTriggerMap;

//        var limitedTargetTriggerMap = targetTriggerMap
//            .Where(kvp => limitedSourceTriggerMap.ContainsKey(kvp.Key))
//            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

//        progressLogger?.Invoke(0, 0, $"⏰ {(runForTest ? $"Test mode: comparing top {testObjectLimit} triggers..." : "Comparing triggers...")}", false);

//        await CompareTriggersAsync(
//            sourceProvider,
//            targetProvider,
//            limitedSourceTriggerMap,
//            limitedTargetTriggerMap,
//            results,
//            progressLogger,
//            options);

//        // --- SEQUENCES ---
//        var sourceSequences = await sourceProvider.GetSequencesAsync();
//        var targetSequences = await targetProvider.GetSequencesAsync();
//        var targetSequenceSet = new HashSet<string>(targetSequences, StringComparer.OrdinalIgnoreCase);
//        var sourceSequenceSet = new HashSet<string>(sourceSequences, StringComparer.OrdinalIgnoreCase);

//        progressLogger?.Invoke(0, 0, "🔢 Comparing sequences...", false);

//        foreach (var seq in sourceSequences)
//        {
//            if (!targetSequenceSet.Contains(seq))
//            {
//                var def = await sourceProvider.GetSequenceDefinitionAsync(seq);
//                results.Add(CreateResult(seq, SchemaObjectType.Sequence, ComparisonStatus.MissingInTarget, def ?? ""));
//            }
//        }

//        foreach (var seq in targetSequences.Where(s => !sourceSequenceSet.Contains(s)))
//        {
//            results.Add(CreateResult(seq, SchemaObjectType.Sequence, ComparisonStatus.MissingInSource, ""));
//        }

//        progressLogger?.Invoke(0, 0, "✅ Schema comparison completed.", false);
//        return results;
//    }

//    // ---------- COMPARE METHODS -----------

//    private async Task CompareTablesAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        IList<string> sourceTables,
//        IList<string> targetTables,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceTables.Count;
//        var targetTableSet = new HashSet<string>(targetTables, StringComparer.OrdinalIgnoreCase);

//        for (int i = 0; i < total; i++)
//        {
//            var tableName = sourceTables[i];
//            progressLogger?.Invoke(i + 1, total, $"🔄 Comparing table: {tableName}", true);

//            var source = await sourceProvider.GetTableDefinitionAsync(tableName);
//            var target = targetTableSet.Contains(tableName)
//                ? await targetProvider.GetTableDefinitionAsync(tableName)
//                : null;

//            if (target == null)
//            {
//                results.Add(CreateResult(tableName, SchemaObjectType.Table, ComparisonStatus.MissingInTarget, "Exists in source, missing in target"));
//                continue;
//            }

//            var subResults = new List<ComparisonSubResult>();
//            var overallStatus = ComparisonStatus.Match;

//            // First check: Use structural signature for semantic comparison (order-independent)
//            if (!source.StructuralEquals(target))
//            {
//                overallStatus = ComparisonStatus.Mismatch;

//                // Only add script comparison as supplementary info if structural differs
//                if (!AreScriptsEqual(source.CreateScript, target.CreateScript, sourceDbKind, targetDbKind, options))
//                {
//                    subResults.Add(new ComparisonSubResult(
//                        "StructuralDiff",
//                        ComparisonStatus.Mismatch,
//                        "Table structure differs (order-independent semantic comparison)",
//                        $"-- SOURCE SIGNATURE\n{source.GetStructuralSignature()}\n\n-- TARGET SIGNATURE\n{target.GetStructuralSignature()}\n\n-- SOURCE SCRIPT\n{source.CreateScript}\n\n-- TARGET SCRIPT\n{target.CreateScript}"
//                    ));
//                }
//            }

//            overallStatus |= await ComparePrimaryKeysAsync(sourceProvider, targetProvider, source, target, subResults);
//            overallStatus |= CompareColumns(source, target, subResults);
//            overallStatus |= await CompareForeignKeysAsync(sourceProvider, targetProvider, source, target, subResults);
//            overallStatus |= await CompareIndexesAsync(sourceProvider, targetProvider, source, target, subResults);
//            overallStatus |= await CompareConstraintsAsync(sourceProvider, targetProvider, source, target, subResults);

//            var diffScript = BuildDiffScript(source, target, subResults, overallStatus);

//            var res = new ComparisonResult
//            {
//                ObjectType = SchemaObjectType.Table,
//                Name = source.Name,
//                Status = overallStatus,
//                DiffScript = diffScript,
//                SubResults = subResults
//            };

//            // Enhance with side-by-side diff information
//            EnhanceComparisonResultWithDiff(res, source.CreateScript, target.CreateScript, _currentOptions.SourceLabel, _currentOptions.TargetLabel);

//            results.Add(res);
//        }

//        var sourceTableSet = new HashSet<string>(sourceTables, StringComparer.OrdinalIgnoreCase);
//        foreach (var targetTable in targetTables.Where(t => !sourceTableSet.Contains(t)))
//        {
//            var result = await HandleTableMissingInSourceAsync(targetProvider, targetTable);
//            results.Add(result);
//        }
//    }

//    private async Task<ComparisonStatus> ComparePrimaryKeysAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        // Check if tables are materialized views (skip PK logic for matviews)
//        var sourceIsMatView = await IsMaterializedViewAsync(sourceProvider, source.Name);
//        var targetIsMatView = await IsMaterializedViewAsync(targetProvider, target.Name);

//        if (sourceIsMatView || targetIsMatView)
//        {
//            // Skip PK comparison for materialized views
//            return ComparisonStatus.Match;
//        }

//        var sourcePk = source.PrimaryKeys.FirstOrDefault();
//        var targetPk = target.PrimaryKeys.FirstOrDefault();

//        // Validate PKs with provider-specific logic (handles invalid/dropped PKs)
//        var sourceValid = sourcePk != null && await IsValidPrimaryKeyAsync(sourceProvider, source.Name, sourcePk);
//        var targetValid = targetPk != null && await IsValidPrimaryKeyAsync(targetProvider, target.Name, targetPk);

//        if (!sourceValid && targetValid)
//        {
//            var script = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);
//            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInSource, 
//                $"Primary key missing/invalid in source: columns ({string.Join(", ", targetPk!.Columns)})", script ?? ""));
//            return ComparisonStatus.Mismatch;
//        }
//        else if (!targetValid && sourceValid)
//        {
//            var script = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
//            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInTarget, 
//                $"Primary key missing/invalid in target: columns ({string.Join(", ", sourcePk!.Columns)})", script ?? ""));
//            return ComparisonStatus.Mismatch;
//        }
//        else if (sourceValid && targetValid)
//        {
//            // Compare by structural definition (column list), not by name
//            if (!sourcePk!.StructuralEquals(targetPk!))
//            {
//                var sourceScript = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
//                var targetScript = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);
//                subResults.Add(new("PrimaryKeys", ComparisonStatus.Mismatch, 
//                    $"Primary key structure differs: source({string.Join(", ", sourcePk.Columns)}) vs target({string.Join(", ", targetPk.Columns)})", 
//                    $"-- SOURCE PK\n{sourceScript}\n\n-- TARGET PK\n{targetScript}"));
//                return ComparisonStatus.Mismatch;
//            }
//        }

//        return ComparisonStatus.Match;
//    }

//    private ComparisonStatus CompareColumns(TableDefinition source, TableDefinition target, List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;
//        var sourceCols = source.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
//        var targetCols = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

//        foreach (var col in sourceCols.Values)
//        {
//            if (!targetCols.TryGetValue(col.Name, out var tgt))
//            {
//                status = ComparisonStatus.Mismatch;
//                subResults.Add(new("Columns", ComparisonStatus.MissingInTarget, $"Column '{col.Name}' is missing in target.",
//                    $"ALTER TABLE \"{source.Name}\" ADD COLUMN \"{col.Name}\" {col.DataType} {(col.IsNullable ? "" : "NOT NULL")};"));
//            }
//            else if (!col.Equals(tgt))
//            {
//                status = ComparisonStatus.Mismatch;
//                subResults.Add(new("Columns", ComparisonStatus.Mismatch,
//                    $"Column '{col.Name}' definition differs: source({col.DataType}) vs target({tgt.DataType})", string.Empty));
//            }
//        }

//        foreach (var col in targetCols.Values)
//        {
//            if (!sourceCols.ContainsKey(col.Name))
//            {
//                status = ComparisonStatus.Mismatch;
//                subResults.Add(new("Columns", ComparisonStatus.MissingInSource,
//                    $"Column '{col.Name}' is missing in source (may be deprecated).", string.Empty));
//            }
//        }

//        return status;
//    }

//    private async Task<ComparisonStatus> CompareForeignKeysAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        // Group FKs by structural key to avoid double-counting and enable semantic comparison
//        var sourceFksByStructure = source.ForeignKeys
//            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetFksByStructure = target.ForeignKeys
//            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var allFkStructures = sourceFksByStructure.Keys.Union(targetFksByStructure.Keys, StringComparer.OrdinalIgnoreCase);

//        foreach (var structuralKey in allFkStructures)
//        {
//            var inSource = sourceFksByStructure.TryGetValue(structuralKey, out var sourceFk);
//            var inTarget = targetFksByStructure.TryGetValue(structuralKey, out var targetFk);

//            if (inSource && inTarget)
//            {
//                // Already structurally equal by grouping key, but check for any additional differences
//                if (!sourceFk!.StructuralEquals(targetFk!))
//                {
//                    var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);
//                    subResults.Add(new("ForeignKeys", ComparisonStatus.Mismatch, 
//                        $"Foreign key structure '{structuralKey}' has differences", script ?? ""));
//                    status = ComparisonStatus.Mismatch;
//                }
//            }
//            else if (inSource && !inTarget)
//            {
//                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk!.Name);
//                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInTarget, 
//                    $"Foreign key '{structuralKey}' missing in target", script ?? ""));
//                status = ComparisonStatus.Mismatch;
//            }
//            else if (!inSource && inTarget)
//            {
//                var script = await targetProvider.GetForeignKeyCreateScriptAsync(target.Name, targetFk!.Name);
//                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInSource, 
//                    $"Foreign key '{structuralKey}' missing in source", script ?? ""));
//                status = ComparisonStatus.Mismatch;
//            }
//        }
//        return status;
//    }

//    private async Task<ComparisonStatus> CompareIndexesAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        // Filter out invalid indexes using provider-specific validation
//        var validSourceIndexes = new List<IndexDefinition>();
//        foreach (var idx in source.Indexes)
//        {
//            if (await IsValidIndexAsync(sourceProvider, idx))
//                validSourceIndexes.Add(idx);
//        }

//        var validTargetIndexes = new List<IndexDefinition>();
//        foreach (var idx in target.Indexes)
//        {
//            if (await IsValidIndexAsync(targetProvider, idx))
//                validTargetIndexes.Add(idx);
//        }

//        // Group indexes by structural key to enable semantic comparison (method + columns + options + predicate)
//        var sourceIndexesByStructure = validSourceIndexes
//            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetIndexesByStructure = validTargetIndexes
//            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var allIndexStructures = sourceIndexesByStructure.Keys.Union(targetIndexesByStructure.Keys, StringComparer.OrdinalIgnoreCase);

//        foreach (var structuralKey in allIndexStructures)
//        {
//            var inSource = sourceIndexesByStructure.TryGetValue(structuralKey, out var sourceIndex);
//            var inTarget = targetIndexesByStructure.TryGetValue(structuralKey, out var targetIndex);

//            if (inSource && inTarget)
//            {
//                // Already structurally equal by grouping key, but verify
//                if (!sourceIndex!.StructuralEquals(targetIndex!))
//                {
//                    var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);
//                    subResults.Add(new("Indexes", ComparisonStatus.Mismatch,
//                        $"Index structure '{structuralKey}' has subtle differences", script ?? ""));
//                    status = ComparisonStatus.Mismatch;
//                }
//            }
//            else if (inSource && !inTarget)
//            {
//                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex!.Name);
//                subResults.Add(new("Indexes", ComparisonStatus.MissingInTarget,
//                    $"Index '{structuralKey}' is missing in target.", script ?? ""));
//                status = ComparisonStatus.Mismatch;
//            }
//            else if (!inSource && inTarget)
//            {
//                var script = await targetProvider.GetIndexCreateScriptAsync(targetIndex!.Name);
//                subResults.Add(new("Indexes", ComparisonStatus.MissingInSource,
//                    $"Index '{structuralKey}' exists in target but not in source.",
//                    $"-- Optionally drop: DROP INDEX IF EXISTS \"{targetIndex.Name}\";"));
//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        return status;
//    }

//    private static async Task<ComparisonStatus> CompareConstraintsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        TableDefinition source,
//        TableDefinition target,
//        List<ComparisonSubResult> subResults)
//    {
//        var status = ComparisonStatus.Match;

//        var sourceUcByStructure = source.UniqueConstraints
//            .GroupBy(uc => uc.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var targetUcByStructure = target.UniqueConstraints
//            .GroupBy(uc => uc.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
//            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

//        var allKeys = sourceUcByStructure.Keys.Union(targetUcByStructure.Keys, StringComparer.OrdinalIgnoreCase);

//        foreach (var key in allKeys)
//        {
//            var inSource = sourceUcByStructure.TryGetValue(key, out var sourceUc);
//            var inTarget = targetUcByStructure.TryGetValue(key, out var targetUc);

//            if (inSource && !inTarget)
//            {
//                var script = await sourceProvider.GetUniqueConstraintCreateScriptAsync(source.Name, sourceUc!.Name);
//                subResults.Add(new("UniqueConstraints", ComparisonStatus.MissingInTarget,
//                    $"Unique constraint on ({key}) missing in target", script ?? ""));
//                status = ComparisonStatus.Mismatch;
//            }
//            else if (!inSource && inTarget)
//            {
//                var script = await targetProvider.GetUniqueConstraintCreateScriptAsync(target.Name, targetUc!.Name);
//                subResults.Add(new("UniqueConstraints", ComparisonStatus.MissingInSource,
//                    $"Unique constraint on ({key}) missing in source", script ?? ""));
//                status = ComparisonStatus.Mismatch;
//            }
//        }

//        return status;
//    }

//    private string? BuildDiffScript(TableDefinition source, TableDefinition target, List<ComparisonSubResult> subResults, ComparisonStatus status)
//    {
//        if (status == ComparisonStatus.Match) return null;

//        var sb = new StringBuilder();
//        foreach (var sub in subResults.Where(s => !string.IsNullOrWhiteSpace(s.CreateScript)))
//        {
//            sb.AppendLine($"-- {sub.Component}: {sub.Status}");
//            sb.AppendLine(sub.CreateScript!.Trim());
//            sb.AppendLine();
//        }

//        if (sb.Length == 0)
//        {
//            sb.AppendLine("-- SOURCE");
//            sb.AppendLine(source.CreateScript?.Trim());
//            sb.AppendLine("-- TARGET");
//            sb.AppendLine(target.CreateScript?.Trim());
//        }

//        return sb.ToString().Trim();
//    }

//    private void EnhanceComparisonResultWithDiff(ComparisonResult result, string? sourceScript, string? targetScript, string sourceLabel = "Source", string targetLabel = "Target")
//    {
//        result.SourceScript = sourceScript?.Trim();
//        result.TargetScript = targetScript?.Trim();

//        if (result.Status != ComparisonStatus.Match &&
//            (!string.IsNullOrWhiteSpace(sourceScript) || !string.IsNullOrWhiteSpace(targetScript)))
//        {
//            var diffResult = _sqlDiffService.ComputeDiff(sourceScript, targetScript);
//            if (diffResult.HasDifferences)
//            {
//                result.SideBySideDiffHtml = _sqlDiffService.GenerateSideBySideHtml(diffResult, sourceLabel, targetLabel);
//            }
//        }
//    }

//    private async Task<ComparisonResult> HandleTableMissingInSourceAsync(IDatabaseSchemaProvider provider, string tableName)
//    {
//        var table = await provider.GetTableDefinitionAsync(tableName);
//        var subResults = new List<ComparisonSubResult>();

//        if (!string.IsNullOrWhiteSpace(table.CreateScript))
//            subResults.Add(new("CreateScript", ComparisonStatus.MissingInSource, "Create script missing in source", table.CreateScript));

//        var pk = table.PrimaryKeys.FirstOrDefault();
//        if (pk != null)
//        {
//            var pkScript = await provider.GetPrimaryKeyCreateScriptAsync(table.Name);
//            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInSource, "Primary key missing in source", pkScript));
//        }

//        foreach (var col in table.Columns)
//        {
//            subResults.Add(new("Columns", ComparisonStatus.MissingInSource,
//                $"Column '{col.Name}' is missing in source",
//                $"ALTER TABLE \"{table.Name}\" ADD COLUMN \"{col.Name}\" {col.DataType} {(col.IsNullable ? "" : "NOT NULL")};"));
//        }

//        foreach (var fk in table.ForeignKeys)
//        {
//            var fkScript = await provider.GetForeignKeyCreateScriptAsync(table.Name, fk.Name);
//            subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInSource, $"Foreign key '{fk.Name}' is missing in source", fkScript));
//        }

//        foreach (var idx in table.Indexes)
//        {
//            var idxScript = await provider.GetIndexCreateScriptAsync(idx.Name);
//            subResults.Add(new("Indexes", ComparisonStatus.MissingInSource, $"Index '{idx.Name}' is missing in source", idxScript));
//        }

//        return new ComparisonResult
//        {
//            ObjectType = SchemaObjectType.Table,
//            Name = table.Name,
//            Status = ComparisonStatus.MissingInSource,
//            Details = "Exists in target, missing in source",
//            DiffScript = BuildDiffScript(table, table, subResults, ComparisonStatus.MissingInSource),
//            SubResults = subResults
//        };
//    }

//    // --- FUNCTIONS ---
//    private async Task CompareFunctionsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbFunctionDefinition> sourceFunctions,
//        Dictionary<string, DbFunctionDefinition> targetFunctionMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceFunctions.Count;
//        int index = 0;

//        foreach (var kvp in sourceFunctions)
//        {
//            index++;
//            var signature = kvp.Key;
//            var source = kvp.Value;

//            progressLogger?.Invoke(index, total, $"⚙️ Comparing function: {source.Name}", true);

//            if (!targetFunctionMap.TryGetValue(signature, out var target))
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.Function, ComparisonStatus.MissingInTarget, source.Definition));
//                continue;
//            }

//            var sourceDef = await sourceProvider.GetFunctionDefinitionAsync(source.Name, source.Arguments);
//            var targetDef = await targetProvider.GetFunctionDefinitionAsync(target.Name, target.Arguments);

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Function,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Function definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };
//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.Function, ComparisonStatus.Match));
//            }
//        }

//        foreach (var kvp in targetFunctionMap)
//        {
//            if (!sourceFunctions.ContainsKey(kvp.Key))
//            {
//                var target = kvp.Value;
//                results.Add(CreateResult(target.Name, SchemaObjectType.Function, ComparisonStatus.MissingInSource, target.Definition));
//            }
//        }
//    }

//    // --- PROCEDURES ---
//    private async Task CompareProceduresAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbFunctionDefinition> sourceProcedureMap,
//        Dictionary<string, DbFunctionDefinition> targetProcedureMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceProcedureMap.Count;
//        int index = 0;

//        foreach (var kvp in sourceProcedureMap)
//        {
//            var signatureKey = kvp.Key;
//            var source = kvp.Value;
//            progressLogger?.Invoke(++index, total, $"🛠 Comparing procedure: {source.Name}", true);

//            if (!targetProcedureMap.TryGetValue(signatureKey, out var target))
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInTarget, source.Definition));
//                continue;
//            }

//            var sourceDef = await sourceProvider.GetProcedureDefinitionAsync(source.Name, source.Arguments);
//            var targetDef = await targetProvider.GetProcedureDefinitionAsync(target.Name, target.Arguments);

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Procedure,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Procedure definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };
//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.Procedure, ComparisonStatus.Match));
//            }
//        }

//        foreach (var kvp in targetProcedureMap)
//        {
//            if (!sourceProcedureMap.ContainsKey(kvp.Key))
//            {
//                var target = kvp.Value;
//                results.Add(CreateResult(target.Name, SchemaObjectType.Procedure, ComparisonStatus.MissingInSource, target.Definition));
//            }
//        }
//    }

//    // --- VIEWS ---
//    private async Task CompareViewsAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbViewDefinition> sourceViewMap,
//        Dictionary<string, DbViewDefinition> targetViewMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceViewMap.Count;
//        int index = 0;

//        foreach (var kvp in sourceViewMap)
//        {
//            var viewKey = kvp.Key;
//            var source = kvp.Value;
//            progressLogger?.Invoke(++index, total, $"🔍 Comparing view: {source.Name}", true);

//            if (!targetViewMap.TryGetValue(viewKey, out var target))
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.View, ComparisonStatus.MissingInTarget, source.Definition));
//                continue;
//            }

//            var sourceDef = await sourceProvider.GetViewDefinitionAsync(source.Name);
//            var targetDef = await targetProvider.GetViewDefinitionAsync(target.Name);

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.View,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "View definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };
//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.View, ComparisonStatus.Match));
//            }
//        }

//        foreach (var kvp in targetViewMap)
//        {
//            if (!sourceViewMap.ContainsKey(kvp.Key))
//            {
//                var target = kvp.Value;
//                results.Add(CreateResult(target.Name, SchemaObjectType.View, ComparisonStatus.MissingInSource, target.Definition));
//            }
//        }
//    }

//    // --- TRIGGERS ---
//    private async Task CompareTriggersAsync(
//        IDatabaseSchemaProvider sourceProvider,
//        IDatabaseSchemaProvider targetProvider,
//        Dictionary<string, DbTriggerDefinition> sourceTriggerMap,
//        Dictionary<string, DbTriggerDefinition> targetTriggerMap,
//        List<ComparisonResult> results,
//        Action<int, int, string, bool>? progressLogger,
//        ComparisonOptions options)
//    {
//        var sourceDbKind = GetDbKind(sourceProvider);
//        var targetDbKind = GetDbKind(targetProvider);

//        int total = sourceTriggerMap.Count;
//        int index = 0;
//        foreach (var kvp in sourceTriggerMap)
//        {
//            var triggerKey = kvp.Key;
//            var source = kvp.Value;
//            progressLogger?.Invoke(++index, total, $"⏰ Comparing trigger: {source.Name}", true);

//            if (!targetTriggerMap.TryGetValue(triggerKey, out var target))
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.Trigger, ComparisonStatus.MissingInTarget, source.Definition));
//                continue;
//            }

//            var sourceDef = await sourceProvider.GetTriggerDefinitionAsync(source.Name);
//            var targetDef = await targetProvider.GetTriggerDefinitionAsync(target.Name);

//            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
//            {
//                var result = new ComparisonResult
//                {
//                    ObjectType = SchemaObjectType.Trigger,
//                    Name = source.Name,
//                    Status = ComparisonStatus.Mismatch,
//                    Details = "Trigger definition differs",
//                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
//                };
//                EnhanceComparisonResultWithDiff(result, sourceDef, targetDef, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
//                results.Add(result);
//            }
//            else
//            {
//                results.Add(CreateResult(source.Name, SchemaObjectType.Trigger, ComparisonStatus.Match));
//            }
//        }

//        foreach (var kvp in targetTriggerMap)
//        {
//            if (!sourceTriggerMap.ContainsKey(kvp.Key))
//            {
//                var target = kvp.Value;
//                results.Add(CreateResult(target.Name, SchemaObjectType.Trigger, ComparisonStatus.MissingInSource, target.Definition));
//            }
//        }
//    }

//    private ComparisonResult CreateResult(string name, SchemaObjectType type, ComparisonStatus status, string diffScript = "")
//    {
//        var result = new ComparisonResult
//        {
//            ObjectType = type,
//            Name = name,
//            Status = status,
//            DiffScript = diffScript
//        };

//        // For single-script objects (Functions, Procedures, Views, Triggers), the diffScript might be the definition
//        // We need to handle side-by-side diff differently based on the status
//        if (type == SchemaObjectType.Function || type == SchemaObjectType.Procedure || 
//            type == SchemaObjectType.View || type == SchemaObjectType.Trigger)
//        {
//            if (status == ComparisonStatus.MissingInTarget)
//            {
//                // Source exists, target doesn't - show source vs empty
//                EnhanceComparisonResultWithDiff(result, diffScript, null, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
//            }
//            else if (status == ComparisonStatus.MissingInSource)
//            {
//                // Target exists, source doesn't - show empty vs target
//                EnhanceComparisonResultWithDiff(result, null, diffScript, _currentOptions.SourceLabel, _currentOptions.TargetLabel);
//            }
//            // For Match status, no diff needed
//            // For Mismatch, we need to get both source and target - handled elsewhere
//        }

//        return result;
//    }

//    private static bool AreScriptsEqual(string? sourceScript, string? targetScript, string sourceDbKind, string targetDbKind, ComparisonOptions options)
//    {
//        if (options.IgnoreOwnership)
//        {
//            var canonicalizedSource = DefinitionCanonicalizer.CanonicalizeDefinition(sourceScript, sourceDbKind, options);
//            var canonicalizedTarget = DefinitionCanonicalizer.CanonicalizeDefinition(targetScript, targetDbKind, options);
//            return string.Equals(canonicalizedSource, canonicalizedTarget, StringComparison.OrdinalIgnoreCase);
//        }

//        return string.Equals(sourceScript?.Trim(), targetScript?.Trim(), StringComparison.OrdinalIgnoreCase);
//    }

//    // --- PROVIDER-SPECIFIC HELPERS ---

//    /// <summary>
//    /// Detects if the provider is PostgreSQL-based
//    /// </summary>
//    private static bool IsPostgresProvider(IDatabaseSchemaProvider provider)
//    {
//        return provider.GetType().Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase);
//    }

//    /// <summary>
//    /// Detects if the provider is MySQL-based  
//    /// </summary>
//    private static bool IsMySqlProvider(IDatabaseSchemaProvider provider)
//    {
//        return provider.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
//               provider.GetType().Name.Contains("MySQL", StringComparison.OrdinalIgnoreCase);
//    }

//    /// <summary>
//    /// Gets the database kind identifier for canonicalization
//    /// </summary>
//    private static string GetDbKind(IDatabaseSchemaProvider provider)
//    {
//        if (IsPostgresProvider(provider))
//            return "postgres";
//        if (IsMySqlProvider(provider))
//            return "mysql";
//        return "unknown";
//    }

//    /// <summary>
//    /// Enhanced primary key comparison with provider-specific catalog logic
//    /// </summary>
//    private static Task<bool> IsValidPrimaryKeyAsync(IDatabaseSchemaProvider provider, string tableName, PrimaryKeyDefinition pk)
//    {
//        try
//        {
//            if (IsPostgresProvider(provider))
//            {
//                // For Postgres: check if PK is valid, not dropped, handles partitioned tables
//                // This is a placeholder for enhanced catalog queries that would be implemented
//                // in the provider-specific classes
//                return Task.FromResult(pk.Columns.Any());
//            }
//            else if (IsMySqlProvider(provider))
//            {
//                // For MySQL: use information_schema with equivalent normalization
//                return Task.FromResult(pk.Columns.Any());
//            }

//            return Task.FromResult(pk.Columns.Any());
//        }
//        catch
//        {
//            // Fallback to basic validation if provider-specific logic fails
//            return Task.FromResult(pk.Columns.Any());
//        }
//    }

//    /// <summary>
//    /// Enhanced index validation with provider-specific checks
//    /// </summary>
//    private static Task<bool> IsValidIndexAsync(IDatabaseSchemaProvider provider, IndexDefinition index)
//    {
//        try
//        {
//            if (IsPostgresProvider(provider))
//            {
//                // For Postgres: check invalid indexes, inherited tables, partial indexes
//                // Provider should handle catalog joins for robust detection
//                return Task.FromResult(index.Columns.Any());
//            }
//            else if (IsMySqlProvider(provider))
//            {
//                // For MySQL: use information_schema equivalent checks
//                return Task.FromResult(index.Columns.Any());
//            }

//            return Task.FromResult(index.Columns.Any());
//        }
//        catch
//        {
//            // Fallback to basic validation
//            return Task.FromResult(index.Columns.Any());
//        }
//    }

//    /// <summary>
//    /// Checks if a table is a materialized view (should skip PK logic)
//    /// </summary>
//    private static Task<bool> IsMaterializedViewAsync(IDatabaseSchemaProvider provider, string tableName)
//    {
//        try
//        {
//            if (IsPostgresProvider(provider))
//            {
//                // In a real implementation, this would query pg_class for relkind = 'm'
//                // For now, basic heuristic
//                var isMatView = tableName.ToLower().Contains("_mv") || tableName.ToLower().Contains("matview");
//                return Task.FromResult(isMatView);
//            }

//            return Task.FromResult(false);
//        }
//        catch
//        {
//            return Task.FromResult(false);
//        }
//    }
//}
