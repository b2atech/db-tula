# 🟢 B2A.DbTula.Core

&#x20;

## Overview

**DBTula Core** is the abstraction layer and shared API for the DBTula database schema comparison toolkit.\
It provides models, contracts, and the comparison engine logic, and is agnostic of any specific database provider.

- Extensible, testable, and cleanly separated architecture.
- No direct database dependencies—just logic, types, and interfaces.
- Use with [B2A.DbTula.Infrastructure.MySql](https://www.nuget.org/packages/B2A.DbTula.Infrastructure.MySql) or [B2A.DbTula.Infrastructure.Postgres](https://www.nuget.org/packages/B2A.DbTula.Infrastructure.Postgres) for provider-specific comparison.

---

## 🚀 Installation

Install via NuGet:

```sh
dotnet add package B2A.DbTula.Core
```

Add the provider(s) you need:

```sh
dotnet add package B2A.DbTula.Infrastructure.Postgres
dotnet add package B2A.DbTula.Infrastructure.MySql
```

---

## ✨ Features

- **Schema Comparison Engine:**\
  Programmatic access to database schema comparison logic.
- **Extensible Provider Model:**\
  Plug in additional infrastructure packages for other databases.
- **Domain Models:**\
  Rich object model for tables, columns, keys, indexes, and more.
- **Reporting API:**\
  Generate results and scripts for diffs and synchronization.

---

## 🧑‍💻 Usage Example

```csharp
using B2A.DbTula.Core;
// using B2A.DbTula.Infrastructure.Postgres; // Or MySql

// Example usage (pseudo-code):
var comparer = new PostgresSchemaComparer();
var result = comparer.CompareSchemas(sourceConnectionString, targetConnectionString);

if (result.HasDifferences)
{
    Console.WriteLine("Schemas are different!");
    // Process differences or generate scripts
}
```

*For real usage, see the infrastructure package documentation or CLI for full examples.*

---

## 📚 Documentation

Full documentation, advanced examples, and API reference available at\
[GitHub Repository](https://github.com/b2atech/db-tula).

---

## Related Packages

- [B2A.DbTula.Infrastructure.MySql](https://www.nuget.org/packages/B2A.DbTula.Infrastructure.MySql) — MySQL support
- [B2A.DbTula.Infrastructure.Postgres](https://www.nuget.org/packages/B2A.DbTula.Infrastructure.Postgres) — PostgreSQL support
- [B2A.DbTula.Cli](https://www.nuget.org/packages/B2A.DbTula.Cli) — CLI Tool

---

## License

MIT License

