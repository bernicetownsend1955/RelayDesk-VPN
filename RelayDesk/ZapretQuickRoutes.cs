namespace RelayDesk;

public static class ZapretQuickRoutes
{
    public static string[] Apply(string[] arguments, IEnumerable<QuickRoute> routes, string executable = "", bool voiceUdpDesync = true)
    {
        var entries = routes.Where(r => r.Zapret).ToArray();
        if (entries.Length == 0) return arguments;
        foreach (var entry in entries) QuickRouting.Validate(entry);
        // Reuse the first TCP strategy, including payload files and repeat/cutoff settings.
        var sections = new List<List<string>> { new() };
        foreach (var arg in arguments)
            if (arg == "--new") sections.Add(new()); else sections[^1].Add(arg);
        var tcp = sections.FirstOrDefault(s => s.Any(a => a.StartsWith("--filter-tcp=")))
            ?? throw new InvalidOperationException("В стратегии zapret нет TCP-профиля для быстрых правил.");
        var dpi = tcp.Where(a => a.StartsWith("--dpi-desync", StringComparison.Ordinal)).ToArray();
        if (dpi.Length == 0) throw new InvalidOperationException("В TCP-профиле zapret нет DPI-параметров.");
        if (arguments.Any(a => a.StartsWith("--wf-raw", StringComparison.Ordinal)))
            throw new InvalidOperationException("Быстрые правила zapret не поддерживают пользовательский --wf-raw. Выбери стратегию с --wf-tcp / --wf-udp.");
        var result = arguments.Where(a => !a.StartsWith("--wf-tcp=") && !a.StartsWith("--wf-udp=")).ToList();
        bool process = entries.Any(r => r.Kind.StartsWith("process_"));
        bool broad = process || entries.Any(r => r.Kind == "ip_cidr");
        result.Insert(0, broad ? "--wf-tcp=1-65535" : "--wf-tcp=80,443");
        // Preserve capture ports needed by existing strategy profiles.
        var oldTcp = arguments.Where(a => a.StartsWith("--wf-tcp=")).Select(a => a[9..]);
        result[0] += oldTcp.Any() ? "," + string.Join(',', oldTcp) : "";
        var oldUdp = arguments.Where(a => a.StartsWith("--wf-udp=")).Select(a => a[9..]);
        result.Insert(1, "--wf-udp=" + (broad ? "1-65535" : "443") + (oldUdp.Any() ? "," + string.Join(',', oldUdp) : ""));
        foreach (var entry in process ? entries.Where(r => !r.Kind.StartsWith("process_")).Append(entries.First(r => r.Kind.StartsWith("process_"))) : entries)
        {
            result.Add("--new");
            bool allPorts = entry.Kind.StartsWith("process_") || entry.Kind == "ip_cidr";
            result.Add(allPorts ? "--filter-tcp=1-65535" : "--filter-tcp=80,443");
            Scope(result, entry); result.AddRange(dpi);
            if (!voiceUdpDesync) continue;
            result.Add("--new"); result.Add("--filter-udp=" + (allPorts ? "1-65535" : "443"));
            Scope(result, entry);
            result.AddRange([allPorts ? "--filter-l7=quic" : "--filter-l7=quic,discord,stun", "--dpi-desync=fake", "--dpi-desync-any-protocol=1", "--dpi-desync-cutoff=n3", "--dpi-desync-repeats=6"]);
            if (allPorts)
            {
                // Keep QUIC separate: a cached STUN profile with n3 would otherwise
                // win over the voice handshake profiles appended below.
                DiscordScope.AddVoiceProfiles(result, executable, entry.Kind == "ip_cidr" ? "1-65535" : "19294-19344,50000-65535", entry.Kind == "ip_cidr" ? [entry.Value] : null);
            }
        }
        return result.ToArray();
    }

    static void Scope(List<string> arguments, QuickRoute route)
    {
        if (route.Kind is "domain" or "domain_suffix") arguments.Add("--hostlist-domains=" + (route.Kind == "domain" ? "^" : "") + route.Value);
        else if (route.Kind == "ip_cidr") arguments.Add("--ipset-ip=" + route.Value);
    }
}
