using System;
using System.Data;

namespace b2a.db_tula
{
    public class SchemaFetcher
    {
        private readonly DatabaseConnection _connection;
        private readonly Action<string> _log;

        public SchemaFetcher(DatabaseConnection connection, Action<string> log)
        {
            _connection = connection;
            _log = log;
        }

        public DataTable GetTables()
        {
            string query = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";
            return ExecuteQueryWithDebug(query);
        }

        public DataTable GetColumns(string tableName)
        {
            string query = $"SELECT column_name, data_type, character_maximum_length FROM information_schema.columns WHERE table_name = '{tableName}'";
            var result = ExecuteQueryWithDebug(query);
            if (result.Columns.Contains("column_name"))
            {
                result.PrimaryKey = new DataColumn[] { result.Columns["column_name"] };
            }
            return result;
        }

        public DataTable GetFunctions()
        {
            string query = "SELECT routine_name FROM information_schema.routines WHERE routine_type = 'FUNCTION' AND specific_schema = 'public'";
            return ExecuteQueryWithDebug(query);
        }

        public DataTable GetProcedures()
        {
            string query = "SELECT routine_name FROM information_schema.routines WHERE routine_type = 'PROCEDURE' AND specific_schema = 'public'";
            return ExecuteQueryWithDebug(query);
        }

        public DataTable GetFunctionParameters(string functionName)
        {
            string query = $"SELECT parameter_name, data_type FROM information_schema.parameters WHERE specific_name = '{functionName}'";
            return ExecuteQueryWithDebug(query);
        }

        public DataTable GetProcedureParameters(string procedureName)
        {
            string query = $"SELECT parameter_name, data_type FROM information_schema.parameters WHERE specific_name = '{procedureName}'";
            return ExecuteQueryWithDebug(query);
        }

        public string GetFunctionDefinition(string functionName)
        {
            string query = $"SELECT pg_get_functiondef(oid) AS definition FROM pg_proc WHERE proname = '{functionName}'";
            var result = ExecuteQueryWithDebug(query);
            return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
        }

        public string GetProcedureDefinition(string procedureName)
        {
            string query = $"SELECT pg_get_functiondef(oid) AS definition FROM pg_proc WHERE proname = '{procedureName}'";
            var result = ExecuteQueryWithDebug(query);
            return result.Rows.Count > 0 ? result.Rows[0]["definition"].ToString() : null;
        }

        public DataTable GetPrimaryKeys(string tableName)
        {
            string query = $"SELECT kcu.column_name FROM information_schema.table_constraints tc " +
                           $"JOIN information_schema.key_column_usage kcu " +
                           $"ON tc.constraint_name = kcu.constraint_name " +
                           $"WHERE tc.table_name = '{tableName}' AND tc.constraint_type = 'PRIMARY KEY'";
            return ExecuteQueryWithDebug(query);
        }

        public DataTable GetForeignKeys(string tableName)
        {
            string query = $"SELECT kcu.column_name, ccu.table_name AS foreign_table_name, ccu.column_name AS foreign_column_name " +
                           $"FROM information_schema.key_column_usage kcu " +
                           $"JOIN information_schema.constraint_column_usage ccu " +
                           $"ON kcu.constraint_name = ccu.constraint_name " +
                           $"WHERE kcu.table_name = '{tableName}' AND kcu.position_in_unique_constraint IS NOT NULL";
            return ExecuteQueryWithDebug(query);
        }

        private DataTable ExecuteQueryWithDebug(string query)
        {
            var result = _connection.ExecuteQuery(query);
            _log($"Query: {query}");
            _log($"Rows returned: {result.Rows.Count}");
            return result;
        }
    }
}
