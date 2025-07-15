# 🟢 B2A.DbTula.Infrastructure.Postgres

&#x20;

## Overview

**DBTula PostgreSQL Infrastructure** is a provider package for [B2A.DbTula.Core](https://www.nuget.org/packages/B2A.DbTula.Core), enabling full-featured schema comparison and script generation for PostgreSQL databases.

- Implements `IDatabaseSchemaProvider` for PostgreSQL
- Comprehensive support for tables, columns, keys, indexes, sequences, functions, and procedures
- Returns full schema definitions and create scripts via high-level API
- Ready for local tools, automation, and pipelines

---

## 🚀 Installation

Install via NuGet (in addition to the Core package):

```sh
dotnet add package B2A.DbTula.Core
dotnet add package B2A.DbTula.Infrastructure.Postgres
```

---

## ✨ Features

- **PostgreSQL Schema Fetcher:**\
  Reads table/column metadata, PKs, FKs, indexes, sequences, routines, and DDL
- **Rich Object Model:**\
  Returns `TableDefinition`, `ColumnDefinition`, `PrimaryKeyDefinition`, `ForeignKeyDefinition`, and more (from Core)
- **Script Extraction:**\
  Extracts create scripts for tables, indexes, keys, sequences, and routines
- **Integration Ready:**\
  Works seamlessly with [B2A.DbTula.Cli](https://www.nuget.org/packages/B2A.DbTula.Cli) or your own app

---

## 🧑‍💻 Usage Example

```csharp
using B2A.DbTula.Core.Abstractions;
using B2A.DbTula.Infrastructure.Postgres;

// Configure logger as needed for your environment
Action<int, int, string, bool> logger = (code, level, msg, isError) => Console.WriteLine(msg);

var provider = new PostgresSchemaProvider(
    connectionString: "Host=localhost;Database=test;Username=postgres;Password=pass;",
    logger: logger,
    verbose: true
);

// List tables
var tables = await provider.GetTablesAsync();

foreach (var table in tables)
{
    var definition = await provider.GetTableDefinitionAsync(table);
    Console.WriteLine($"Table: {definition.Name}");
    // ... work with definition.Columns, definition.PrimaryKeys, etc.
}
```

---

## 📚 Documentation

See [GitHub Repository](https://github.com/b2atech/db-tula) for advanced usage, CLI, and troubleshooting.

---

## Related Packages

- [B2A.DbTula.Core](https://www.nuget.org/packages/B2A.DbTula.Core) — Required abstraction & models
- [B2A.DbTula.Infrastructure.MySql](https://www.nuget.org/packages/B2A.DbTula.Infrastructure.MySql) — MySQL provider
- [B2A.DbTula.Cli](https://www.nuget.org/packages/B2A.DbTula.Cli) — CLI Tool

---

## License

MIT License

