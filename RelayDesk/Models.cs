using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RelayDesk;

public class Profile
{
    public string SubscriptionId { get; set; } = "";
    public string SubscriptionGroup { get; set; } = "";
    public List<QuickRoute> QuickRoutes { get; set; } = new();
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Новый профиль";
    public string Core { get; set; } = "sing-box";
    public string Executable { get; set; } = "";
    public string Config { get; set; } = "";
    public bool Managed { get; set; }
    public bool Tun { get; set; }
    public bool SystemProxy { get; set; }
    public string Dns { get; set; } = "1.1.1.1";
    public int LocalPort { get; set; } = 2080;
    public string Protocol { get; set; } = "Конфигурация";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string ProxyUri { get; set; } = "http://127.0.0.1:2080";
    public bool AutoCandidate { get; set; } = true;
    public string SourceKey { get; set; } = "";
    public override string ToString() => Name;
}

public class Preferences
{
    public bool InterfaceMotion { get; set; } = true;
    public bool AsciiBackground { get; set; } = true;
    public bool GraphiteGlass { get; set; } = true;
    public bool QuietMode { get; set; } = true;
    public bool TrayEnabled { get; set; } = true;
    public int ModernUiVersion { get; set; }
    public string LastConnectedProfileId { get; set; } = "";
    public List<SubscriptionSource> Subscriptions { get; set; } = new();
    public List<string> SubscriptionGroups { get; set; } = new();
    public bool WindowsStartup { get; set; }
    public bool StartupVpn { get; set; } = true;
    public bool StartupZapret { get; set; } = true;
    public string StartupProfileId { get; set; } = "";
    public string AppUpdateRepository { get; set; } = "";
    public int UpdatePolicyVersion { get; set; }
    public bool ExperimentalDiagnostics { get; set; }
    public string SupportHost { get; set; } = "";
    public bool SendSupportLogs { get; set; }
    public string SupportJob { get; set; } = "";
    public string LastDiscordAuditSummary { get; set; } = "";
    public string Theme { get; set; } = "Системная";
    public Dictionary<string, string> Engines { get; set; } = new();
    public string ZapretExe { get; set; } = "";
    public string ZapretArgs { get; set; } = "";
    public bool WithZapret { get; set; }
    public string TestUrl { get; set; } = "https://www.gstatic.com/generate_204";
    public bool AutoServer { get; set; }
    public bool DiscordSplit { get; set; }
    public bool ZapretDiscordOnly { get; set; } = true;
    public bool AutoZapret { get; set; }
    public string DiscordVoiceCidrs { get; set; } = "";
    public string VoiceEndpoint { get; set; } = "";
    public bool VoiceDiscoveryEnabled { get; set; }
    public bool VoiceUdpDesync { get; set; } = true;
    public uint VoiceSsrc { get; set; }
    public string StrategyList { get; set; } = "";
    public string ImportFolder { get; set; } = "";
    public bool SyncOnStart { get; set; }
    public List<string> ApprovedImportFiles { get; set; } = new();
    public List<string> ApprovedImportKeys { get; set; } = new();
    public List<string> ZapretApplications { get; set; } = new();
    public bool ConditionalRouting { get; set; } = true;
    public bool CheckComponentUpdates { get; set; } = true;
    public Dictionary<string, ComponentInstall> ComponentInstalls { get; set; } = new();
}

public sealed class Store
{
    public string Folder { get; }
    public List<Profile> Profiles { get; } = new();
    public Preferences Settings { get; private set; } = new();
    public Store(string? folder = null)
    {
        Folder = folder ?? Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelayDesk");
        Directory.CreateDirectory(Folder);
        // Never overwrite a damaged user file as part of startup.
        var file = Path.Combine(Folder, "profiles.json");
        if (File.Exists(file)) Profiles.AddRange(JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(file)) ?? throw new InvalidDataException("Файл профилей пуст."));
        if (File.Exists(Path.Combine(Folder, "settings.json"))) Settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new();
        foreach (var p in Profiles)
        {
            if (!Guid.TryParseExact(p.Id, "N", out _)) throw new InvalidDataException("Некорректный ID профиля.");
            if (!Settings.Engines.ContainsKey(p.Core) && File.Exists(p.Executable)) Settings.Engines[p.Core] = p.Executable;
        }
    }
    public void Save()
    {
        Atomic("profiles.json", Profiles);
        Atomic("settings.json", Settings);
    }
    void Atomic<T>(string name, T value)
    {
        var file = Path.Combine(Folder, name);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(value, JsonTools.Options));
        File.Move(file + ".tmp", file, true);
    }
}

