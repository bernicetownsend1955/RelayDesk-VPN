using System.Text.Json;

namespace RelayDesk;

public enum ConnectionMode { Off, ZapretOnly, VpnOnly, Combined }

public static class RuntimePolicy
{
    public static ConnectionMode Mode(bool vpn, bool zapret) => (vpn, zapret) switch { (true, true) => ConnectionMode.Combined, (true, false) => ConnectionMode.VpnOnly, (false, true) => ConnectionMode.ZapretOnly, _ => ConnectionMode.Off };
    public static string Label(ConnectionMode mode) => mode switch { ConnectionMode.Combined => "VPN + zapret / раздельные правила", ConnectionMode.VpnOnly => "Только VPN / исходный профиль", ConnectionMode.ZapretOnly => "Только zapret / все приложения", _ => "Всё выключено" };
    public static Preferences Settings(Preferences source, ConnectionMode mode)
    {
        var copy = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(source))!;
        if (source.ConditionalRouting)
        {
            copy.DiscordSplit = mode == ConnectionMode.Combined;
            copy.ZapretDiscordOnly = mode == ConnectionMode.Combined;
        }
        return copy;
    }
    public static string Config(Profile p, Preferences settings, ConnectionMode mode) => QuickRouting.Apply(JsonTools.Build(p, Settings(settings, mode)), p.Core, p.QuickRoutes);
    public static string[] ZapretArguments(Preferences settings, ConnectionMode mode, string? strategy = null)
    {
        if (!settings.ConditionalRouting || mode != ConnectionMode.ZapretOnly) return DiscordScope.Arguments(Settings(settings, mode), strategy);
        var args = (strategy ?? settings.ZapretArgs).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Validate that user arguments cannot smuggle in host/ip/port restrictions.
        _ = DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = true }, string.Join('\n', args));
        var result = new List<string> { "--wf-tcp=1-65535", "--wf-udp=1-65535", "--filter-tcp=1-65535" };
        result.AddRange(args);
        result.AddRange(["--new", "--filter-udp=443", "--filter-l7=quic", "--dpi-desync=fake", "--dpi-desync-cutoff=n3", "--dpi-desync-repeats=6"]);
        var bin = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(string.IsNullOrWhiteSpace(settings.ZapretExe) ? "winws.exe" : settings.ZapretExe))!;
        var quic = System.IO.Path.Combine(bin, "quic_initial_www_google_com.bin");
        if (System.IO.File.Exists(quic)) result.Add("--dpi-desync-fake-quic=" + quic);
        result.AddRange(["--new", "--filter-udp=19294-19344,50000-65535", "--filter-l7=discord,stun", "--dpi-desync=fake", "--dpi-desync-any-protocol=1", "--dpi-desync-cutoff=n3", "--dpi-desync-repeats=6"]);
        var voice = System.IO.Path.Combine(bin, "ACTIVE_DISCORD_UDP.bin");
        if (System.IO.File.Exists(voice)) result.AddRange(["--dpi-desync-fake-discord=" + voice, "--dpi-desync-fake-stun=" + voice]);
        return result.ToArray();
    }
}
