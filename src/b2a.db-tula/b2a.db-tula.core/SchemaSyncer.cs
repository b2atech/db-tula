using b2a.db_tula.core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.core
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

        //public void SyncTable(string sourceTable, string targetTable)
        //{
        //    var sourceSchemaFetcher = new SchemaFetcher(_sourceConnection, _log);
        //    var sourceTableDefinition = sourceSchemaFetcher.GetTableDefinition(sourceTable);

        //    var targetSchemaFetcher = new SchemaFetcher(_targetConnection, _log);
        //    var targetTableDefinition = targetSchemaFetcher.GetTableDefinition(targetTable);

        //    var schemaComparer = new SchemaComparer();
        //    var sqlCommands = schemaComparer.GenerateSyncCommands(sourceTableDefinition, targetTableDefinition);

        //    foreach (var command in sqlCommands)
        //    {
        //        _targetConnection.ExecuteCommand(command);
        //        _log($"Executed command: {command}");
        //    }

        //    _log($"Table '{sourceTable}' synced successfully.");
        //}

        public List<string> GenerateSyncCommands(TableDefinition sourceTable, TableDefinition? targetTable)
        {
            var commands = new List<string>();

            // Target table is missing
            if (targetTable == null || string.IsNullOrWhiteSpace(targetTable.Name))
            {
                if (!string.IsNullOrWhiteSpace(sourceTable.CreateScript))
                {
                    commands.Add(sourceTable.CreateScript);
                }
                else
                {
                    commands.Add($"-- Missing CreateScript for table \"{sourceTable.Name}\"");
                }
                return commands;
            }

            foreach (var sourceColumn in sourceTable.Columns)
            {
                var targetColumn = targetTable.Columns.FirstOrDefault(c => c.Name == sourceColumn.Name);

                if (targetColumn == null)
                {
                    commands.Add($"ALTER TABLE \"{targetTable.Name}\" ADD COLUMN \"{sourceColumn.Name}\" {sourceColumn.DataType};");
                }
                else if (sourceColumn.DataType != targetColumn.DataType)
                {
                    commands.Add($"ALTER TABLE \"{targetTable.Name}\" ALTER COLUMN \"{sourceColumn.Name}\" TYPE {sourceColumn.DataType};");
                }
            }

            if (!sourceTable.PrimaryKeys.SequenceEqual(targetTable.PrimaryKeys))
            {
                commands.Add($"ALTER TABLE \"{targetTable.Name}\" DROP CONSTRAINT IF EXISTS \"{targetTable.Name}_pkey\";");
                commands.Add($"ALTER TABLE \"{targetTable.Name}\" ADD PRIMARY KEY ({string.Join(", ", sourceTable.PrimaryKeys.Select(pk => $"\"{pk}\""))});");
            }

            foreach (var sourceFk in sourceTable.ForeignKeys)
            {
                var targetFk = targetTable.ForeignKeys.FirstOrDefault(fk => fk.Name == sourceFk.Name);
                if (targetFk == null)
                {
                    commands.Add($"ALTER TABLE \"{targetTable.Name}\" ADD CONSTRAINT \"{sourceFk.Name}\" FOREIGN KEY (\"{sourceFk.ColumnName}\") REFERENCES \"{sourceFk.ReferencedTable}\"(\"{sourceFk.ReferencedColumn}\");");
                }
            }

            return commands;
        }


        public List<string> GenerateSyncCommands(string sourceDefinition, string targetDefinition, string objectType)
        {
            var commands = new List<string>();

            if (sourceDefinition != targetDefinition)
            {
                _log($"{objectType} definition mismatch. Generating CREATE OR REPLACE statement.");
                commands.Add($"{sourceDefinition};");
            }
            else
            {
                _log($"{objectType} is already synchronized.");
            }

            return commands;
        }

        

    }
}
