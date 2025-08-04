using B2A.DbTula.Core.Abstractions;
using Npgsql;
using System.Data;
using Microsoft.Extensions.Logging;

namespace B2A.DbTula.Infrastructure.Postgres
{
    public class PostgresDatabaseConnection : IDatabaseConnection
    {
        private readonly string _connectionString;
        private readonly ILogger<PostgresDatabaseConnection> _logger;

        public PostgresDatabaseConnection(string connectionString, ILogger<PostgresDatabaseConnection> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public DataTable ExecuteQuery(string query)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                using var command = new NpgsqlCommand(query, connection);
                using var adapter = new NpgsqlDataAdapter(command);
                var dataTable = new DataTable();
                connection.Open();
                adapter.Fill(dataTable);
                return dataTable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing query: {Query}", query);
                return new DataTable();
            }
        }

        public void ExecuteCommand(string sqlCommand)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                using var command = new NpgsqlCommand(sqlCommand, connection);
                connection.Open();
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command: {Command}", sqlCommand);
            }
        }

        public async Task<DataTable> ExecuteQueryAsync(string query)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await using var command = new NpgsqlCommand(query, connection);
                await connection.OpenAsync();

                using var reader = await command.ExecuteReaderAsync();
                var dataTable = new DataTable();
                dataTable.Load(reader);
                return dataTable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing async query: {Query}", query);
                return new DataTable();
            }
        }

        public async Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await using var command = new NpgsqlCommand(query, connection);

                foreach (var param in parameters)
                {
                    command.Parameters.AddWithValue(param.Key, param.Value);
                }

                await connection.OpenAsync();

                using var reader = await command.ExecuteReaderAsync();
                var dataTable = new DataTable();
                dataTable.Load(reader);
                return dataTable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing async query with parameters: {Query}", query);
                return new DataTable();
            }
        }

        public async Task ExecuteCommandAsync(string sqlCommand)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await using var command = new NpgsqlCommand(sqlCommand, connection);
                await connection.OpenAsync();
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing async command: {Command}", sqlCommand);
            }
        }
    }
}
