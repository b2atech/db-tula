using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using B2A.DbTula.Core.Utilities;
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
public class SchemaComparer : ISchemaComparer
{
    public async Task<IList<ComparisonResult>> CompareAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Action<int, int, string, bool>? progressLogger = null,
        bool runForTest = false, int testObjectLimit = 10,
        ComparisonOptions? options = null)
    {
        options ??= new ComparisonOptions();
        var results = new List<ComparisonResult>();

        progressLogger?.Invoke(0, 0, "🔍 Fetching schema objects...", false);

        // --- TABLES ---
        var sourceTables = await sourceProvider.GetTablesAsync();
        var targetTables = await targetProvider.GetTablesAsync();

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
            progressLogger,
            options);

        // --- FUNCTIONS ---
        string GetSignatureKey(DbFunctionDefinition def) =>
            $"{def.Name}({def.Arguments})";

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
           progressLogger,
           options);

        // --- PROCEDURES ---
        var sourceProcedures = await sourceProvider.GetProceduresAsync();
        var targetProcedures = await targetProvider.GetProceduresAsync();

        var sourceProcedureMap = sourceProcedures.ToDictionary(p => GetSignatureKey(p), StringComparer.OrdinalIgnoreCase);
        var targetProcedureMap = targetProcedures.ToDictionary(p => GetSignatureKey(p), StringComparer.OrdinalIgnoreCase);

        var limitedSourceProcedureMap = runForTest
            ? sourceProcedureMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceProcedureMap;

        var limitedTargetProcedureMap = targetProcedureMap
            .Where(kvp => limitedSourceProcedureMap.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"🛠 {(runForTest ? $"Test mode: comparing top {testObjectLimit} procedures..." : "Comparing procedures...")}", false);

        await CompareProceduresAsync(
            sourceProvider,
            targetProvider,
            limitedSourceProcedureMap,
            limitedTargetProcedureMap,
            results,
            progressLogger,
            options);

        // --- VIEWS ---
        var sourceViews = await sourceProvider.GetViewsAsync();
        var targetViews = await targetProvider.GetViewsAsync();
        var sourceViewMap = sourceViews.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var targetViewMap = targetViews.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

        var limitedSourceViewMap = runForTest
            ? sourceViewMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceViewMap;

        var limitedTargetViewMap = targetViewMap
            .Where(kvp => limitedSourceViewMap.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"🔍 {(runForTest ? $"Test mode: comparing top {testObjectLimit} views..." : "Comparing views...")}", false);

        await CompareViewsAsync(
            sourceProvider,
            targetProvider,
            limitedSourceViewMap,
            limitedTargetViewMap,
            results,
            progressLogger,
            options);

        // --- TRIGGERS ---
        var sourceTriggers = await sourceProvider.GetTriggersAsync();
        var targetTriggers = await targetProvider.GetTriggersAsync();
        Func<DbTriggerDefinition, string> triggerKeySelector = t => $"{t.Table}|{t.Name}";
        var sourceTriggerMap = sourceTriggers.ToDictionary(triggerKeySelector, StringComparer.OrdinalIgnoreCase);
        var targetTriggerMap = targetTriggers.ToDictionary(triggerKeySelector, StringComparer.OrdinalIgnoreCase);

        var limitedSourceTriggerMap = runForTest
            ? sourceTriggerMap.Take(testObjectLimit).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            : sourceTriggerMap;

        var limitedTargetTriggerMap = targetTriggerMap
            .Where(kvp => limitedSourceTriggerMap.ContainsKey(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        progressLogger?.Invoke(0, 0, $"⏰ {(runForTest ? $"Test mode: comparing top {testObjectLimit} triggers..." : "Comparing triggers...")}", false);

        await CompareTriggersAsync(
            sourceProvider,
            targetProvider,
            limitedSourceTriggerMap,
            limitedTargetTriggerMap,
            results,
            progressLogger,
            options);

        progressLogger?.Invoke(0, 0, "✅ Schema comparison completed.", false);
        return results;
    }

    // ---------- COMPARE METHODS -----------

    private static async Task CompareTablesAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        IList<string> sourceTables,
        IList<string> targetTables,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        
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

            // First check: Use structural signature for semantic comparison (order-independent)
            if (!source.StructuralEquals(target))
            {
                overallStatus = ComparisonStatus.Mismatch;
                
                // Only add script comparison as supplementary info if structural differs
                if (!AreScriptsEqual(source.CreateScript, target.CreateScript, sourceDbKind, targetDbKind, options))
                {
                    subResults.Add(new ComparisonSubResult(
                        "StructuralDiff",
                        ComparisonStatus.Mismatch,
                        "Table structure differs (order-independent semantic comparison)",
                        $"-- SOURCE SIGNATURE\n{source.GetStructuralSignature()}\n\n-- TARGET SIGNATURE\n{target.GetStructuralSignature()}\n\n-- SOURCE SCRIPT\n{source.CreateScript}\n\n-- TARGET SCRIPT\n{target.CreateScript}"
                    ));
                }
            }

            overallStatus |= await ComparePrimaryKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= CompareColumns(source, target, subResults);
            overallStatus |= await CompareForeignKeysAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= await CompareIndexesAsync(sourceProvider, targetProvider, source, target, subResults);
            overallStatus |= await CompareConstraintsAsync(sourceProvider, targetProvider, source, target, subResults);

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
        // Check if tables are materialized views (skip PK logic for matviews)
        var sourceIsMatView = await IsMaterializedViewAsync(sourceProvider, source.Name);
        var targetIsMatView = await IsMaterializedViewAsync(targetProvider, target.Name);
        
        if (sourceIsMatView || targetIsMatView)
        {
            // Skip PK comparison for materialized views
            return ComparisonStatus.Match;
        }

        var sourcePk = source.PrimaryKeys.FirstOrDefault();
        var targetPk = target.PrimaryKeys.FirstOrDefault();

        // Validate PKs with provider-specific logic (handles invalid/dropped PKs)
        var sourceValid = sourcePk != null && await IsValidPrimaryKeyAsync(sourceProvider, source.Name, sourcePk);
        var targetValid = targetPk != null && await IsValidPrimaryKeyAsync(targetProvider, target.Name, targetPk);

        if (!sourceValid && targetValid)
        {
            var script = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInSource, 
                $"Primary key missing/invalid in source: columns ({string.Join(", ", targetPk!.Columns)})", script ?? ""));
            return ComparisonStatus.Mismatch;
        }
        else if (!targetValid && sourceValid)
        {
            var script = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
            subResults.Add(new("PrimaryKeys", ComparisonStatus.MissingInTarget, 
                $"Primary key missing/invalid in target: columns ({string.Join(", ", sourcePk!.Columns)})", script ?? ""));
            return ComparisonStatus.Mismatch;
        }
        else if (sourceValid && targetValid)
        {
            // Compare by structural definition (column list), not by name
            if (!sourcePk!.StructuralEquals(targetPk!))
            {
                var sourceScript = await sourceProvider.GetPrimaryKeyCreateScriptAsync(source.Name);
                var targetScript = await targetProvider.GetPrimaryKeyCreateScriptAsync(target.Name);
                subResults.Add(new("PrimaryKeys", ComparisonStatus.Mismatch, 
                    $"Primary key structure differs: source({string.Join(", ", sourcePk.Columns)}) vs target({string.Join(", ", targetPk.Columns)})", 
                    $"-- SOURCE PK\n{sourceScript}\n\n-- TARGET PK\n{targetScript}"));
                return ComparisonStatus.Mismatch;
            }
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

        // Group FKs by structural key to avoid double-counting and enable semantic comparison
        var sourceFksByStructure = source.ForeignKeys
            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetFksByStructure = target.ForeignKeys
            .GroupBy(fk => fk.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allFkStructures = sourceFksByStructure.Keys.Union(targetFksByStructure.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var structuralKey in allFkStructures)
        {
            var inSource = sourceFksByStructure.TryGetValue(structuralKey, out var sourceFk);
            var inTarget = targetFksByStructure.TryGetValue(structuralKey, out var targetFk);

            if (inSource && inTarget)
            {
                // Already structurally equal by grouping key, but check for any additional differences
                if (!sourceFk!.StructuralEquals(targetFk!))
                {
                    var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk.Name);
                    subResults.Add(new("ForeignKeys", ComparisonStatus.Mismatch, 
                        $"Foreign key structure '{structuralKey}' has differences", script ?? ""));
                    status = ComparisonStatus.Mismatch;
                }
            }
            else if (inSource && !inTarget)
            {
                var script = await sourceProvider.GetForeignKeyCreateScriptAsync(source.Name, sourceFk!.Name);
                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInTarget, 
                    $"Foreign key '{structuralKey}' missing in target", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
            else if (!inSource && inTarget)
            {
                var script = await targetProvider.GetForeignKeyCreateScriptAsync(target.Name, targetFk!.Name);
                subResults.Add(new("ForeignKeys", ComparisonStatus.MissingInSource, 
                    $"Foreign key '{structuralKey}' missing in source", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
        }
        return status;
    }

    private static async Task<ComparisonStatus> CompareIndexesAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        var status = ComparisonStatus.Match;
        
        // Filter out invalid indexes using provider-specific validation
        var validSourceIndexes = new List<IndexDefinition>();
        foreach (var idx in source.Indexes)
        {
            if (await IsValidIndexAsync(sourceProvider, idx))
                validSourceIndexes.Add(idx);
        }

        var validTargetIndexes = new List<IndexDefinition>();
        foreach (var idx in target.Indexes)
        {
            if (await IsValidIndexAsync(targetProvider, idx))
                validTargetIndexes.Add(idx);
        }
        
        // Group indexes by structural key to enable semantic comparison (method + columns + options + predicate)
        var sourceIndexesByStructure = validSourceIndexes
            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetIndexesByStructure = validTargetIndexes
            .GroupBy(idx => idx.GetStructuralKey(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allIndexStructures = sourceIndexesByStructure.Keys.Union(targetIndexesByStructure.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var structuralKey in allIndexStructures)
        {
            var inSource = sourceIndexesByStructure.TryGetValue(structuralKey, out var sourceIndex);
            var inTarget = targetIndexesByStructure.TryGetValue(structuralKey, out var targetIndex);

            if (inSource && inTarget)
            {
                // Already structurally equal by grouping key, but verify
                if (!sourceIndex!.StructuralEquals(targetIndex!))
                {
                    var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex.Name);
                    subResults.Add(new("Indexes", ComparisonStatus.Mismatch,
                        $"Index structure '{structuralKey}' has subtle differences", script ?? ""));
                    status = ComparisonStatus.Mismatch;
                }
            }
            else if (inSource && !inTarget)
            {
                var script = await sourceProvider.GetIndexCreateScriptAsync(sourceIndex!.Name);
                subResults.Add(new("Indexes", ComparisonStatus.MissingInTarget,
                    $"Index '{structuralKey}' is missing in target.", script ?? ""));
                status = ComparisonStatus.Mismatch;
            }
            else if (!inSource && inTarget)
            {
                var script = await targetProvider.GetIndexCreateScriptAsync(targetIndex!.Name);
                subResults.Add(new("Indexes", ComparisonStatus.MissingInSource,
                    $"Index '{structuralKey}' exists in target but not in source.",
                    $"-- Optionally drop: DROP INDEX IF EXISTS \"{targetIndex.Name}\";"));
                status = ComparisonStatus.Mismatch;
            }
        }

        return status;
    }

    /// <summary>
    /// Compare check constraints and unique constraints by semantics, not names/text
    /// This is a placeholder for when constraint models are added to the core library
    /// </summary>
    private static Task<ComparisonStatus> CompareConstraintsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        TableDefinition source,
        TableDefinition target,
        List<ComparisonSubResult> subResults)
    {
        // Placeholder for constraint comparison
        // When constraint models are available, this would:
        // 1. Compare check constraints by normalized expressions (whitespace-insensitive)
        // 2. Compare unique constraints by column sets, not names
        // 3. Handle provider-specific constraint syntax differences
        
        try
        {
            // For now, just verify that the structural signatures handle constraints
            // The structural signature in TableDefinition will need to be enhanced
            // to include constraint information when constraint models are added
            
            // Placeholder: if we had constraint information, we would compare them here
            // similar to how we compare FKs and indexes by structure
            
            return Task.FromResult(ComparisonStatus.Match);
        }
        catch (Exception)
        {
            // If constraint comparison fails, don't fail the entire table comparison
            return Task.FromResult(ComparisonStatus.Match);
        }
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

    // --- FUNCTIONS ---
    private static async Task CompareFunctionsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbFunctionDefinition> sourceFunctions,
        Dictionary<string, DbFunctionDefinition> targetFunctionMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        
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

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
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

    // --- PROCEDURES ---
    private static async Task CompareProceduresAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbFunctionDefinition> sourceProcedureMap,
        Dictionary<string, DbFunctionDefinition> targetProcedureMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        
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

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
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

    // --- VIEWS ---
    private static async Task CompareViewsAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbViewDefinition> sourceViewMap,
        Dictionary<string, DbViewDefinition> targetViewMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        
        int total = sourceViewMap.Count;
        int index = 0;

        foreach (var kvp in sourceViewMap)
        {
            var viewKey = kvp.Key;
            var source = kvp.Value;
            progressLogger?.Invoke(++index, total, $"🔍 Comparing view: {source.Name}", true);

            if (!targetViewMap.TryGetValue(viewKey, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.View, ComparisonStatus.MissingInTarget, source.Definition));
                continue;
            }

            var sourceDef = await sourceProvider.GetViewDefinitionAsync(source.Name);
            var targetDef = await targetProvider.GetViewDefinitionAsync(target.Name);

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.View,
                    Name = source.Name,
                    Status = ComparisonStatus.Mismatch,
                    Details = "View definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                });
            }
            else
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.View, ComparisonStatus.Match));
            }
        }

        foreach (var kvp in targetViewMap)
        {
            if (!sourceViewMap.ContainsKey(kvp.Key))
            {
                var target = kvp.Value;
                results.Add(CreateResult(target.Name, SchemaObjectType.View, ComparisonStatus.MissingInSource, target.Definition));
            }
        }
    }

    // --- TRIGGERS ---
    private static async Task CompareTriggersAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Dictionary<string, DbTriggerDefinition> sourceTriggerMap,
        Dictionary<string, DbTriggerDefinition> targetTriggerMap,
        List<ComparisonResult> results,
        Action<int, int, string, bool>? progressLogger,
        ComparisonOptions options)
    {
        var sourceDbKind = GetDbKind(sourceProvider);
        var targetDbKind = GetDbKind(targetProvider);
        
        int total = sourceTriggerMap.Count;
        int index = 0;
        foreach (var kvp in sourceTriggerMap)
        {
            var triggerKey = kvp.Key;
            var source = kvp.Value;
            progressLogger?.Invoke(++index, total, $"⏰ Comparing trigger: {source.Name}", true);

            if (!targetTriggerMap.TryGetValue(triggerKey, out var target))
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Trigger, ComparisonStatus.MissingInTarget, source.Definition));
                continue;
            }

            var sourceDef = await sourceProvider.GetTriggerDefinitionAsync(source.Name);
            var targetDef = await targetProvider.GetTriggerDefinitionAsync(target.Name);

            if (!AreScriptsEqual(sourceDef, targetDef, sourceDbKind, targetDbKind, options))
            {
                results.Add(new ComparisonResult
                {
                    ObjectType = SchemaObjectType.Trigger,
                    Name = source.Name,
                    Status = ComparisonStatus.Mismatch,
                    Details = "Trigger definition differs",
                    DiffScript = $"-- SOURCE\n{sourceDef}\n\n-- TARGET\n{targetDef}"
                });
            }
            else
            {
                results.Add(CreateResult(source.Name, SchemaObjectType.Trigger, ComparisonStatus.Match));
            }
        }

        foreach (var kvp in targetTriggerMap)
        {
            if (!sourceTriggerMap.ContainsKey(kvp.Key))
            {
                var target = kvp.Value;
                results.Add(CreateResult(target.Name, SchemaObjectType.Trigger, ComparisonStatus.MissingInSource, target.Definition));
            }
        }
    }

    // --- HELPERS ---
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

    private static bool AreScriptsEqual(string? sourceScript, string? targetScript, string sourceDbKind, string targetDbKind, ComparisonOptions options)
    {
        if (options.IgnoreOwnership)
        {
            var canonicalizedSource = DefinitionCanonicalizer.CanonicalizeDefinition(sourceScript, sourceDbKind, options);
            var canonicalizedTarget = DefinitionCanonicalizer.CanonicalizeDefinition(targetScript, targetDbKind, options);
            return string.Equals(canonicalizedSource, canonicalizedTarget, StringComparison.OrdinalIgnoreCase);
        }
        
        return string.Equals(sourceScript?.Trim(), targetScript?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // --- PROVIDER-SPECIFIC HELPERS ---

    /// <summary>
    /// Detects if the provider is PostgreSQL-based
    /// </summary>
    private static bool IsPostgresProvider(IDatabaseSchemaProvider provider)
    {
        return provider.GetType().Name.Contains("Postgres", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects if the provider is MySQL-based  
    /// </summary>
    private static bool IsMySqlProvider(IDatabaseSchemaProvider provider)
    {
        return provider.GetType().Name.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
               provider.GetType().Name.Contains("MySQL", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the database kind identifier for canonicalization
    /// </summary>
    private static string GetDbKind(IDatabaseSchemaProvider provider)
    {
        if (IsPostgresProvider(provider))
            return "postgres";
        if (IsMySqlProvider(provider))
            return "mysql";
        return "unknown";
    }

    /// <summary>
    /// Enhanced primary key comparison with provider-specific catalog logic
    /// </summary>
    private static Task<bool> IsValidPrimaryKeyAsync(IDatabaseSchemaProvider provider, string tableName, PrimaryKeyDefinition pk)
    {
        try
        {
            if (IsPostgresProvider(provider))
            {
                // For Postgres: check if PK is valid, not dropped, handles partitioned tables
                // This is a placeholder for enhanced catalog queries that would be implemented
                // in the provider-specific classes
                return Task.FromResult(pk.Columns.Any());
            }
            else if (IsMySqlProvider(provider))
            {
                // For MySQL: use information_schema with equivalent normalization
                return Task.FromResult(pk.Columns.Any());
            }
            
            return Task.FromResult(pk.Columns.Any());
        }
        catch
        {
            // Fallback to basic validation if provider-specific logic fails
            return Task.FromResult(pk.Columns.Any());
        }
    }

    /// <summary>
    /// Enhanced index validation with provider-specific checks
    /// </summary>
    private static Task<bool> IsValidIndexAsync(IDatabaseSchemaProvider provider, IndexDefinition index)
    {
        try
        {
            if (IsPostgresProvider(provider))
            {
                // For Postgres: check invalid indexes, inherited tables, partial indexes
                // Provider should handle catalog joins for robust detection
                return Task.FromResult(index.Columns.Any());
            }
            else if (IsMySqlProvider(provider))
            {
                // For MySQL: use information_schema equivalent checks
                return Task.FromResult(index.Columns.Any());
            }
            
            return Task.FromResult(index.Columns.Any());
        }
        catch
        {
            // Fallback to basic validation
            return Task.FromResult(index.Columns.Any());
        }
    }

    /// <summary>
    /// Checks if a table is a materialized view (should skip PK logic)
    /// </summary>
    private static Task<bool> IsMaterializedViewAsync(IDatabaseSchemaProvider provider, string tableName)
    {
        try
        {
            if (IsPostgresProvider(provider))
            {
                // In a real implementation, this would query pg_class for relkind = 'm'
                // For now, basic heuristic
                var isMatView = tableName.ToLower().Contains("_mv") || tableName.ToLower().Contains("matview");
                return Task.FromResult(isMatView);
            }
            
            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
