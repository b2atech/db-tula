using B2a.DbTula.Core.Abstractions;
using B2a.DbTula.Infrastructure.Postgres;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Infrastructure.MySql;

namespace B2A.DbTula.Cli.Factories;

public static class SchemaProviderFactory
{
    public static IDatabaseSchemaProvider Create(
    DbType dbType,
    string connectionString,
     Action<int, int, string, bool> logger,
    bool verbose = false,
    LogLevel logLevel = LogLevel.Basic)
    {
        return dbType switch
        {
            DbType.Postgres => new PostgresSchemaProvider(
                connectionString,
                logger,
                verbose,
                logLevel),
           DbType.MySql => new MySqlSchemaProvider(
                connectionString,
                logger,
                verbose,
                logLevel),
            _ => throw new NotSupportedException($"Unsupported DB type: {dbType}")
        };
    }
}