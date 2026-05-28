namespace ZMA.Licensing;

public class LicenseInfo
{
    public bool Valid { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Licensee { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Tier { get; set; } = "free";
    public int MaxEntities { get; set; } = 2;
    public DateTime ExpiresAt { get; set; }
    public string Error { get; set; } = string.Empty;
}
