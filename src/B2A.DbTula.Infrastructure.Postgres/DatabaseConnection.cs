using B2A.DbTula.Core.Enums;
using Npgsql;
using System.Data;

namespace B2A.DbTula.Infrastructure.Postgres;

public class DatabaseConnection
{
    private readonly string _connectionString;
    private readonly Action<int, int, string, bool>? _logger;
    private readonly bool _verbose;
    private readonly LogLevel _logLevel;
    private readonly int _commandTimeoutSeconds;

    public DatabaseConnection(
        string connectionString,
        Action<int, int, string, bool>? logger,
        bool verbose,
        LogLevel logLevel,
        int commandTimeoutSeconds = 120)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Database connection string cannot be empty.", nameof(connectionString));

        _connectionString = connectionString;
        _logger = logger;
        _verbose = verbose;
        _logLevel = logLevel;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public DataTable ExecuteQuery(string query)
    {
        ValidateQuery(query);

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            using var command = new NpgsqlCommand(query, connection)
            {
                CommandTimeout = _commandTimeoutSeconds
            };

            using var adapter = new NpgsqlDataAdapter(command);

            var dataTable = new DataTable();

            Log($"Opening PostgreSQL connection for sync query...");
            connection.Open();

            Log($"Executing sync query...");
            adapter.Fill(dataTable);

            Log($"Sync query completed. Rows={dataTable.Rows.Count}");

            return dataTable;
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteQuery] Failed to execute query. Error={ex.Message}");
            LogError($"[ExecuteQuery] Query:\n{query}");
            throw;
        }
    }

    public async Task<DataTable> ExecuteQueryAsync(string query)
    {
        ValidateQuery(query);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await using var command = new NpgsqlCommand(query, connection)
            {
                CommandTimeout = _commandTimeoutSeconds
            };

            Log($"Opening PostgreSQL connection for async query...");
            await connection.OpenAsync();

            Log($"Executing async query...");
            await using var reader = await command.ExecuteReaderAsync();

            var dataTable = new DataTable();
            dataTable.Load(reader);

            Log($"Async query completed. Rows={dataTable.Rows.Count}");

            return dataTable;
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteQueryAsync] Failed to execute query. Error={ex.Message}");
            LogError($"[ExecuteQueryAsync] Query:\n{query}");
            throw;
        }
    }

    public async Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters)
    {
        ValidateQuery(query);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await using var command = new NpgsqlCommand(query, connection)
            {
                CommandTimeout = _commandTimeoutSeconds
            };

            foreach (var param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }

            Log($"Opening PostgreSQL connection for async parameterized query...");
            await connection.OpenAsync();

            Log($"Executing async parameterized query...");
            await using var reader = await command.ExecuteReaderAsync();

            var dataTable = new DataTable();
            dataTable.Load(reader);

            Log($"Async parameterized query completed. Rows={dataTable.Rows.Count}");

            return dataTable;
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteQueryAsync with Parameters] Failed to execute query. Error={ex.Message}");
            LogError($"[ExecuteQueryAsync with Parameters] Query:\n{query}");
            LogError($"[ExecuteQueryAsync with Parameters] Parameters={FormatParameters(parameters)}");
            throw;
        }
    }

    public void ExecuteCommand(string sqlCommand)
    {
        ValidateQuery(sqlCommand);

        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            using var command = new NpgsqlCommand(sqlCommand, connection)
            {
                CommandTimeout = _commandTimeoutSeconds
            };

            Log($"Opening PostgreSQL connection for sync command...");
            connection.Open();

            Log($"Executing sync command...");
            var affectedRows = command.ExecuteNonQuery();

            Log($"Sync command completed. AffectedRows={affectedRows}");
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteCommand] Failed to execute command. Error={ex.Message}");
            LogError($"[ExecuteCommand] Command:\n{sqlCommand}");
            throw;
        }
    }

    public async Task ExecuteCommandAsync(string sqlCommand)
    {
        ValidateQuery(sqlCommand);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await using var command = new NpgsqlCommand(sqlCommand, connection)
            {
                CommandTimeout = _commandTimeoutSeconds
            };

            Log($"Opening PostgreSQL connection for async command...");
            await connection.OpenAsync();

            Log($"Executing async command...");
            var affectedRows = await command.ExecuteNonQueryAsync();

            Log($"Async command completed. AffectedRows={affectedRows}");
        }
        catch (Exception ex)
        {
            LogError($"[ExecuteCommandAsync] Failed to execute command. Error={ex.Message}");
            LogError($"[ExecuteCommandAsync] Command:\n{sqlCommand}");
            throw;
        }
    }

    public async Task<bool> CanConnectAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            return true;
        }
        catch (Exception ex)
        {
            LogError($"[CanConnectAsync] PostgreSQL connection failed. Error={ex.Message}");
            throw;
        }
    }

    public void Log(string message)
    {
        if (_verbose)
        {
            _logger?.Invoke(0, 0, $"[{_logLevel}] {message}", false);
        }
    }

    private void LogError(string message)
    {
        _logger?.Invoke(0, 0, $"[ERROR] {message}", false);
    }

    private static void ValidateQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("SQL query/command cannot be empty.", nameof(query));
    }

    private static string FormatParameters(Dictionary<string, object> parameters)
    {
        if (parameters.Count == 0)
            return "none";

        return string.Join(", ", parameters.Select(p =>
        {
            var value = p.Value == null || p.Value == DBNull.Value
                ? "NULL"
                : p.Value.ToString();

            return $"{p.Key}={value}";
        }));
    }
}


