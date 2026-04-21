using System.Security.Cryptography;
using System.Text;
using ARIA.Core.Interfaces;

namespace ARIA.Service.Security;

/// <summary>
/// DPAPI-backed credential store. Each credential is stored as an encrypted blob
/// under %LOCALAPPDATA%\ARIA\creds\ and is only decryptable by the same Windows user.
/// </summary>
public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly string StorePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ARIA", "creds");

    public DpapiCredentialStore()
    {
        Directory.CreateDirectory(StorePath);
    }

    public void Save(string key, string value)
    {
        var plainBytes = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetCredPath(key), encrypted);
    }

    public string? Load(string key)
    {
        var path = GetCredPath(key);
        if (!File.Exists(path)) return null;

        var encrypted = File.ReadAllBytes(path);
        var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    public void Delete(string key)
    {
        var path = GetCredPath(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string GetCredPath(string key)
    {
        // Base64url-encode the key to produce a safe filename
        var safe = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .Replace('/', '_')
            .Replace('+', '-')
            .TrimEnd('=');
        return Path.Combine(StorePath, safe + ".cred");
    }
}
