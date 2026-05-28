using B2A.DbTula.Cli.Factories;
using B2A.DbTula.Cli.Helpers;
using B2A.DbTula.Cli.Reports;
using B2A.DbTula.Cli.Services;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace B2A.DbTula.Cli;

public record BatchJob
{
    public string   Name          { get; init; } = "";
    public string   Source        { get; init; } = "";
    public string   Target        { get; init; } = "";
    public string   SourceLabel   { get; init; } = "QA";
    public string   TargetLabel   { get; init; } = "PROD";
    public string   SourceType    { get; init; } = "postgres";
    public string   TargetType    { get; init; } = "postgres";
    public string   OutputFile    { get; init; } = "";
    public bool     GenerateSync  { get; init; } = false;
    public string?  SyncOutputFile { get; init; }
    public bool     Optional      { get; init; } = false;
    public bool     IgnoreOwnership { get; init; } = true;
}

public record BatchConfig
{
    public string        Title       { get; init; } = "Schema Comparison Batch Report";
    public List<BatchJob> Comparisons { get; init; } = new();
}

public record BatchJobResult(
    string  Name,
    string  OutputFile,
    int     MatchCount,
    int     MismatchCount,
    int     MissingInTargetCount,
    int     MissingInSourceCount,
    bool    Skipped     = false,
    string? SkipReason  = null,
    bool    Failed      = false,
    string? FailReason  = null)
{
    public int  DriftCount => MismatchCount + MissingInTargetCount + MissingInSourceCount;
    public bool HasDrift   => DriftCount > 0;
}

