using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace ZMA.Licensing;

public static class MachineFingerprint
{
    public static string Get()
    {
        var parts = new List<string>();

        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);
            if (drive != null)
                parts.Add(drive.VolumeLabel ?? "");
        }
        catch { }

        try
        {
            parts.Add(Environment.MachineName);
            parts.Add(Environment.OSVersion.VersionString);
            parts.Add(Environment.ProcessorCount.ToString());
        }
        catch { }

        try
        {
            using var mc = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            foreach (var o in mc.Get())
            {
                if (o["ProcessorId"] is string id)
                    parts.Add(id);
            }
        }
        catch { }

        try
        {
            using var mc = new ManagementObjectSearcher("SELECT MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = true");
            foreach (var o in mc.Get())
            {
                if (o["MACAddress"] is string mac)
                    parts.Add(mac);
            }
        }
        catch { }

        var raw = string.Join("|", parts.Where(p => !string.IsNullOrEmpty(p)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
