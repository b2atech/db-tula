using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Tests;

/// <summary>
/// In-memory schema provider for unit tests — no database required.
/// </summary>
public class MockSchemaProvider : IDatabaseSchemaProvider
{
    public List<string> Tables { get; set; } = new();
    public Dictionary<string, TableDefinition> TableDefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbFunctionDefinition> Functions { get; set; } = new();
    public Dictionary<string, string?> FunctionDefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbFunctionDefinition> Procedures { get; set; } = new();
    public Dictionary<string, string?> ProcedureDefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbViewDefinition> Views { get; set; } = new();
    public Dictionary<string, string?> ViewDefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DbTriggerDefinition> Triggers { get; set; } = new();
    public Dictionary<string, string?> TriggerDefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Sequences { get; set; } = new();
    public Dictionary<string, string?> SequenceDefs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<IList<string>> GetTablesAsync() => Task.FromResult<IList<string>>(Tables);

    public Task<TableDefinition> GetTableDefinitionAsync(string tableName)
    {
        if (TableDefs.TryGetValue(tableName, out var def)) return Task.FromResult(def);
        return Task.FromResult(new TableDefinition { Name = tableName });
    }

    public Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName) =>
        Task.FromResult<IList<ColumnDefinition>>(TableDefs.TryGetValue(tableName, out var d) ? d.Columns : new List<ColumnDefinition>());

    public Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysAsync(string tableName) =>
        Task.FromResult<IList<PrimaryKeyDefinition>>(TableDefs.TryGetValue(tableName, out var d) ? d.PrimaryKeys : new List<PrimaryKeyDefinition>());

    public Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName) =>
        Task.FromResult<IList<ForeignKeyDefinition>>(TableDefs.TryGetValue(tableName, out var d) ? d.ForeignKeys : new List<ForeignKeyDefinition>());

    public Task<IList<IndexDefinition>> GetIndexesAsync(string tableName) =>
        Task.FromResult<IList<IndexDefinition>>(TableDefs.TryGetValue(tableName, out var d) ? d.Indexes : new List<IndexDefinition>());

    public Task<IList<UniqueConstraintDefinition>> GetUniqueConstraintsAsync(string tableName) =>
        Task.FromResult<IList<UniqueConstraintDefinition>>(TableDefs.TryGetValue(tableName, out var d) ? d.UniqueConstraints : new List<UniqueConstraintDefinition>());

    public Task<string?> GetUniqueConstraintCreateScriptAsync(string tableName, string constraintName) =>
        Task.FromResult<string?>(null);

    public Task<IList<string>> GetSequencesAsync() => Task.FromResult<IList<string>>(Sequences);

    public Task<string?> GetSequenceDefinitionAsync(string sequenceName) =>
        Task.FromResult(SequenceDefs.TryGetValue(sequenceName, out var d) ? d : null);

    public Task<IList<DbFunctionDefinition>> GetFunctionsAsync() => Task.FromResult<IList<DbFunctionDefinition>>(Functions);

    public Task<string?> GetFunctionDefinitionAsync(string functionName, string? arguments = null)
    {
        var key = string.IsNullOrWhiteSpace(arguments) ? functionName : $"{functionName}({arguments})";
        if (FunctionDefs.TryGetValue(key, out var d)) return Task.FromResult(d);
        if (FunctionDefs.TryGetValue(functionName, out var fallback)) return Task.FromResult(fallback);
        return Task.FromResult<string?>(null);
    }

    public Task<IList<DbFunctionDefinition>> GetProceduresAsync() => Task.FromResult<IList<DbFunctionDefinition>>(Procedures);

    public Task<string?> GetProcedureDefinitionAsync(string procedureName, string? arguments = null)
    {
        var key = string.IsNullOrWhiteSpace(arguments) ? procedureName : $"{procedureName}({arguments})";
        if (ProcedureDefs.TryGetValue(key, out var d)) return Task.FromResult(d);
        if (ProcedureDefs.TryGetValue(procedureName, out var fallback)) return Task.FromResult(fallback);
        return Task.FromResult<string?>(null);
    }

    public Task<string> GetCreateTableScriptAsync(string tableName) =>
        Task.FromResult(TableDefs.TryGetValue(tableName, out var d) ? d.CreateScript : string.Empty);

    public Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName) => Task.FromResult<string?>(null);
    public Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName) => Task.FromResult<string?>(null);
    public Task<string?> GetIndexCreateScriptAsync(string indexName) => Task.FromResult<string?>(null);

    public Task<IList<DbViewDefinition>> GetViewsAsync() => Task.FromResult<IList<DbViewDefinition>>(Views);

    public Task<string?> GetViewDefinitionAsync(string viewName) =>
        Task.FromResult(ViewDefs.TryGetValue(viewName, out var d) ? d : null);

    public Task<IList<DbTriggerDefinition>> GetTriggersAsync() => Task.FromResult<IList<DbTriggerDefinition>>(Triggers);

    public Task<string?> GetTriggerDefinitionAsync(string triggerName) =>
        Task.FromResult(TriggerDefs.TryGetValue(triggerName, out var d) ? d : null);
}
