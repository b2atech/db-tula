# db-tula

# db-tula

## Overview
**db-tula** is a powerful and intuitive schema comparison tool designed to compare database schemas across PostgreSQL and MySQL databases. It helps identify differences between database schemas, ensuring consistency and aiding in database migrations, with robust ownership-agnostic comparison capabilities.

## Features
- **Cross-Database Support**: Compare schemas between PostgreSQL and MySQL databases
- **Comprehensive Object Comparison**: Tables, columns, functions, procedures, views, triggers, indexes, constraints, and sequences
- **Ownership-Agnostic Comparison**: Ignore owner/definer differences for robust cross-environment comparisons (default enabled)
- **Order-Independent Semantic Comparison**: Compare table structures semantically, not by text order
- **DDL Noise Filtering**: Automatically filter out comments, grants, and other non-structural differences
- **Detailed HTML Reports**: Generate comprehensive comparison reports with visual diff highlighting
- **CLI Interface**: Command-line tool for integration into CI/CD pipelines
- **Test Mode**: Limited comparison for quick validation

## Installation

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later
- Access to PostgreSQL and/or MySQL databases
- Database connection permissions for schema reading

### Steps
1. **Clone the repository**
    ```sh
    git clone https://github.com/b2atech/db-tula.git
    cd db-tula
    ```

2. **Install dependencies**
    ```sh
    dotnet restore
    ```

3. **Build the project**
    ```sh
    dotnet build src/B2A.DbTula.Cli
    ```

4. **Run the application**
    ```sh
    dotnet run --project src/B2A.DbTula.Cli -- --help
    ```

## Usage

### Basic Schema Comparison

Compare schemas between two databases:

```sh
dotnet run --project src/B2A.DbTula.Cli -- \
  --source "Host=localhost;Port=5432;Database=source_db;User Id=postgres;Password=123" \
  --target "Host=localhost;Port=5432;Database=target_db;User Id=postgres;Password=123" \
  --sourceType postgres \
  --targetType postgres \
  --out comparison-report.html
```

### Cross-Database Comparison (PostgreSQL to MySQL)

```sh
dotnet run --project src/B2A.DbTula.Cli -- \
  --source "Host=localhost;Port=5432;Database=pg_db;User Id=postgres;Password=123" \
  --target "Server=localhost;Database=mysql_db;Uid=root;Pwd=123" \
  --sourceType postgres \
  --targetType mysql \
  --out cross-db-report.html
```

### Ownership-Agnostic Comparison

By default, db-tula ignores ownership differences for robust comparison:

```sh
# Default behavior - ignores owners/definers (recommended)
dotnet run --project src/B2A.DbTula.Cli -- \
  --source "connectionstring1" \
  --target "connectionstring2" \
  --sourceType postgres \
  --targetType postgres

# Include ownership differences in comparison
dotnet run --project src/B2A.DbTula.Cli -- \
  --source "connectionstring1" \
  --target "connectionstring2" \
  --sourceType postgres \
  --targetType postgres \
  --no-ignore-ownership
```

### Test Mode

Compare a limited number of objects for quick validation:

```sh
dotnet run --project src/B2A.DbTula.Cli -- \
  --source "connectionstring1" \
  --target "connectionstring2" \
  --sourceType postgres \
  --targetType mysql \
  --test \
  --limit 5 \
  --title "Quick Test Comparison"
```

### Schema Extraction

Extract database objects to SQL files:

```sh
dotnet run --project src/B2A.DbTula.Cli -- extract \
  --extract-conn "Host=localhost;Port=5432;Database=mydb;User Id=postgres;Password=123" \
  --extract-type postgres \
  --outputDir ./extracted-schema \
  --objects views,functions,procedures \
  --overwrite
```

## Ownership-Agnostic Comparison

One of db-tula's key features is ownership-agnostic comparison, which allows robust schema comparison across different environments where database objects may have different owners or definers.

### What Gets Normalized

When `--ignore-ownership` is enabled (default), db-tula automatically removes:

**PostgreSQL:**
- `ALTER ... OWNER TO username;` statements
- Schema prefixes (e.g., `public.table_name` → `table_name`)
- `SET search_path` statements
- Security labels and grants
- Comments

**MySQL:**
- `DEFINER=`username`@`host`` clauses
- `SQL SECURITY DEFINER/INVOKER` clauses  
- Database prefixes
- Comments

### Example

These PostgreSQL functions would be considered **identical** with ownership-agnostic comparison:

```sql
-- Source Database (owned by user1)
CREATE FUNCTION public.calculate_total(price numeric, tax_rate numeric)
RETURNS numeric AS $$
BEGIN
    RETURN price * (1 + tax_rate);
END;
$$ LANGUAGE plpgsql;
ALTER FUNCTION public.calculate_total(price numeric, tax_rate numeric) OWNER TO user1;

-- Target Database (owned by user2)  
CREATE FUNCTION public.calculate_total(price numeric, tax_rate numeric)
RETURNS numeric AS $$
BEGIN
    RETURN price * (1 + tax_rate);
END;
$$ LANGUAGE plpgsql;
ALTER FUNCTION public.calculate_total(price numeric, tax_rate numeric) OWNER TO user2;
```

### When to Disable

Use `--no-ignore-ownership` when:
- Ownership differences are functionally important
- You need to audit actual object ownership
- Comparing within the same environment where ownership should match

## Command Line Options

```
Usage:
  dotnet db-tula.cli.dll compare --source <src-conn> --target <tgt-conn> --sourceType postgres --targetType mysql [options]
  dotnet db-tula.cli.dll extract --extract-conn <conn> --extract-type postgres [options]

Comparison Options:
  --source <connection>        Source database connection string
  --target <connection>        Target database connection string  
  --sourceType <type>          Source database type (postgres, mysql)
  --targetType <type>          Target database type (postgres, mysql)
  --out <file>                 Output HTML report file (default: schema-sync.html)
  --ignore-ownership           Ignore owner/definer differences (default: true)
  --no-ignore-ownership        Include owner/definer differences in comparison
  --test                       Enable test mode (limited object comparison)
  --limit <number>             Number of objects to compare in test mode
  --title <text>               Custom report title

Extraction Options:
  extract                      Switch to extraction mode
  --extract-conn <connection>  Database connection string for extraction
  --extract-type <type>        Database type for extraction (postgres, mysql)
  --outputDir <directory>      Output directory for extracted files (default: dbobjects)
  --objects <types>            Object types to extract (default: all)
                              Available: tables,views,functions,procedures,triggers
  --overwrite                  Overwrite existing files
```

Contributions are welcome! Please follow these steps:

1. **Fork the repository**.
2. **Create a new branch**.
    ```sh
    git checkout -b feature-branch
    ```

3. **Make your changes**.
4. **Commit your changes**.
    ```sh
    git commit -m "Description of your changes"
    ```

5. **Push to the branch**.
    ```sh
    git push origin feature-branch
    ```

6. **Create a Pull Request**.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.md) file for more details.

## Contact

For any questions or suggestions, feel free to open an issue or reach out to me at [bharat.mane@gmail.com](mailto:bharat.mane@gmail.com).

---

**db-tula** - Simplifying database schema comparison.