public static class BatchProcessor
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
    };

    public static async Task<BatchConfig?> LoadBatchConfigurationAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            Log.Error("Batch config file not found: {Path}", configPath);
            return null;
        }
        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<BatchConfig>(json, _jsonOpts);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse batch config: {Path}", configPath);
            return null;
        }
    }

    public static async Task<int> ProcessBatchAsync(
        BatchConfig config,
        bool testMode,
        int  testObjectLimit,
        bool failOnDrift = false)
    {
        Log.Information("📦 Batch: {Title} — {Count} job(s)", config.Title, config.Comparisons.Count);

        var results  = new List<BatchJobResult>();
        var comparer = new SchemaComparer();
        var logger   = LoggerHelpers.CreateUnifiedLogger();

        foreach (var job in config.Comparisons)
        {
            var result = await RunJobAsync(job, comparer, logger, testMode, testObjectLimit);
            results.Add(result);
        }

        // ── Summary ───────────────────────────────────────────────────────────
        Log.Information("");
        Log.Information("╔══════════════════════════════════════════════════════╗");
        Log.Information("║              BATCH SUMMARY                          ║");
        Log.Information("╠══════════════════════════════════════════════════════╣");
        foreach (var r in results)
        {
            if (r.Skipped)
                Log.Information("║  ⏭  {Name,-12} — skipped ({Reason})", r.Name, r.SkipReason);
            else if (r.Failed)
                Log.Warning(   "║  ❌ {Name,-12} — FAILED  ({Reason})", r.Name, r.FailReason);
            else if (r.HasDrift)
                Log.Warning(   "║  ⚠️  {Name,-12} — drift: {Drift} ({MM} mismatch, {MT} missing-target, {MS} missing-source)",
                    r.Name, r.DriftCount, r.MismatchCount, r.MissingInTargetCount, r.MissingInSourceCount);
            else
                Log.Information("║  ✅ {Name,-12} — clean ({Match} objects match)", r.Name, r.MatchCount);
        }
        Log.Information("╚══════════════════════════════════════════════════════╝");

        // ── Email ─────────────────────────────────────────────────────────────
        var driftResults = results.Where(r => !r.Skipped && !r.Failed && r.HasDrift).ToList();
        if (driftResults.Count > 0)
        {
            var emailConfig = EmailService.ReadFromEnvironment();
            if (emailConfig != null)
            {
                var subject  = $"⚠️ Schema Drift — {config.Title} ({driftResults.Count} service(s) have drift)";
                var body     = EmailService.BuildBatchDriftEmailBody(config.Title, results);
                var reports  = results
                    .Where(r => !r.Skipped && !r.Failed && File.Exists(r.OutputFile))
                    .Select(r => r.OutputFile)
                    .ToList();
                try
                {
                    await EmailService.SendBatchDriftReportAsync(emailConfig, subject, body, reports);
                    Log.Information("📧 Drift report emailed to: {To}", string.Join(", ", emailConfig.To));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "📧 Failed to send batch drift email — check SMTP env vars");
                }
            }
        }

        // ── Exit code ─────────────────────────────────────────────────────────
        if (!failOnDrift) return 0;

        var failedJobs = results.Where(r => r.Failed).ToList();
        if (failedJobs.Count > 0) return 3;

        var hasMissingInTarget = results.Any(r => r.MissingInTargetCount > 0);
        if (hasMissingInTarget) return 2;

        return results.Any(r => r.HasDrift) ? 1 : 0;
    }

    private static async Task<BatchJobResult> RunJobAsync(
        BatchJob job,
        SchemaComparer comparer,
        Action<int, int, string, bool> logger,
        bool testMode,
        int  testObjectLimit)
    {
        // Substitute ${ENV_VAR} placeholders
        var source = Substitute(job.Source);
        var target = Substitute(job.Target);

        // Handle missing env vars
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            if (job.Optional)
            {
                Log.Information("⏭  {Name}: skipped — env var not configured", job.Name);
                return new BatchJobResult(job.Name, job.OutputFile, 0, 0, 0, 0,
                    Skipped: true, SkipReason: "env var not set");
            }
            Log.Error("❌ {Name}: required env var is not set", job.Name);
            return new BatchJobResult(job.Name, job.OutputFile, 0, 0, 0, 0,
                Failed: true, FailReason: "required env var not set");
        }

        Log.Information("▶  {Name}: comparing {SourceLabel} → {TargetLabel}", job.Name, job.SourceLabel, job.TargetLabel);

        try
        {
            Enum.TryParse<DbType>(job.SourceType, true, out var srcType);
            Enum.TryParse<DbType>(job.TargetType, true, out var tgtType);

            var unifiedLogger  = LoggerHelpers.CreateUnifiedLogger();
            var sourceProvider = SchemaProviderFactory.Create(srcType, source, unifiedLogger, verbose: false);
            var targetProvider = SchemaProviderFactory.Create(tgtType, target, unifiedLogger, verbose: false);

            var options = new ComparisonOptions
            {
                IgnoreOwnership = job.IgnoreOwnership,
                SourceLabel     = job.SourceLabel,
                TargetLabel     = job.TargetLabel,
            };

            var comparisonResults = await comparer.CompareAsync(
                sourceProvider, targetProvider, unifiedLogger,
                testMode, testObjectLimit, options);

            var resultList = comparisonResults.ToList();
            SchemaLinter.Annotate(resultList);

            int matchCount          = resultList.Count(r => r.Status == ComparisonStatus.Match);
            int mismatchCount       = resultList.Count(r => r.Status == ComparisonStatus.Mismatch);
            int missingInTargetCount = resultList.Count(r => r.Status == ComparisonStatus.MissingInTarget);
            int missingInSourceCount = resultList.Count(r => r.Status == ComparisonStatus.MissingInSource);

            // Ensure output directory exists
            var outDir = Path.GetDirectoryName(job.OutputFile);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            var report = new SchemaComparisonReport
            {
                Title       = $"{job.Name} Schema ({job.SourceLabel} vs {job.TargetLabel})",
                SourceLabel = job.SourceLabel,
                TargetLabel = job.TargetLabel,
                GeneratedOn = DateTime.UtcNow,
                Results     = resultList,
            };
            await HtmlReportGenerator.GenerateWithRazorAsync(report, job.OutputFile);

            if (job.GenerateSync && !string.IsNullOrWhiteSpace(job.SyncOutputFile))
            {
                var syncOptions = new SyncScriptOptions
                {
                    SourceLabel = job.SourceLabel,
                    TargetLabel = job.TargetLabel,
                };
                var gen      = new SyncScriptGenerator();
                var script   = gen.Generate(resultList, syncOptions, comparer.LastSourceSnapshot);
                var syncText = gen.Render(script, syncOptions);
                await File.WriteAllTextAsync(Substitute(job.SyncOutputFile), syncText);
            }

            Log.Information("   ✅ {Name}: {Total} objects — {Drift} drift", job.Name,
                resultList.Count, mismatchCount + missingInTargetCount + missingInSourceCount);

            return new BatchJobResult(job.Name, job.OutputFile,
                matchCount, mismatchCount, missingInTargetCount, missingInSourceCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ {Name}: comparison failed", job.Name);
            return new BatchJobResult(job.Name, job.OutputFile, 0, 0, 0, 0,
                Failed: true, FailReason: ex.Message);
        }
    }

    private static string Substitute(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return Regex.Replace(value, @"\$\{([^}]+)\}",
            m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");
    }
}
