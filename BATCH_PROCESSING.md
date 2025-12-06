# Batch Processing Multiple Databases

This feature allows you to process multiple database extractions and comparisons in a single run using a JSON configuration file.

## Usage

```bash
dotnet B2A.DbTula.Cli.dll --batch batch-config.json
# or
dotnet B2A.DbTula.Cli.dll --config batch-config.json
```

## Configuration File Format

The configuration file is a JSON file with two main sections:
- `extractionJobs`: Array of database extraction jobs
- `comparisonJobs`: Array of database comparison jobs

### Example Configuration

```json
{
  "extractionJobs": [
    {
      "name": "QA Database 1",
      "connectionString": "Server=db.qa.example.com;Port=5432;Database=qa-db1;User Id=user1;Password=pass1;",
      "dbType": "postgres",
      "outputDir": "./extracted-qa-db1",
      "objects": "functions,procedures,views,triggers",
      "overwrite": true
    },
    {
      "name": "Production Database",
      "connectionString": "Server=db.prod.example.com;Port=5432;Database=prod-db;User Id=produser;Password=prodpass;",
      "dbType": "postgres",
      "outputDir": "./extracted-prod-db",
      "objects": "all",
      "overwrite": true
    }
  ],
  "comparisonJobs": [
    {
      "name": "Compare QA to Production",
      "sourceConnectionString": "Server=db.qa.example.com;Port=5432;Database=qa-db1;User Id=user1;Password=pass1;",
      "targetConnectionString": "Server=db.prod.example.com;Port=5432;Database=prod-db;User Id=produser;Password=prodpass;",
      "sourceType": "postgres",
      "targetType": "postgres",
      "outputFile": "./comparison-qa-vs-prod.html",
      "title": "QA vs Production Schema Comparison",
      "ignoreOwnership": true
    }
  ]
}
```

## Extraction Job Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `name` | string | Yes | - | Friendly name for the extraction job |
| `connectionString` | string | Yes | - | Database connection string |
| `dbType` | string | Yes | - | Database type: `postgres` or `mysql` |
| `outputDir` | string | No | `dbobjects` | Directory to write extracted .sql files |
| `objects` | string | No | `all` | Object types to extract: `all`, or comma-separated list like `functions,procedures,views,triggers,tables` |
| `overwrite` | boolean | No | `false` | Whether to overwrite existing files |

## Comparison Job Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `name` | string | Yes | - | Friendly name for the comparison job |
| `sourceConnectionString` | string | Yes | - | Source database connection string |
| `targetConnectionString` | string | Yes | - | Target database connection string |
| `sourceType` | string | Yes | - | Source database type: `postgres` or `mysql` |
| `targetType` | string | Yes | - | Target database type: `postgres` or `mysql` |
| `outputFile` | string | No | `schema-sync.html` | Output HTML report file path |
| `title` | string | No | `Schema Comparison Report` | Title for the comparison report |
| `ignoreOwnership` | boolean | No | `true` | Whether to ignore owner/definer differences |

## Features

- **Parallel Processing**: All jobs are processed sequentially with clear progress indicators
- **Error Handling**: If one job fails, the others will still be processed
- **Detailed Logging**: Each job logs its progress and results
- **Flexible Output**: Each extraction job can have its own output directory, and each comparison job can generate its own HTML report

## Use Cases

1. **Extract schemas from multiple environments**: Extract schemas from QA, Staging, and Production databases in one run
2. **Compare multiple database pairs**: Compare multiple QA databases against production
3. **Regular schema audits**: Set up a scheduled job to extract and compare schemas from multiple databases
4. **Multi-tenant systems**: Extract schemas from multiple tenant databases for analysis

## Notes

- Jobs are executed sequentially (not in parallel) to avoid connection pool exhaustion
- Each job is isolated - a failure in one job doesn't stop the others
- The batch configuration file can contain extraction jobs only, comparison jobs only, or both
- Connection strings in the configuration file should be properly escaped JSON strings
