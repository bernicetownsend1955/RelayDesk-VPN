using System.Text.Json;

namespace RelayDesk;

public enum ConnectionMode { Off, ZapretOnly, VpnOnly, Combined }

public static class RuntimePolicy
{
    public static bool LegacySubscriptionDns(Profile profile) => profile.Managed && profile.SubscriptionId.Length>0 && profile.Dns=="1.1.1.1";
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
        if(source.RoutingPreset!="rules")copy.DiscordSplit=false;
        return copy;
    }
    public static string Config(Profile p, Preferences settings, ConnectionMode mode)
    {
        var effective = Settings(settings,mode);
        if (p.Core is not ("sing-box" or "Xray")) effective.DiscordSplit = false;
        var runtimeProfile=SharedConnection.Effective(p,settings);
        if(LegacySubscriptionDns(p)){if(ReferenceEquals(runtimeProfile,p))runtimeProfile=SharedRouting.Copy(p);runtimeProfile.Dns="system";}
        var config=JsonTools.Build(runtimeProfile,effective);
        if(SharedRouting.NeedsTun(p,settings))config=ApplicationRoutes.PrepareTun(config);
        return settings.RoutingPreset=="rules"?RoutingFallback.Apply(QuickRouting.Apply(config,p.Core,SharedRouting.ForProfile(p,settings),mode == ConnectionMode.Combined),p.Core,settings.FallbackRoute,mode==ConnectionMode.Combined):RoutingPresets.Apply(config,p.Core,settings.RoutingPreset);
    }
    public static IReadOnlyList<QuickRoute> ZapretRoutes(Profile? profile,Preferences settings,ConnectionMode mode)
    {
        if(settings.RoutingPreset!="rules")return [];
        var routes=SharedRouting.Rules(profile,settings).ToList();
        if(settings.FallbackRoute=="zapret"&&mode==ConnectionMode.Combined){routes.Add(new("ip_cidr","0.0.0.0/0","@zapret",Zapret:true));routes.Add(new("ip_cidr","::/0","@zapret",Zapret:true));}
        return routes;
    }
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
        if (settings.VoiceUdpDesync) DiscordScope.AddVoiceProfiles(result, settings.ZapretExe, "19294-19344,50000-65535");
        return result.ToArray();
    }
}
