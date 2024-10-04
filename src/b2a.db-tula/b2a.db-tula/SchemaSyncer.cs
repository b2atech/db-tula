using b2a.db_tula.Models;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula
{
    public class SchemaSyncer
    {
        private readonly DatabaseConnection _sourceConnection;
        private readonly DatabaseConnection _targetConnection;
        private readonly Action<string> _log;

        public SchemaSyncer(DatabaseConnection sourceConnection, DatabaseConnection targetConnection, Action<string> log)
        {
            _sourceConnection = sourceConnection;
            _targetConnection = targetConnection;
            _log = log;
        }

        public void SyncTable(string sourceTable, string targetTable)
        {
            // Fetch table definition from the source
            var sourceSchemaFetcher = new SchemaFetcher(_sourceConnection, _log);
            var sourceTableDefinition = sourceSchemaFetcher.GetTableDefinition(sourceTable);

            // Fetch table definition from the target
            var targetSchemaFetcher = new SchemaFetcher(_targetConnection, _log);
            var targetTableDefinition = targetSchemaFetcher.GetTableDefinition(targetTable);

            // Generate the necessary SQL commands to sync the table schema
            var schemaComparer = new SchemaComparer();
            var sqlCommands = schemaComparer.GenerateSyncCommands(sourceTableDefinition, targetTableDefinition);

            // Execute the generated SQL commands on the target database
            foreach (var command in sqlCommands)
            {
                _targetConnection.ExecuteCommand(command);
                _log($"Executed command: {command}");
            }

            _log($"Table {sourceTable} synced successfully.");
        }

        public List<string> GenerateSyncCommands(TableDefinition sourceTable, TableDefinition targetTable)
        {
            var commands = new List<string>();

            // Compare columns
            foreach (var sourceColumn in sourceTable.Columns)
            {
                var targetColumn = targetTable.Columns.FirstOrDefault(c => c.Name == sourceColumn.Name);

                if (targetColumn == null)
                {
                    // Column does not exist in target, generate ADD COLUMN command
                    commands.Add($"ALTER TABLE {targetTable.Name} ADD COLUMN {sourceColumn.Name} {sourceColumn.DataType};");
                }
                else if (sourceColumn.DataType != targetColumn.DataType)
                {
                    // Data type mismatch, generate ALTER COLUMN command
                    commands.Add($"ALTER TABLE {targetTable.Name} ALTER COLUMN {sourceColumn.Name} TYPE {sourceColumn.DataType};");
                }
            }

            // Compare primary keys
            if (!sourceTable.PrimaryKeys.SequenceEqual(targetTable.PrimaryKeys))
            {
                commands.Add($"ALTER TABLE {targetTable.Name} DROP CONSTRAINT IF EXISTS {targetTable.Name}_pkey;");
                commands.Add($"ALTER TABLE {targetTable.Name} ADD PRIMARY KEY ({string.Join(",", sourceTable.PrimaryKeys)});");
            }

            // Compare foreign keys
            foreach (var sourceFk in sourceTable.ForeignKeys)
            {
                var targetFk = targetTable.ForeignKeys.FirstOrDefault(fk => fk.Name == sourceFk.Name);
                if (targetFk == null)
                {
                    commands.Add($"ALTER TABLE {targetTable.Name} ADD CONSTRAINT {sourceFk.Name} FOREIGN KEY ({sourceFk.ColumnName}) REFERENCES {sourceFk.ReferencedTable}({sourceFk.ReferencedColumn});");
                }
            }

            return commands;
        }


        public List<string> GenerateSyncCommandsForFunctionsOrProcedures(string sourceDefinition, string targetDefinition, string objectType)
        {
            var commands = new List<string>();

            if (sourceDefinition != targetDefinition)
            {
                if (objectType == "Function")
                {
                    // If function definition differs, generate a CREATE OR REPLACE FUNCTION command
                    commands.Add($"{sourceDefinition};");
                }
                else if (objectType == "Procedure")
                {
                    // If procedure definition differs, generate a CREATE OR REPLACE PROCEDURE command
                    commands.Add($"{sourceDefinition};");
                }
            }
            else
            {
                _log($"{objectType} is already synchronized.");
            }

            return commands;
        }



    }

}
