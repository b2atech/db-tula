
using B2A.DbTula.Core.Models;
using System.Threading.Tasks;

namespace B2a.DbTula.Core.Abstractions;

public interface IDatabaseSchemaProvider
{
    Task<IList<string>> GetTablesAsync();
    Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName);
    Task<IList<PrimaryKeyDefinition>> GetPrimaryKeysAsync(string tableName);
    Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName);
    Task<IList<IndexDefinition>> GetIndexesAsync(string tableName);
    Task<IList<DbFunctionDefinition>> GetFunctionsAsync();
    Task<IList<DbFunctionDefinition>> GetProceduresAsync();
    Task<string> GetFunctionDefinitionAsync(string functionName);
    Task<string> GetProcedureDefinitionAsync(string procedureName);
    Task<string> GetCreateTableScriptAsync(string tableName);
    Task<TableDefinition> GetTableDefinitionAsync(string tableName);
    Task<string?> GetPrimaryKeyCreateScriptAsync(string tableName);
    Task<string?> GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName);
    Task<string?> GetIndexCreateScriptAsync(string indexName);
    Task<IList<DbViewDefinition>> GetViewsAsync();
    Task<string> GetViewDefinitionAsync(string viewName);
    Task<IList<DbTriggerDefinition>> GetTriggersAsync();
    Task<string> GetTriggerDefinitionAsync(string triggerName);

}