using B2A.DbTula.Api.Data;

namespace B2A.DbTula.Api.Models;

public record UserDto(Guid Id, string Email, string Name, UserRole Role);

public record DatabaseDto(
    Guid Id,
    string Name,
    DbKind DbType,
    DbEnvironment Environment,
    bool IsWriteAccount,
    Guid? ReadAccountId,
    DateTime CreatedAt
);

public record ProfileDto(
    Guid Id,
    string Name,
    string? Description,
    Guid SourceDbId,
    string SourceDbName,
    Guid TargetDbId,
    string TargetDbName,
    bool IgnoreOwnership,
    string? CronExpression,
    DateTime CreatedAt,
    Guid? LastRunId,
    string? LastRunStatus,
    DateTime? LastRunAt,
    string? LastRunSummary
);

public record ComparisonRunDto(
    Guid Id,
    Guid? ProfileId,
    string? ProfileName,
    Guid SourceDbId,
    string SourceDbName,
    Guid TargetDbId,
    string TargetDbName,
    RunStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? SummaryJson,
    string? ErrorMessage
);

public record ComparisonRunDetailDto(
    Guid Id,
    Guid? ProfileId,
    string? ProfileName,
    Guid SourceDbId,
    string SourceDbName,
    Guid TargetDbId,
    string TargetDbName,
    RunStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ResultJson,
    string? SummaryJson,
    bool HasSafeScript,
    bool HasRiskyScript,
    bool HasDestructiveScript,
    string? ErrorMessage
);

public record SyncStatementDto(
    Guid Id,
    string Category,
    string ObjectType,
    string ObjectName,
    string Sql,
    string Comment,
    int OrderIndex,
    bool IsApproved,
    bool IsApplied,
    DateTime? AppliedAt
);

public record ApplySafeResult(int SuccessCount, int FailureCount, List<string> Errors);

public record AuditLogDto(
    Guid Id,
    Guid ComparisonRunId,
    string AppliedByName,
    DateTime AppliedAt,
    string TargetDbName,
    int SuccessCount,
    int FailureCount,
    string? ErrorDetails
);

public record DriftTrendPoint(string Date, int Mismatch, int MissingInTarget);

public record DbHealthDto(
    Guid ProfileId,
    string ProfileName,
    string SourceDb,
    string TargetDb,
    string Status,    // Healthy | Drift | Unknown
    int TotalDrift,
    DateTime? LastRunAt,
    Guid? LastRunId
);

public record MetricsSummaryDto(
    int TotalRuns30d,
    int DriftRuns30d,
    int StatementsApplied,
    int DbsRegistered
);
