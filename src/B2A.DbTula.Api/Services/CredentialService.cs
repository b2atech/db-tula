using Microsoft.AspNetCore.DataProtection;

namespace B2A.DbTula.Api.Services;

public class CredentialService(IDataProtector protector, IConfiguration config)
{
    public string Encrypt(string plainText) => protector.Protect(plainText);

    public string Decrypt(string cipherText)
    {
        var cs = protector.Unprotect(cipherText);
        return ApplyHostMappings(cs);
    }

    // Substitutes hostnames in connection strings based on HostMappings config.
    // Allows the same encrypted connection string to work across environments.
    // e.g. appsettings.Development.json: "HostMappings": { "db.prod.dgtula.com": "10.9.0.1" }
    private string ApplyHostMappings(string connectionString)
    {
        var mappings = config.GetSection("HostMappings").GetChildren();
        foreach (var mapping in mappings)
        {
            if (!string.IsNullOrEmpty(mapping.Key) && !string.IsNullOrEmpty(mapping.Value))
                connectionString = connectionString.Replace(
                    $"Host={mapping.Key}", $"Host={mapping.Value}",
                    StringComparison.OrdinalIgnoreCase);
        }
        return connectionString;
    }
}

public static class CredentialServiceExtensions
{
    public static IServiceCollection AddCredentialService(this IServiceCollection services)
    {
        services.AddSingleton<CredentialService>(sp =>
        {
            var dp = sp.GetRequiredService<IDataProtectionProvider>();
            var config = sp.GetRequiredService<IConfiguration>();
            return new CredentialService(dp.CreateProtector("dbtula.db-credentials.v1"), config);
        });
        return services;
    }
}
