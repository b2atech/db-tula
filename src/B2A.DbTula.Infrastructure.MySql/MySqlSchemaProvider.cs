using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Infrastructure.MySql
{
    public class MySqlSchemaProvider : IDatabaseSchemaProvider
    {
        //private readonly SchemaFetcher _fetcher;
        //private readonly DatabaseConnection _connection;
        public MySqlSchemaProvider(
            string connectionString,
            Action<int, int, string, bool> logger,
            bool verbose,
            LogLevel logLevel = LogLevel.Basic)
        {
            //_connection = new DatabaseConnection(connectionString, logger, verbose, logLevel);
            //_fetcher = new SchemaFetcher(_connection, logger, verbose, logLevel);
        }
        Task<IList<ColumnDefinition>> IDatabaseSchemaProvider.GetColumnsAsync(string tableName)
        {
            throw new NotImplementedException();
        }

        Task<string> IDatabaseSchemaProvider.GetCreateTableScriptAsync(string tableName)
        {
            throw new NotImplementedException();
        }

        Task<string?> IDatabaseSchemaProvider.GetForeignKeyCreateScriptAsync(string tableName, string foreignKeyName)
        {
            throw new NotImplementedException();
        }

        Task<IList<ForeignKeyDefinition>> IDatabaseSchemaProvider.GetForeignKeysAsync(string tableName)
        {
            throw new NotImplementedException();
        }

        Task<string> IDatabaseSchemaProvider.GetFunctionDefinitionAsync(string functionName)
        {
            throw new NotImplementedException();
        }

        Task<IList<DbFunctionDefinition>> IDatabaseSchemaProvider.GetFunctionsAsync()
        {
            throw new NotImplementedException();
        }

        Task<string?> IDatabaseSchemaProvider.GetIndexCreateScriptAsync(string indexName)
        {
            throw new NotImplementedException();
        }

        Task<IList<IndexDefinition>> IDatabaseSchemaProvider.GetIndexesAsync(string tableName)
        {
            throw new NotImplementedException();
        }

        Task<string?> IDatabaseSchemaProvider.GetPrimaryKeyCreateScriptAsync(string tableName)
        {
            throw new NotImplementedException();
        }

        Task<IList<PrimaryKeyDefinition>> IDatabaseSchemaProvider.GetPrimaryKeysAsync(string tableName)
        {
            throw new NotImplementedException();
        }

        Task<string> IDatabaseSchemaProvider.GetProcedureDefinitionAsync(string procedureName)
        {
            throw new NotImplementedException();
        }

        Task<IList<DbFunctionDefinition>> IDatabaseSchemaProvider.GetProceduresAsync()
        {
            throw new NotImplementedException();
        }

        Task<TableDefinition> IDatabaseSchemaProvider.GetTableDefinitionAsync(string tableName)
        {
            throw new NotImplementedException();
        }

        Task<IList<string>> IDatabaseSchemaProvider.GetTablesAsync()
        {
            throw new NotImplementedException();
        }
    }
}
