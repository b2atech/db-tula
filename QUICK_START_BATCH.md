# Quick Start: Multiple Database Support

This guide shows how to use the new batch processing feature to extract and compare multiple databases in a single run.

## What's New?

You can now process 5-6 or more databases at once using a JSON configuration file. Each database extraction or comparison generates its own output.

## Command

```bash
# Using the new batch mode
dotnet B2A.DbTula.Cli.dll --batch batch-config.json

# Or use the alias
dotnet B2A.DbTula.Cli.dll --config batch-config.json
```

## Example Configuration

Create a file named `batch-config.json`:

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
      "name": "QA Database 2",
      "connectionString": "Server=db.qa.example.com;Port=5432;Database=qa-db2;User Id=user2;Password=pass2;",
      "dbType": "postgres",
      "outputDir": "./extracted-qa-db2",
      "objects": "all",
      "overwrite": true
    },
    {
      "name": "QA Database 3",
      "connectionString": "Server=db.qa.example.com;Port=5432;Database=qa-db3;User Id=user3;Password=pass3;",
      "dbType": "postgres",
      "outputDir": "./extracted-qa-db3",
      "objects": "views,functions",
      "overwrite": true
    }
  ],
  "comparisonJobs": [
    {
      "name": "Compare QA DB1 to Production",
      "sourceConnectionString": "Server=db.qa.example.com;Port=5432;Database=qa-db1;User Id=user1;Password=pass1;",
      "targetConnectionString": "Server=db.prod.example.com;Port=5432;Database=prod-db;User Id=produser;Password=prodpass;",
      "sourceType": "postgres",
      "targetType": "postgres",
      "outputFile": "./comparison-qa-db1-vs-prod.html",
      "title": "QA DB1 vs Production Schema Comparison",
      "ignoreOwnership": true
    }
  ]
}
```

## What Happens?

When you run the command:

1. All **extraction jobs** are processed first, in order
   - Each job extracts schema objects to its own directory
   - Progress is logged for each job
   
2. Then all **comparison jobs** are processed, in order
   - Each job generates its own HTML report
   - Progress is logged for each job

3. If one job fails, the others continue to run

## Output Example

```
🚀 Starting batch processing with 4 jobs
📤 Processing 3 extraction jobs
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📋 Job 1/4: QA Database 1
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📦 Extracting from database: QA Database 1
   Database Type: postgres
   Output Directory: ./extracted-qa-db1
   Objects: functions,procedures,views,triggers
   ✓ Objects written to ./extracted-qa-db1
✅ Extraction job 'QA Database 1' completed successfully

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
📋 Job 2/4: QA Database 2
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
...
```

## Migration from Single Database

### Before (single database):
```bash
dotnet B2A.DbTula.Cli.dll --extract \
  --extract-conn "Server=...;Database=db1;..." \
  --extract-type postgres \
  --outputDir ./extracted-db1 \
  --objects functions,procedures,views,triggers \
  --overwrite
```

### After (multiple databases):
Create `batch-config.json` with multiple extraction jobs and run:
```bash
dotnet B2A.DbTula.Cli.dll --batch batch-config.json
```

## For More Information

See [BATCH_PROCESSING.md](BATCH_PROCESSING.md) for detailed documentation.
