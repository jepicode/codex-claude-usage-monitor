using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CodexClaudeUsageWidget.Models;

namespace CodexClaudeUsageWidget.Services;

public sealed record ProviderTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string? AccountId);

public sealed class LocalStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexClaudeUsageWidget");
    private string CredentialsPath => Path.Combine(_directory, "credentials.bin");
    private string QuotaCachePath => Path.Combine(_directory, "quota-cache.json");
    private readonly object _quotaCacheLock = new();


    public Dictionary<string, ProviderTokens> ReadCredentials()
    {
        if (!File.Exists(CredentialsPath)) return new Dictionary<string, ProviderTokens>();
        var protectedBytes = File.ReadAllBytes(CredentialsPath);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Dictionary<string, ProviderTokens>>(plainBytes)
               ?? new Dictionary<string, ProviderTokens>();
    }

    public void WriteCredentials(Dictionary<string, ProviderTokens> credentials)
    {
        Directory.CreateDirectory(_directory);
        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        WriteAtomically(CredentialsPath, protectedBytes);
    }


    public Dictionary<string, QuotaSnapshot> ReadQuotaCache()
    {
        lock (_quotaCacheLock)
        {
            if (!File.Exists(QuotaCachePath)) return new Dictionary<string, QuotaSnapshot>();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, QuotaSnapshot>>(File.ReadAllText(QuotaCachePath))
                       ?? new Dictionary<string, QuotaSnapshot>();
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                return new Dictionary<string, QuotaSnapshot>();
            }
        }
    }

    public void WriteQuotaCache(string provider, QuotaSnapshot snapshot)
    {
        lock (_quotaCacheLock)
        {
            var cache = ReadQuotaCache();
            cache[provider] = snapshot;
            Directory.CreateDirectory(_directory);
            WriteAtomically(QuotaCachePath, JsonSerializer.SerializeToUtf8Bytes(cache, JsonOptions));
        }
    }

    public void DeleteQuotaCache(string provider)
    {
        lock (_quotaCacheLock)
        {
            var cache = ReadQuotaCache();
            if (!cache.Remove(provider)) return;
            WriteAtomically(QuotaCachePath, JsonSerializer.SerializeToUtf8Bytes(cache, JsonOptions));
        }
    }

    public void DeleteCredentials()
    {
        if (File.Exists(CredentialsPath)) File.Delete(CredentialsPath);
    }

    private static void WriteAtomically(string path, byte[] contents)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, contents);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
