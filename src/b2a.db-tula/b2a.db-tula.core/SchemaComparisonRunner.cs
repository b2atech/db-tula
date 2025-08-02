using b2a.db_tula.core.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core
{
    public class SchemaComparisonRunner
    {
        private readonly SchemaFetcher _source;
        private readonly SchemaFetcher _target;
        private readonly SchemaComparer _comparer;
        private readonly LogLevel _logLevel;
        private readonly SchemaSyncer _syncer;

        public SchemaComparisonRunner(SchemaFetcher source, SchemaFetcher target, SchemaSyncer syncer,LogLevel logLevel = LogLevel.Basic)
        {
            _source = source;
            _target = target;
            _syncer = syncer;
            _comparer = new SchemaComparer();
            _logLevel = logLevel;

        }

        public async Task<SchemaComparisonReport> RunComparisonAsync(Action<string, LogLevel> log)
        {
            var report = new SchemaComparisonReport();
            await _source.EnsurePgGetTableDefFunctionExistsAsync();
            await _target.EnsurePgGetTableDefFunctionExistsAsync();
            var sourceTables = await _source.GetTablesAsync();
            var targetTables = await _target.GetTablesAsync();

            // Keep only the first 10 tables ordered by name for testing
            //sourceTables = sourceTables.AsEnumerable()
            //    .OrderBy(row => row["table_name"].ToString())
            //    .Take(10)
            //    .CopyToDataTable();

            //targetTables = targetTables.AsEnumerable()
            //    .OrderBy(row => row["table_name"].ToString())
            //    .Take(10)
            //    .CopyToDataTable();

            report.TableResults = await _comparer.CompareTablesAsync(
                _source, _target, sourceTables, targetTables,
                (i, total, tableName) => Console.WriteLine($"Tables compared: {i}/{total} - {tableName}", LogLevel.Basic)
            );

            foreach (var item in report.TableResults.Where(i => i.Comparison.NeedsSync()))
            {
                var srcDef = item.SourceDefinition;
                var tgtDef = item.DestinationDefinition;
                var syncCommands = _syncer.GenerateSyncCommands(srcDef, tgtDef);
                var comment = BuildComment("Table", item.SourceName, item.Comparison);
                item.SyncScript = JoinCommandsWithComment(comment, syncCommands);
            }



            var sourceFuncs = await _source.GetFunctionsAsync();
            var targetFuncs = await _target.GetFunctionsAsync();
            report.FunctionResults = _comparer.CompareFunctions(_source, _target, sourceFuncs, targetFuncs, 
                (i, total, fnctionName) => Console.WriteLine($"Functions compared: {i}/{total} - {fnctionName}", LogLevel.Basic));

            foreach (var item in report.FunctionResults.Where(i => i.Comparison.NeedsSync()))
            {
                item.SyncScript = await GenerateScriptWithCommentAsync(
                    "Function",
                    item.Comparison,
                    item.SourceName,
                    () => _source.GetFunctionDefinitionAsync(item.SourceName),
                    () => _target.GetFunctionDefinitionAsync(item.DestinationName),
                    _syncer.GenerateSyncCommands
                );
            }

            var sourceProcs = await _source.GetProceduresAsync();
            var targetProcs = await _target.GetProceduresAsync();
            report.ProcedureResults = _comparer.CompareProcedures(_source, _target, sourceProcs, targetProcs, 
                (i, total, procedureName) => Console.WriteLine($"Procedures compared: {i}/{total} - {procedureName}", LogLevel.Basic));

            foreach (var item in report.ProcedureResults.Where(i => i.Comparison.NeedsSync()))
            {
                item.SyncScript = await GenerateScriptWithCommentAsync(
                    "Procedure",
                    item.Comparison,
                    item.SourceName,
                    () => _source.GetProcedureDefinitionAsync(item.SourceName),
                    () => _target.GetProcedureDefinitionAsync(item.DestinationName),
                    _syncer.GenerateSyncCommands
                );
            }


            // ---- Views ----
            var sourceViews = await _source.GetViewsAsync();
            var targetViews = await _target.GetViewsAsync();
            report.ViewResults = _comparer.CompareViews(
                _source, _target, sourceViews, targetViews,
                (i, total, viewName) => log($"Views compared: {i}/{total} - {viewName}", LogLevel.Basic)
            );

            foreach (var item in report.ViewResults.Where(i => i.Comparison.NeedsSync()))
            {
                item.SyncScript = await GenerateScriptWithCommentAsync(
                    "View",
                    item.Comparison,
                    item.SourceName,
                    () => _source.GetViewDefinitionAsync(item.SourceName),
                    () => _target.GetViewDefinitionAsync(item.DestinationName),
                    _syncer.GenerateSyncCommands
                );
            }

            // ---- Triggers ----
            var sourceTriggers = await _source.GetTriggersAsync();
            var targetTriggers = await _target.GetTriggersAsync();
            report.TriggerResults = _comparer.CompareTriggers(
                _source, _target, sourceTriggers, targetTriggers,
                (i, total, trigName) => log($"Triggers compared: {i}/{total} - {trigName}", LogLevel.Basic)
            );

            // Optionally generate sync scripts for triggers
            foreach (var item in report.TriggerResults.Where(i => i.Comparison.NeedsSync()))
            {
                item.SyncScript = await GenerateScriptWithCommentAsync(
                    "Trigger",
                    item.Comparison,
                    item.SourceName,
                    () => _source.GetTriggerDefinitionAsync(item.SourceName),
                    () => _target.GetTriggerDefinitionAsync(item.DestinationName),
                    _syncer.GenerateSyncCommands
                );
            }





            var srcSeqs = (await _source.GetSequencesAsync()).AsEnumerable().Select(r => r["sequence_name"].ToString()).ToList();
            var tgtSeqs = (await _target.GetSequencesAsync()).AsEnumerable().Select(r => r["sequence_name"].ToString()).ToList();
            report.SequenceResults = _comparer.CompareSequences(srcSeqs, tgtSeqs);
            foreach (var seq in report.SequenceResults.Where(s => s.Comparison.NeedsSync()))
            {
                seq.SyncScript = await GenerateScriptWithCommentAsync(
                    "Sequence",
                    seq.Comparison,
                    seq.SourceName,
                    () => _source.GetSequenceDefinitionAsync(seq.SourceName),
                    () => _target.GetSequenceDefinitionAsync(seq.DestinationName),
                    _syncer.GenerateSyncCommands
                );
            }



            return report;
        }
        private async Task<string> GenerateScriptWithCommentAsync<T>(
             string objectType,
             ComparisonType comparisonType,
             string sourceName,
             Func<Task<T>> getSourceDef,
             Func<Task<T>> getTargetDef,
             Func<T, T, string, List<string>> syncCommandGenerator)
                {
                    var srcDef = await getSourceDef();
                    var tgtDef = await getTargetDef();
                    var syncCommands = syncCommandGenerator(srcDef, tgtDef, objectType);

                    var comment = comparisonType switch
                    {
                        ComparisonType.MissingInTarget => $"-- {objectType} missing in target: \"{sourceName}\"",
                        ComparisonType.MissingInSource => $"-- {objectType} missing in source: \"{sourceName}\"",
                        ComparisonType.ExtraInTarget => $"-- {objectType} extra in target: \"{sourceName}\"",
                        ComparisonType.Changed => $"-- {objectType} differs: \"{sourceName}\"",
                        _ => $"-- {objectType} matches: \"{sourceName}\""
                    };

                    return $"{comment}\n{string.Join("\n", syncCommands)}";
                }



        private string BuildComment(string objectType, string objectName, ComparisonType comparison) =>
                     comparison switch
                     {
                         ComparisonType.MissingInTarget => $"-- {objectType} missing in target: \"{objectName}\"",
                         ComparisonType.MissingInSource => $"-- {objectType} missing in source: \"{objectName}\"",
                         ComparisonType.ExtraInTarget => $"-- {objectType} extra in target: \"{objectName}\"",
                         ComparisonType.Changed => $"-- {objectType} differs: \"{objectName}\"",
                         _ => $"-- {objectType} matches: \"{objectName}\""
                     };

        private string JoinCommandsWithComment(string comment, List<string> syncCommands) =>
                        $"{comment}\n{string.Join("\n", syncCommands)}";

        

    }
}