public static class JsonTools
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static JsonObject Object(string text) => JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? throw new FormatException("Ожидается объект JSON.");
    public static string Text(JsonNode? node) => node?.ToString() ?? "";
    public static int Port(string value) => int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : throw new FormatException("Порт должен быть от 1 до 65535.");
    public static string Build(Profile p, Preferences? settings = null)
    {
        if (!p.Managed) return settings?.DiscordSplit == true ? ApplicationRoutes.Apply(p.Core == "sing-box" ? ApplicationRoutes.PrepareTun(p.Config) : p.Config, p.Core, settings) : p.Config;
        if (p.Core != "sing-box") throw new InvalidOperationException("Профиль из ссылки использует sing-box. Для другого ядра импортируй его полную конфигурацию.");
        var root = Object(p.Config);
        var inbounds = new JsonArray(new JsonObject { ["type"] = "mixed", ["tag"] = "relay-mixed", ["listen"] = "127.0.0.1", ["listen_port"] = Port(p.LocalPort.ToString()) });
        if (p.Tun || settings?.DiscordSplit == true) inbounds.Add(new JsonObject { ["type"] = "tun", ["tag"] = "relay-tun", ["address"] = new JsonArray("172.19.0.1/30"), ["auto_route"] = true, ["strict_route"] = true, ["stack"] = "mixed" });
        root["inbounds"] = inbounds;
        if (p.Dns != "system" && !System.Net.IPAddress.TryParse(p.Dns, out _)) throw new FormatException("В поле DNS укажи IP-адрес резолвера.");
        var resolver = p.Dns == "system" ? new JsonObject { ["type"] = "local", ["tag"] = "resolver" } : new JsonObject { ["type"] = "udp", ["tag"] = "resolver", ["server"] = p.Dns };
        root["dns"] = new JsonObject { ["servers"] = new JsonArray(resolver), ["final"] = "resolver" };
        root["route"] = new JsonObject { ["auto_detect_interface"] = true, ["default_domain_resolver"] = "resolver", ["final"] = "proxy", ["rules"] = new JsonArray(new JsonObject { ["port"] = 53, ["action"] = "hijack-dns" }) };
        if (settings?.DiscordSplit == true)
        {
            root["outbounds"]!.AsArray().Add(new JsonObject { ["type"] = "direct", ["tag"] = "discord-direct" });
            var rules = root["route"]!["rules"]!.AsArray();
            root["dns"]!["reverse_mapping"] = true;
            rules.Add(new JsonObject { ["process_name"] = new JsonArray("Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe", "vesktop.exe"), ["action"] = "route", ["outbound"] = "discord-direct" });
            var applications = ApplicationRoutes.Paths(settings.ZapretApplications);
            if (applications.Length > 0) rules.Add(new JsonObject { ["process_path"] = new JsonArray(applications.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["action"] = "route", ["outbound"] = "discord-direct" });
            rules.Add(new JsonObject { ["action"] = "sniff", ["sniffer"] = new JsonArray("tls", "http", "quic") });
            rules.Add(new JsonObject { ["domain_suffix"] = new JsonArray(DiscordScope.Domains.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["action"] = "route", ["outbound"] = "discord-direct" });
            var cidrs = DiscordScope.Cidrs(settings.DiscordVoiceCidrs);
            if (cidrs.Length > 0) rules.Add(new JsonObject { ["ip_cidr"] = new JsonArray(cidrs.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["action"] = "route", ["outbound"] = "discord-direct" });
        }
        return root.ToJsonString(Options);
    }
    public static List<(string Host, int Port)> Endpoints(Profile p)
    {
        if (p.Managed && p.Host.Length > 0) return [(p.Host, p.Port)];
        if (p.Core is not ("sing-box" or "Xray")) return p.Host.Length > 0 ? [(p.Host, Port(p.Port.ToString()))] : [];
        var root = Object(p.Config);
        var result = new List<(string Host, int Port)>();
        foreach (var node in root["outbounds"]?.AsArray() ?? [])
        {
            if (node == null) continue;
            if (p.Core == "sing-box")
            {
                var host = Text(node["server"]); var port = Text(node["server_port"]);
                if (host.Length > 0 && port.Length > 0) result.Add((host, Port(port)));
            }
            else foreach (var kind in new[] { "vnext", "servers" })
                foreach (var server in node["settings"]?[kind]?.AsArray() ?? [])
                    if (server != null) result.Add((Text(server["address"]), Port(Text(server["port"]))));
        }
        return result.Distinct().ToList();
    }
    public static Uri LocalProxy(Profile p)
    {
        var value = p.Managed ? $"http://127.0.0.1:{p.LocalPort}" : p.ProxyUri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "socks5") || !System.Net.IPAddress.TryParse(uri.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip) || uri.Port is < 1 or > 65535 || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new FormatException("Локальный прокси: http://127.0.0.1:порт или socks5://127.0.0.1:порт, без логина.");
        return uri;
    }
}
