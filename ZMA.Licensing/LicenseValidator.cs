using System.Net.Http.Json;
using System.Text.Json;

namespace ZMA.Licensing;

public class LicenseValidator
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zma");

    private static readonly string CacheFile = Path.Combine(ConfigDir, "license");
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public string ServerUrl { get; set; } = Environment.GetEnvironmentVariable("ZMA_LICENSE_SERVER")
        ?? "https://zk-hybrid-architecture-production.up.railway.app";

    public LicenseInfo Cached { get; private set; } = new();

    public LicenseValidator()
    {
        LoadCache();
    }

    public bool IsRegistered => Cached.Valid && Cached.ExpiresAt > DateTime.UtcNow;

    public int MaxEntities => Cached.Valid ? Math.Max(Cached.MaxEntities, 2) : 2;

    public bool CanMigrateEntityCount(int count) => count <= MaxEntities;

    public async Task<LicenseInfo> RegisterAsync(string key)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var fingerprint = MachineFingerprint.Get();
            var response = await http.PostAsJsonAsync($"{ServerUrl}/api/license/validate", new
            {
                key,
                machineFingerprint = fingerprint
            });

            var result = await response.Content.ReadFromJsonAsync<LicenseInfo>();
            if (result is null)
                return Fail("Invalid server response");

            result.Valid = result.Valid && !string.IsNullOrEmpty(result.Licensee);
            Cached = result;

            if (result.Valid)
                SaveCache(result);

            return result;
        }
        catch (Exception ex)
        {
            return Fail($"Could not reach license server: {ex.Message}");
        }
    }

    public async Task<LicenseInfo> RefreshAsync()
    {
        var cached = ReadCache();
        if (cached is null || string.IsNullOrEmpty(cached.Key))
            return Fail("No license registered. Run 'zma register --key <YOUR_KEY>'");

        return await RegisterAsync(cached.Key);
    }

    // ─── Cache management ───

    private void LoadCache()
    {
        var cached = ReadCache();
        if (cached is not null)
            Cached = cached;
    }

    private LicenseInfo? ReadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            var meta = JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(CacheFile));
            if (meta is null) return null;
            if (meta.CachedAt + CacheTtl < DateTime.UtcNow) return null; // expired cache
            return meta.License;
        }
        catch
        {
            return null;
        }
    }

    private void SaveCache(LicenseInfo license)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var meta = new CacheMeta { CachedAt = DateTime.UtcNow, License = license };
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(meta));
        }
        catch { }
    }

    private static LicenseInfo Fail(string error) => new()
    {
        Valid = false,
        Tier = "free",
        MaxEntities = 2,
        Error = error
    };

    private class CacheMeta
    {
        public DateTime CachedAt { get; set; }
        public LicenseInfo License { get; set; } = new();
    }
}
