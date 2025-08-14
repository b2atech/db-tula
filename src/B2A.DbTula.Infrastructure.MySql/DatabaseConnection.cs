using B2A.DbTula.Core.Enums;
using MySqlConnector;
using System.Data;

namespace B2A.DbTula.Infrastructure.MySql;

public class DatabaseConnection
{
    private readonly string _connectionString;
    private readonly Action<int, int, string, bool>? _logger;
    private readonly bool _verbose;
    private readonly LogLevel _logLevel;

    public DatabaseConnection(string connectionString, Action<int, int, string, bool>? logger, bool verbose, LogLevel logLevel)
    {
        _connectionString = connectionString;
        _logger = logger;
        _verbose = verbose;
        _logLevel = logLevel;
    }

    public DataTable ExecuteQuery(string query)
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            using var command = new MySqlCommand(query, connection);
            using var adapter = new MySqlDataAdapter(command);
            var dataTable = new DataTable();
            connection.Open();
            adapter.Fill(dataTable);
            return dataTable;
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteQuery] Error executing query:\n{query}\n{ex}");
            return new DataTable();
        }
    }

    public void ExecuteCommand(string sqlCommand)
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            using var command = new MySqlCommand(sqlCommand, connection);
            connection.Open();
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteCommand] Error executing command:\n{sqlCommand}\n{ex}");
        }
    }

    public async Task<DataTable> ExecuteQueryAsync(string query)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await using var command = new MySqlCommand(query, connection);
            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();
            var dataTable = new DataTable();
            dataTable.Load(reader);
            return dataTable;
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteQueryAsync] Error executing query:\n{query}\n{ex}");
            return new DataTable();
        }
    }

    public async Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await using var command = new MySqlCommand(query, connection);

            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            await connection.OpenAsync();

            await using var reader = await command.ExecuteReaderAsync();
            var dataTable = new DataTable();
            dataTable.Load(reader);
            return dataTable;
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteQueryAsync with Parameters] Error executing query:\n{query}\n{ex}");
            return new DataTable();
        }
    }

    public async Task ExecuteCommandAsync(string sqlCommand)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await using var command = new MySqlCommand(sqlCommand, connection);
            await connection.OpenAsync();
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteCommandAsync] Error executing command:\n{sqlCommand}\n{ex}");
        }
    }

    public void Log(string message)
    {
        if (_verbose)
            _logger?.Invoke(0, 0, $"[{_logLevel}] {message}", false);
    }

    private void LogError(string message)
    {
        _logger?.Invoke(0, 0, $"[ERROR] {message}", false);
    }
}
