
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;

namespace B2a.DbTula.Core.Abstractions;

public interface IDatabaseSchemaProvider
{
    DbProviderKind ProviderKind { get; }

    Task<IList<string>> GetTablesAsync();
    Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName);
    Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysAsync(string tableName);
    Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName);
    Task<IList<IndexDefinition>> GetIndexesAsync(string tableName);
    Task<IList<UniqueConstraintDefinition>> GetUniqueConstraintsAsync(string tableName);
    Task<string?> GetUniqueConstraintCreateScriptAsync(string tableName, string constraintName);
    Task<IList<string>> GetSequencesAsync();
    Task<string?> GetSequenceDefinitionAsync(string sequenceName);
    Task<IList<DbFunctionDefinition>> GetFunctionsAsync();
    Task<IList<DbFunctionDefinition>> GetProceduresAsync();
    Task<string?> GetFunctionDefinitionAsync(string functionName, string? arguments = null);
    Task<string?> GetProcedureDefinitionAsync(string procedureName, string? arguments = null);
    Task<string?> GetCreateTableScriptAsync(string tableName);
    Task<TableDefinition> GetTableDefinitionAsync(string tableName);
    Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName);
    Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName);
    Task<string?> GetIndexCreateScriptAsync(string indexName);
    Task<IList<DbViewDefinition>> GetViewsAsync();
    Task<string?> GetViewDefinitionAsync(string viewName);
    Task<IList<DbTriggerDefinition>> GetTriggersAsync();
    Task<string?> GetTriggerDefinitionAsync(string triggerName);
    Task<HashSet<string>> GetMaterializedViewNamesAsync();
}
