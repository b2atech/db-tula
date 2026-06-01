using B2A.DbTula.Api.Data;

namespace B2A.DbTula.Api.Models;

public record GoogleAuthRequest(string IdToken);

public record RegisterDatabaseRequest(
    string Name,
    DbKind DbType,
    DbEnvironment Environment,
    string ConnectionString,
    bool IsWriteAccount,
    Guid? ReadAccountId
);

public record CreateProfileRequest(
    string Name,
    string? Description,
    Guid SourceDbId,
    Guid TargetDbId,
    bool IgnoreOwnership,
    string? CronExpression = null
);

public record StartComparisonRequest(Guid SourceDbId, Guid TargetDbId);

public record UpdateRoleRequest(UserRole Role);

public record ToggleStatementRequest(bool IsApproved);
