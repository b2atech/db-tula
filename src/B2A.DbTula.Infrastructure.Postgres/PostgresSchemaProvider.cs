using B2a.DbTula.Core;
using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using B2A.DbTula.Infrastructure.Postgres;
using Npgsql;
using Serilog;
using System.Data.Common;

namespace B2a.DbTula.Infrastructure.Postgres;


    public class PostgresSchemaProvider : IDatabaseSchemaProvider
    {
    private readonly SchemaFetcher _fetcher;
    private readonly DatabaseConnection _connection;
    public PostgresSchemaProvider(
        string connectionString,
        Action<string> log,
        bool verbose,
        LogLevel logLevel = LogLevel.Basic)
    {
        _connection = new DatabaseConnection(connectionString, log, verbose, logLevel);
        _fetcher = new SchemaFetcher(_connection, log, verbose, logLevel);
    }


    public async Task<IList<TableDefinition>> GetTablesAsync()
        {
            var tableRows = await _fetcher.GetTablesAsync();
            var tables = new List<TableDefinition>();

            foreach (System.Data.DataRow row in tableRows.Rows)
            {
                var tableName = row["table_name"].ToString();
                if (!string.IsNullOrEmpty(tableName))
                {
                    var definition = await _fetcher.GetTableDefinitionAsync(tableName);
                    tables.Add(definition);
                }
            }

            return tables;
        }

        public async Task<IList<ColumnDefinition>> GetColumnsAsync(string tableName)
        {
            return await _fetcher.GetColumnsListAsync(tableName);
        }

        public async Task<IList<string>> GetPrimaryKeysAsync(string tableName)
        {
            return await _fetcher.GetPrimaryKeysListAsync(tableName);
        }

        public async Task<IList<ForeignKeyDefinition>> GetForeignKeysAsync(string tableName)
        {
            return await _fetcher.GetForeignKeysListAsync(tableName);
        }

        public async Task<IList<IndexDefinition>> GetIndexesAsync(string tableName)
        {
            return await _fetcher.GetIndexesListAsync(tableName);
        }

        public async Task<IList<DbFunctionDefinition>> GetFunctionsAsync()
        {
            var table = await _fetcher.GetFunctionsAsync();
            return ParseFunctionOrProcedureList(table);
        }

        public async Task<IList<DbFunctionDefinition>> GetProceduresAsync()
        {
            var table = await _fetcher.GetProceduresAsync();
            return ParseFunctionOrProcedureList(table);
        }

        public async Task<string> GetFunctionDefinitionAsync(string functionName)
        {
            return await _fetcher.GetFunctionDefinitionAsync(functionName);
        }

        public async Task<string> GetProcedureDefinitionAsync(string procedureName)
        {
            return await _fetcher.GetProcedureDefinitionAsync(procedureName);
        }

        public async Task<string> GetCreateTableScriptAsync(string tableName)
        {
            return await _fetcher.GetCreateTableScriptAsync(tableName);
        }

        public async Task<TableDefinition> GetTableDefinitionAsync(string tableName)
        {
            return await _fetcher.GetTableDefinitionAsync(tableName);
        }

        private List<DbFunctionDefinition> ParseFunctionOrProcedureList(System.Data.DataTable table)
        {
            var list = new List<DbFunctionDefinition>();

            foreach (System.Data.DataRow row in table.Rows)
            {
                list.Add(new DbFunctionDefinition
                {
                    Name = row["routine_name"].ToString(),
                    Arguments = row["arguments"]?.ToString()
                });
            }

            return list;
        }
    }

