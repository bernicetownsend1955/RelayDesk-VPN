using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace RelayDesk;

// Persist before changing Windows settings so an interrupted session can recover next time.
public sealed class ProxyLease(string folder)
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    readonly string file = Path.Combine(folder, "proxy-recovery.json");
    public record Snapshot(int? Enable, string? Server, string? Override, string? Pac, string Applied);
    [DllImport("wininet.dll", SetLastError = true)] static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int length);
    static void Notify() { InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0); InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0); }
    public bool Active => File.Exists(file);
    public static string DisableUnavailableProfileProxy(IEnumerable<Uri> profileEndpoints)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, true) ?? throw new InvalidOperationException("Нет доступа к настройкам прокси Windows.");
        if (key.GetValue("ProxyEnable") is not int enabled || enabled != 1 || key.GetValue("AutoConfigURL") != null) return "";
        var server = key.GetValue("ProxyServer") as string ?? "";
        var endpoint = UnavailableProfileProxy(server, profileEndpoints, System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners());
        if (endpoint == null) return "";
        // Recheck before writing: another proxy may have started during inspection.
        if (key.GetValue("ProxyServer") as string != server || key.GetValue("ProxyEnable") is not int current || current != 1 || key.GetValue("AutoConfigURL") != null || System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == endpoint.Port)) return "";
        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord); Notify();
        return $"Отключён недоступный системный прокси профиля {endpoint.Host}:{endpoint.Port}. Режим только zapret использует прямое подключение.";
    }
    public static Uri? UnavailableProfileProxy(string server, IEnumerable<Uri> profileEndpoints, IEnumerable<System.Net.IPEndPoint> listeners)
    {
        if (!Uri.TryCreate(server.Contains("://", StringComparison.Ordinal) ? server : "http://" + server, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "http" || !System.Net.IPAddress.TryParse(endpoint.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip) || endpoint.UserInfo.Length > 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0 || endpoint.Port is < 1 or > 65535) return null;
        if (!profileEndpoints.Any(p => p.Scheme == "http" && p.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase) && p.Port == endpoint.Port)) return null;
        return listeners.Any(e => e.Port == endpoint.Port) ? null : endpoint;
    }
    public void Apply(Uri proxy)
    {
        if (proxy.Scheme != "http") throw new InvalidOperationException("Системному прокси нужен HTTP / mixed-вход. SOCKS5 оставь для проверки HTTPS.");
        if (Active) throw new InvalidOperationException("Сначала восстанови прежний системный прокси.");
        using var key = Registry.CurrentUser.OpenSubKey(Key, true) ?? throw new InvalidOperationException("Нет доступа к настройкам прокси Windows.");
        var applied = proxy.Host + ":" + proxy.Port;
        var snapshot = new Snapshot(key.GetValue("ProxyEnable") as int?, key.GetValue("ProxyServer") as string, key.GetValue("ProxyOverride") as string, key.GetValue("AutoConfigURL") as string, applied);
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot));
        try
        {
            key.SetValue("ProxyServer", applied); key.SetValue("ProxyOverride", "localhost;127.*;[::1]");
            key.DeleteValue("AutoConfigURL", false); key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord); Notify();
        }
        catch { Restore(true); throw; }
    }
    public string Restore(bool force = false)
    {
        if (!Active) return "";
        var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file)) ?? throw new InvalidDataException("Не удалось прочитать сохранённые настройки прокси.");
        using var key = Registry.CurrentUser.OpenSubKey(Key, true) ?? throw new InvalidOperationException("Нет доступа к настройкам прокси Windows.");
        // Respect settings changed by another application since our acquisition.
        var server = key.GetValue("ProxyServer") as string;
        if (!force && (server != snapshot.Applied || key.GetValue("ProxyEnable") is not int enable || enable != 1 || key.GetValue("AutoConfigURL") != null || key.GetValue("ProxyOverride") as string != "localhost;127.*;[::1]"))
        {
            File.Delete(file); return "Прокси изменён другой программой. Её настройки сохранены.";
        }
        Set("ProxyEnable", snapshot.Enable, RegistryValueKind.DWord); Set("ProxyServer", snapshot.Server); Set("ProxyOverride", snapshot.Override); Set("AutoConfigURL", snapshot.Pac);
        Notify(); File.Delete(file); return "Прежний системный прокси восстановлен.";
        void Set(string name, object? value, RegistryValueKind kind = RegistryValueKind.String) { if (value == null) key.DeleteValue(name, false); else key.SetValue(name, value, kind); }
    }
}
