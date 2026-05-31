using System.Security.Cryptography;
using System.Text;

namespace B2A.DbTula.Api.Services;

/// <summary>
/// Encrypts/decrypts database connection strings using a fixed AES-256 key from config.
/// Simpler and more reliable than ASP.NET Core Data Protection for this use case.
/// Config key: Auth:EncryptionKey (32+ char string).
/// </summary>
public class CredentialService(IConfiguration config)
{
    private byte[] GetKey()
    {
        var keyStr = config["Auth:EncryptionKey"]
            ?? throw new InvalidOperationException("Auth:EncryptionKey not configured");
        // Derive 32-byte key using SHA-256
        return SHA256.HashData(Encoding.UTF8.GetBytes(keyStr));
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = GetKey();
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
            sw.Write(plainText);

        return Convert.ToBase64String(ms.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        var data = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        aes.Key = GetKey();

        var iv = data[..16];
        var cipher = data[16..];
        aes.IV = iv;

        using var ms = new MemoryStream(cipher);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);
        var result = sr.ReadToEnd();
        return ApplyHostMappings(result);
    }

    private string ApplyHostMappings(string connectionString)
    {
        var mappings = config.GetSection("HostMappings").GetChildren();
        foreach (var mapping in mappings)
            if (!string.IsNullOrEmpty(mapping.Key) && !string.IsNullOrEmpty(mapping.Value))
                connectionString = connectionString.Replace(
                    $"Host={mapping.Key}", $"Host={mapping.Value}",
                    StringComparison.OrdinalIgnoreCase);
        return connectionString;
    }
}

public static class CredentialServiceExtensions
{
    public static IServiceCollection AddCredentialService(this IServiceCollection services)
    {
        services.AddScoped<CredentialService>();
        return services;
    }
}
