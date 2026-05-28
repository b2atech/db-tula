using B2a.DbTula.Core.Abstractions;
using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Cli.Helpers;

public static class DatabaseIdentityGuard
{
    public static async Task ValidateBeforeComparisonAsync(
        IDatabaseSchemaProvider sourceProvider,
        IDatabaseSchemaProvider targetProvider,
        Action<int, int, string, bool>? progressLogger = null,
        CancellationToken cancellationToken = default)
    {
        progressLogger?.Invoke(0, 0, "🔐 Validating database identities before comparison...", false);

        var sourceIdentity = await GetIdentityAsync("SOURCE / QA", sourceProvider, cancellationToken);
        var targetIdentity = await GetIdentityAsync("TARGET / PROD", targetProvider, cancellationToken);

        progressLogger?.Invoke(0, 0, $"🟢 Source DB: {sourceIdentity.SafeDisplayName}", false);
        progressLogger?.Invoke(0, 0, $"🔵 Target DB: {targetIdentity.SafeDisplayName}", false);

        if (IsSameDatabase(sourceIdentity, targetIdentity))
        {
            var message =
                "❌ Source and target database appear to be the SAME database. " +
                "Comparison stopped to avoid false results. " +
                "This usually happens when QA and PROD SSH tunnels use the same localhost port, same remote DB, or same connection string.";

            progressLogger?.Invoke(0, 0, message, false);

            throw new InvalidOperationException(
                $"{message}{Environment.NewLine}" +
                $"Source: {sourceIdentity.SafeDisplayName}{Environment.NewLine}" +
                $"Target: {targetIdentity.SafeDisplayName}");
        }

        if (LooksLikeSameLocalTunnel(sourceIdentity, targetIdentity))
        {
            progressLogger?.Invoke(
                0,
                0,
                "⚠️ Warning: Source and target use the same configured localhost tunnel endpoint. Verify SSH tunnel ports.",
                false);
        }

        progressLogger?.Invoke(0, 0, "✅ Database identity validation passed.", false);
    }

    private static async Task<DatabaseIdentity> GetIdentityAsync(
        string label,
        IDatabaseSchemaProvider provider,
        CancellationToken cancellationToken)
    {
        if (provider is not IDatabaseIdentityProvider identityProvider)
        {
            return new DatabaseIdentity
            {
                ProviderName = label,
                CurrentDatabase = "Unknown",
                CurrentUser = "Unknown",
                Version = $"Provider does not implement {nameof(IDatabaseIdentityProvider)}"
            };
        }

        var identity = await identityProvider.GetDatabaseIdentityAsync(cancellationToken);

        return new DatabaseIdentity
        {
            ProviderName = label,
            ConfiguredHost = identity.ConfiguredHost,
            ConfiguredPort = identity.ConfiguredPort,
            ConfiguredDatabase = identity.ConfiguredDatabase,
            ConfiguredUsername = identity.ConfiguredUsername,
            ServerAddress = identity.ServerAddress,
            ServerPort = identity.ServerPort,
            CurrentDatabase = identity.CurrentDatabase,
            CurrentUser = identity.CurrentUser,
            Version = identity.Version
        };
    }

    private static bool IsSameDatabase(DatabaseIdentity source, DatabaseIdentity target)
    {
        if (!string.IsNullOrWhiteSpace(source.CurrentDatabase) &&
            !string.IsNullOrWhiteSpace(target.CurrentDatabase) &&
            !string.Equals(source.CurrentDatabase, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target.CurrentDatabase, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(source.Fingerprint, target.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(source.ConfiguredHost, target.ConfiguredHost, StringComparison.OrdinalIgnoreCase)
            && source.ConfiguredPort == target.ConfiguredPort
            && string.Equals(source.ConfiguredDatabase, target.ConfiguredDatabase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(source.ConfiguredUsername, target.ConfiguredUsername, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSameLocalTunnel(DatabaseIdentity source, DatabaseIdentity target)
    {
        var sourceHost = source.ConfiguredHost?.Trim();
        var targetHost = target.ConfiguredHost?.Trim();

        var bothLocalhost =
            IsLocalhost(sourceHost) &&
            IsLocalhost(targetHost);

        return bothLocalhost &&
               source.ConfiguredPort.HasValue &&
               target.ConfiguredPort.HasValue &&
               source.ConfiguredPort.Value == target.ConfiguredPort.Value;
    }

    private static bool IsLocalhost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task ValidateSingleProviderAsync(
    string label,
    IDatabaseSchemaProvider provider,
    Action<int, int, string, bool>? progressLogger = null,
    CancellationToken cancellationToken = default)
    {
        progressLogger?.Invoke(0, 0, $"🔐 Validating database identity: {label}", false);

        var identity = await GetIdentityAsync(label, provider, cancellationToken);

        progressLogger?.Invoke(0, 0, $"🟢 Database: {identity.SafeDisplayName}", false);

        if (string.Equals(identity.CurrentDatabase, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            progressLogger?.Invoke(
                0,
                0,
                $"⚠️ Provider for {label} does not support database identity validation.",
                false);
        }
    }
}