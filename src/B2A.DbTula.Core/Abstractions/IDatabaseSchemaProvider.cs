
using B2A.DbTula.Core.Models;

namespace B2a.DbTula.Core.Abstractions;
 
public interface IDatabaseSchemaProvider
{
    // -----------------------------
    // Tables
    // -----------------------------
    Task<IList<string>> GetTablesAsync();

    Task<TableDefinition> GetTableDefinitionAsync(string tableName);

    Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName);

    Task<string> GetCreateTableScriptAsync(string tableName);


    // -----------------------------
    // Primary Keys
    // -----------------------------
    Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysAsync(string tableName);

    Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName);


    // -----------------------------
    // Foreign Keys
    // -----------------------------
    Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName);

    Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName);


    // -----------------------------
    // Indexes
    // -----------------------------
    Task<IList<IndexDefinition>> GetIndexesAsync(string tableName);

    Task<string?> GetIndexCreateScriptAsync(string indexName);


    // -----------------------------
    // Unique Constraints
    // Optional for providers.
    // Providers can override when supported.
    // -----------------------------
    Task<IList<UniqueConstraintDefinition>> GetUniqueConstraintsAsync(string tableName)
    {
        return Task.FromResult<IList<UniqueConstraintDefinition>>(
            new List<UniqueConstraintDefinition>());
    }

    Task<string?> GetUniqueConstraintCreateScriptAsync(string tableName, string constraintName)
    {
        return Task.FromResult<string?>(null);
    }


    // -----------------------------
    // Sequences
    // Optional for providers.
    // Providers can override when supported.
    // -----------------------------
    Task<IList<string>> GetSequencesAsync()
    {
        return Task.FromResult<IList<string>>(
            new List<string>());
    }

    Task<string?> GetSequenceDefinitionAsync(string sequenceName)
    {
        return Task.FromResult<string?>(null);
    }


    // -----------------------------
    // Functions
    // -----------------------------
    Task<IList<DbFunctionDefinition>> GetFunctionsAsync();

    Task<string?> GetFunctionDefinitionAsync(string functionName)
    {
        return Task.FromResult<string?>(null);
    }

    Task<string?> GetFunctionDefinitionAsync(string functionName, string? arguments)
    {
        return GetFunctionDefinitionAsync(functionName);
    }


    // -----------------------------
    // Procedures
    // -----------------------------
    Task<IList<DbFunctionDefinition>> GetProceduresAsync();

    Task<string?> GetProcedureDefinitionAsync(string procedureName)
    {
        return Task.FromResult<string?>(null);
    }

    Task<string?> GetProcedureDefinitionAsync(string procedureName, string? arguments)
    {
        return GetProcedureDefinitionAsync(procedureName);
    }


    // -----------------------------
    // Views
    // -----------------------------
    Task<IList<DbViewDefinition>> GetViewsAsync();

    Task<string?> GetViewDefinitionAsync(string viewName);


    // -----------------------------
    // Triggers
    // -----------------------------
    Task<IList<DbTriggerDefinition>> GetTriggersAsync();

    Task<string?> GetTriggerDefinitionAsync(string triggerName);
}
//public interface IDatabaseSchemaProvider
//{
//    Task<IList<string>> GetTablesAsync();
//    Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName);
//    Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysAsync(string tableName);
//    Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName);
//    Task<IList<IndexDefinition>> GetIndexesAsync(string tableName);
//    Task<IList<UniqueConstraintDefinition>> GetUniqueConstraintsAsync(string tableName);
//    Task<string?> GetUniqueConstraintCreateScriptAsync(string tableName, string constraintName);
//    Task<IList<string>> GetSequencesAsync();
//    Task<string?> GetSequenceDefinitionAsync(string sequenceName);
//    Task<IList<DbFunctionDefinition>> GetFunctionsAsync();
//    Task<IList<DbFunctionDefinition>> GetProceduresAsync();
//    Task<string?> GetFunctionDefinitionAsync(string functionName, string? arguments = null);
//    Task<string?> GetProcedureDefinitionAsync(string procedureName, string? arguments = null);
//    Task<string> GetCreateTableScriptAsync(string tableName);
//    Task<TableDefinition> GetTableDefinitionAsync(string tableName);
//    Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName);
//    Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName);
//    Task<string?> GetIndexCreateScriptAsync(string indexName);
//    Task<IList<DbViewDefinition>> GetViewsAsync();
//    Task<string?> GetViewDefinitionAsync(string viewName);
//    Task<IList<DbTriggerDefinition>> GetTriggersAsync();
//    Task<string?> GetTriggerDefinitionAsync(string triggerName);
//}