//using B2A.DbTula.Core.Enums;
//using Npgsql;
//using System.Data;

//namespace B2A.DbTula.Infrastructure.Postgres;

//public class DatabaseConnection
//{
//    private readonly string _connectionString;
//    private readonly Action<int, int, string, bool> _logger;
//    private readonly bool _verbose;
//    private readonly LogLevel _logLevel;

//    public DatabaseConnection(string connectionString, Action<int, int, string, bool>? logger, bool verbose, LogLevel logLevel)
//    {
//        _connectionString = connectionString;
//        _logger = logger;
//        _verbose = verbose;
//        _logLevel = logLevel;
//    }

//    public DataTable ExecuteQuery(string query)
//    {
//        try
//        {
//            using var connection = new NpgsqlConnection(_connectionString);
//            using var command = new NpgsqlCommand(query, connection);
//            using var adapter = new NpgsqlDataAdapter(command);
//            var dataTable = new DataTable();
//            connection.Open();
//            adapter.Fill(dataTable);
//            return dataTable;
//        }
//        catch (Exception ex)
//        {
//            LogError($"[ExecuteQuery] Error executing query:\n{query}\n{ex}");
//            return new DataTable();
//        }
//    }

//    public void ExecuteCommand(string sqlCommand)
//    {
//        try
//        {
//            using var connection = new NpgsqlConnection(_connectionString);
//            using var command = new NpgsqlCommand(sqlCommand, connection);
//            connection.Open();
//            command.ExecuteNonQuery();
//        }
//        catch (Exception ex)
//        {
//            LogError($"[ExecuteCommand] Error executing command:\n{sqlCommand}\n{ex}");
//        }
//    }

//    public async Task<DataTable> ExecuteQueryAsync(string query)
//    {
//        try
//        {
//            await using var connection = new NpgsqlConnection(_connectionString);
//            await using var command = new NpgsqlCommand(query, connection);
//            await connection.OpenAsync();

//            await using var reader = await command.ExecuteReaderAsync();
//            var dataTable = new DataTable();
//            dataTable.Load(reader);
//            return dataTable;
//        }
//        catch (Exception ex)
//        {
//            LogError($"[ExecuteQueryAsync] Error executing query:\n{query}\n{ex}");
//            return new DataTable();
//        }
//    }

//    public async Task<DataTable> ExecuteQueryAsync(string query, Dictionary<string, object> parameters)
//    {
//        try
//        {
//            await using var connection = new NpgsqlConnection(_connectionString);
//            await using var command = new NpgsqlCommand(query, connection);

//            foreach (var param in parameters)
//            {
//                command.Parameters.AddWithValue(param.Key, param.Value);
//            }

//            await connection.OpenAsync();

//            await using var reader = await command.ExecuteReaderAsync();
//            var dataTable = new DataTable();
//            dataTable.Load(reader);
//            return dataTable;
//        }
//        catch (Exception ex)
//        {
//            LogError($"[ExecuteQueryAsync with Parameters] Error executing query:\n{query}\n{ex}");
//            return new DataTable();
//        }
//    }

//    public async Task ExecuteCommandAsync(string sqlCommand)
//    {
//        try
//        {
//            await using var connection = new NpgsqlConnection(_connectionString);
//            await using var command = new NpgsqlCommand(sqlCommand, connection);
//            await connection.OpenAsync();
//            await command.ExecuteNonQueryAsync();
//        }
//        catch (Exception ex)
//        {
//            LogError($"[ExecuteCommandAsync] Error executing command:\n{sqlCommand}\n{ex}");
//        }
//    }

//    public void Log(string message)
//    {
//        if (_verbose)
//            _logger?.Invoke(0,0,$"[{_logLevel}] {message}",false);
//    }

//    private void LogError(string message)
//    {
//        _logger?.Invoke(0,0,$"[ERROR] {message}", false);
//    }
//}
