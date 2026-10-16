using System.Net;
using System.Net.Sockets;

namespace RelayDesk;

public static class DiscordScope
{
    public static readonly string[] Domains = ["discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discordcdn.com", "discord.media", "gateway.discord.gg"];
    public static string[] Cidrs(string text)
    {
        var result = new List<string>();
        foreach (var value in text.Split(['\r', '\n', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = value.Split('/');
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var ip)) throw new FormatException("Некорректный IP/CIDR голосового сервера: " + value);
            var max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            var prefix = parts.Length == 1 ? max : int.TryParse(parts[1], out var p) ? p : -1;
            if (prefix < (max == 32 ? 24 : 64) || prefix > max) throw new FormatException("Для Discord укажи конкретный IP или узкую подсеть: IPv4 /24–32, IPv6 /64–128.");
            result.Add(ip + "/" + prefix);
        }
        return result.Distinct().ToArray();
    }
    public static string[] Arguments(Preferences settings, string? strategy = null)
    {
        var args = (strategy ?? settings.ZapretArgs).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!settings.ZapretDiscordOnly)
        {
            if (args.Length == 0) throw new FormatException("Укажи DPI-параметры стратегии или полную команду winws.");
            if (args.All(a => a.StartsWith("--dpi-desync", StringComparison.Ordinal))) return ["--wf-tcp=1-65535", "--filter-tcp=1-65535", .. args];
            if (!args.Any(a => (a.StartsWith("--wf-tcp=", StringComparison.Ordinal) || a.StartsWith("--wf-udp=", StringComparison.Ordinal) || a.StartsWith("--wf-raw=", StringComparison.Ordinal)) && a[(a.IndexOf('=') + 1)..].Trim().Length > 0)) throw new FormatException("В полной команде winws отсутствует фильтр перехвата. Добавь --wf-tcp=порты / --wf-udp=порты либо оставь только DPI-параметры: фильтр будет добавлен программой.");
            return args;
        }
        // Scope owns every filtering option; a strategy supplies desync parameters only.
        if (args.Length == 0) throw new FormatException("Укажи DPI-параметры стратегии.");
        foreach (var arg in args)
            if (!arg.StartsWith("--dpi-desync", StringComparison.Ordinal) || arg.StartsWith("--dpi-desync-skip-nosni=", StringComparison.Ordinal) && arg != "--dpi-desync-skip-nosni=1" || arg.Contains('\r'))
                throw new FormatException("В режиме Discord в стратегии допустимы только --dpi-desync…; порты, домены и IP-фильтры добавляются программой. Полные BAT-файлы не подходят.");
        var cidrs = Cidrs(settings.DiscordVoiceCidrs);
        var result = new List<string> { "--wf-tcp=443" };
        if (cidrs.Length > 0) result.Add("--wf-udp=50000-65535");
        result.Add("--filter-tcp=443"); result.Add("--hostlist-domains=" + string.Join(',', Domains)); result.AddRange(args);
        if (cidrs.Length > 0)
        {
            result.Add("--new"); result.Add("--filter-udp=50000-65535"); result.Add("--ipset-ip=" + string.Join(',', cidrs)); result.Add("--filter-l7=discord,stun");
            result.Add("--dpi-desync=fake"); result.Add("--dpi-desync-any-protocol=1"); result.Add("--dpi-desync-cutoff=n3"); result.Add("--dpi-desync-repeats=6");
        }
        return result.ToArray();
    }
}
