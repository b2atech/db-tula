using Npgsql;
using System.Data;

namespace b2a.db_tula.core
{
    public class DatabaseConnection
    {
        private readonly string _connectionString;

        public DatabaseConnection(string connectionString)
        {
            _connectionString = connectionString;
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
                Console.WriteLine($"[ExecuteQuery Error] {ex.Message}");
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
                Console.WriteLine($"[ExecuteCommand Error] {ex.Message}");
            }
        }

        public async Task<DataTable> ExecuteQueryAsync(string query)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await using var command = new NpgsqlCommand(query, connection);
                await connection.OpenAsync();

                var reader = await command.ExecuteReaderAsync();
                var dataTable = new DataTable();
                dataTable.Load(reader);
                return dataTable;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExecuteQueryAsync Error] {ex.Message}");
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

                var reader = await command.ExecuteReaderAsync();
                var dataTable = new DataTable();
                dataTable.Load(reader);
                return dataTable;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExecuteQueryAsync with params Error] {ex.Message}");
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
                Console.WriteLine($"[ExecuteCommandAsync Error] {ex.Message}");
            }
        }
    }
}
