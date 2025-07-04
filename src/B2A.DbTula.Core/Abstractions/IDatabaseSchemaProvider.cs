
using B2A.DbTula.Core.Models;

namespace B2a.DbTula.Core.Abstractions;

public interface IDatabaseSchemaProvider
{
    Task<IList<TableDefinition>> GetTablesAsync();
    Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName);
    Task<IList<string>> GetPrimaryKeysAsync(string tableName);
    Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName);
    Task<IList<IndexDefinition>> GetIndexesAsync(string tableName);
    Task<IList<DbFunctionDefinition>> GetFunctionsAsync();
    Task<IList<DbFunctionDefinition>> GetProceduresAsync();
    Task<string> GetFunctionDefinitionAsync(string functionName);
    Task<string> GetProcedureDefinitionAsync(string procedureName);
    Task<string> GetCreateTableScriptAsync(string tableName);
    Task<TableDefinition> GetTableDefinitionAsync(string tableName);
}