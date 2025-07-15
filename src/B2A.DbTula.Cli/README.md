# 🟢 B2A.DbTula.Cli – Command Line Tool

&#x20;

## Overview

**DBTula CLI** is a fast, cross-platform command-line tool for comparing database schemas and generating migration scripts for **PostgreSQL** and **MySQL**. It is designed for use in local development, DevOps pipelines, and automation.

---

## 🚀 Installation

Install globally (recommended):

```sh
dotnet tool install --global dbtula
```

Or, use as a local tool in your repository:

```sh
dotnet new tool-manifest # if you don't have one
dotnet tool install dbtula
```

---

## 🔥 Usage

### Basic Schema Comparison

```sh
dbtula --source "<src-conn-string>" --target "<tgt-conn-string>" --sourceType postgres --targetType mysql --out schema-sync.html
```

- `--source`        : Source database connection string
- `--target`        : Target database connection string
- `--sourceType`    : Source DB type (`postgres` or `mysql`)
- `--targetType`    : Target DB type (`postgres` or `mysql`)
- `--out`           : Output HTML report file (default: `schema-sync.html`)

### Additional Options

- `--test`                  : Enable test mode (compare only limited number of objects)
- `--limit <number>`        : Number of objects to compare in test mode (default: 10)
- `--title <text>`          : Custom title to display in the HTML report

### Full Example

```sh
dbtula \
  --source "Host=localhost;Port=5432;Database=src;Username=postgres;Password=pass" \
  --target "Server=localhost;Database=tgt;Uid=root;Pwd=pass;" \
  --sourceType postgres \
  --targetType mysql \
  --out diff.html \
  --title "Production vs QA Comparison" \
  --test --limit 5
```

---

## 📝 All CLI Options

| Option             | Description                                                |
| ------------------ | ---------------------------------------------------------- |
| `--source`         | Source DB connection string                                |
| `--target`         | Target DB connection string                                |
| `--sourceType`     | `postgres` or `mysql`                                      |
| `--targetType`     | `postgres` or `mysql`                                      |
| `--out`            | Output HTML report file name (default: `schema-sync.html`) |
| `--title`          | Custom title for the report header                         |
| `--test`           | Enable test mode (limits comparison to N objects)          |
| `--limit <number>` | Number of objects to compare in test mode (default: 10)    |

---

## 📚 Documentation

For advanced scenarios, API usage, and troubleshooting, see\
[GitHub Repository](https://github.com/b2atech/db-tula).

---

## Related Packages

- [B2A.DbTula.Core](https://www.nuget.org/packages/B2A.DbTula.Core) — Required abstraction & models
- [B2A.DbTula.Infrastructure.MySql](https://www.nuget.org/packages/B2A.DbTula.Infrastructure.MySql) — MySQL provider
- [B2A.DbTula.Infrastructure.Postgres](https://www.nuget.org/packages/B2A.DbTula.Infrastructure.Postgres) — PostgreSQL provider

---

## License

MIT License

