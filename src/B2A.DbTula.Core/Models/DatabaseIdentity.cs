namespace B2A.DbTula.Core.Models;

public sealed class DatabaseIdentity
{
    public string ProviderName { get; init; } = string.Empty;

    public string? ConfiguredHost { get; init; }

    public int? ConfiguredPort { get; init; }

    public string? ConfiguredDatabase { get; init; }

    public string? ConfiguredUsername { get; init; }

    public string? ServerAddress { get; init; }

    public int? ServerPort { get; init; }

    public string CurrentDatabase { get; init; } = string.Empty;

    public string CurrentUser { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string SafeDisplayName =>
        $"{ProviderName} | Config={ConfiguredHost ?? "unknown"}:{ConfiguredPort?.ToString() ?? "unknown"}/{ConfiguredDatabase ?? "unknown"} | " +
        $"Actual={ServerAddress ?? "unknown"}:{ServerPort?.ToString() ?? "unknown"}/{CurrentDatabase} | User={CurrentUser}";

    public string Fingerprint =>
        $"{ServerAddress}|{ServerPort}|{CurrentDatabase}|{CurrentUser}".ToLowerInvariant();
}