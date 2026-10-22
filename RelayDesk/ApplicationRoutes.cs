using System.IO;
using System.Text.Json.Nodes;

namespace RelayDesk;

public static class ApplicationRoutes
{
    public static string PrepareTun(string configuration)
    {
        var root = JsonTools.Object(configuration);
        if (root["inbounds"] is not null and not JsonArray || root["route"] is not null and not JsonObject)
            throw new FormatException("Некорректные inbounds / route профиля sing-box.");
        var inbounds = root["inbounds"] as JsonArray ?? new JsonArray(); root["inbounds"] ??= inbounds;
        var tun = inbounds.OfType<JsonObject>().FirstOrDefault(i => JsonTools.Text(i["type"]) == "tun");
        if (tun == null)
        {
            var tag = "relay-tun";
            while (inbounds.Any(i => JsonTools.Text(i?["tag"]) == tag)) tag += "-1";
            tun = new JsonObject { ["type"] = "tun", ["tag"] = tag, ["address"] = new JsonArray("172.19.0.1/30"), ["strict_route"] = true, ["stack"] = "mixed" };
            inbounds.Add(tun);
        }
        tun["auto_route"] = true;
        var route = root["route"] as JsonObject ?? new JsonObject(); root["route"] ??= route;
        if (route["auto_detect_interface"] == null && route["default_interface"] == null) route["auto_detect_interface"] = true;
        return root.ToJsonString(JsonTools.Options);
    }
    public static string[] Paths(IEnumerable<string> paths)
    {
        return paths.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Укажи полный путь к EXE-файлу приложения.");
            return Path.GetFullPath(path);
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string Apply(string configuration, string core, Preferences settings)
    {
        if (core == "Xray") return XrayDomains(configuration,settings);
        if (core != "sing-box") throw new InvalidOperationException("Правила EXE для полного JSON сейчас поддержаны в sing-box.");
        var root = JsonTools.Object(configuration);
        if ((root["inbounds"] as JsonArray)?.Any(n => n is JsonObject && JsonTools.Text(n["type"]) == "tun" && JsonTools.Text(n["auto_route"]) == "true") != true)
            throw new InvalidOperationException("Для правил EXE в полном JSON нужен TUN с auto_route=true. Исходная конфигурация не изменена.");
        var route = root["route"] as JsonObject ?? throw new FormatException("В конфигурации отсутствует route.");
        var original = route["rules"] as JsonArray ?? new JsonArray();
        var outbounds = root["outbounds"] as JsonArray ?? throw new FormatException("В конфигурации отсутствуют outbounds.");
        var direct = outbounds.FirstOrDefault(n => n is JsonObject && JsonTools.Text(n["type"]) == "direct" && JsonTools.Text(n["tag"]).Length > 0);
        var tag = direct == null ? "relay-exe-direct" : JsonTools.Text(direct["tag"]);
        if (direct == null)
        {
            if (outbounds.Any(n => n is JsonObject && JsonTools.Text(n["tag"]) == tag)) throw new FormatException("Тег relay-exe-direct уже занят.");
            outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = tag });
        }
        var rules = new JsonArray();
        var insertAt = original.TakeWhile(n => n is not JsonObject || JsonTools.Text(n["action"]) != "route" || JsonTools.Text(n["outbound"]) == tag).Count();
        foreach (var rule in original.Take(insertAt)) rules.Add(rule?.DeepClone());
        var paths = Paths(settings.ZapretApplications);
        if (paths.Length > 0) Add(new JsonObject { ["process_path"] = new JsonArray(paths.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["action"] = "route", ["outbound"] = tag });
        Add(new JsonObject { ["process_name"] = new JsonArray("Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe", "vesktop.exe"), ["action"] = "route", ["outbound"] = tag });
        // Preserve leading protocol setup and direct routes, then override proxy routes for the chosen apps.
        Add(new JsonObject { ["action"] = "sniff", ["sniffer"] = new JsonArray("tls", "http", "quic") });
        Add(new JsonObject { ["domain_suffix"] = new JsonArray(DiscordScope.Domains.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["action"] = "route", ["outbound"] = tag });
        var cidrs = DiscordScope.Cidrs(settings.DiscordVoiceCidrs);
        if (cidrs.Length > 0) Add(new JsonObject { ["ip_cidr"] = new JsonArray(cidrs.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["action"] = "route", ["outbound"] = tag });
        foreach (var rule in original.Skip(insertAt)) rules.Add(rule?.DeepClone());
        route["rules"] = rules;
        return root.ToJsonString(JsonTools.Options);
        void Add(JsonObject rule) { if (!rules.Any(n => JsonNode.DeepEquals(n, rule))) rules.Add(rule); }
    }
    static string XrayDomains(string configuration, Preferences settings)
    {
        var root = JsonTools.Object(configuration);
        if (root["outbounds"] is not JsonArray outbounds || root["routing"] is not null and not JsonObject || root["routing"]?["rules"] is not null and not JsonArray)
            throw new FormatException("Некорректные outbounds / routing профиля Xray.");
        if (outbounds.Any(o => o is not JsonObject || JsonTools.Text(o["protocol"]).Length == 0)) throw new FormatException("В профиле Xray выходы должны содержать protocol.");
        var direct = outbounds.OfType<JsonObject>().FirstOrDefault(o => JsonTools.Text(o["protocol"]) == "freedom" && JsonTools.Text(o["tag"]).Length > 0);
        var tag = direct == null ? "relay-discord-direct" : JsonTools.Text(direct["tag"]);
        if (direct == null)
        {
            while (outbounds.Any(o => JsonTools.Text(o?["tag"]) == tag)) tag += "-1";
            outbounds.Add(new JsonObject { ["protocol"] = "freedom", ["tag"] = tag, ["settings"] = new JsonObject() });
        }
        var routing = root["routing"] as JsonObject ?? new JsonObject(); root["routing"] ??= routing;
        var original = routing["rules"] as JsonArray ?? new JsonArray();
        var rules = new JsonArray(new JsonObject { ["type"] = "field", ["domain"] = new JsonArray(DiscordScope.Domains.Select(d => (JsonNode?)JsonValue.Create("domain:"+d)).ToArray()), ["outboundTag"] = tag });
        var cidrs = DiscordScope.Cidrs(settings.DiscordVoiceCidrs);
        if (cidrs.Length > 0) rules.Add(new JsonObject { ["type"] = "field", ["ip"] = new JsonArray(cidrs.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray()), ["outboundTag"] = tag });
        foreach (var rule in original) if (!rules.Any(existing => JsonNode.DeepEquals(existing,rule))) rules.Add(rule?.DeepClone());
        routing["rules"] = rules;
        foreach (var inbound in (root["inbounds"] as JsonArray ?? []).OfType<JsonObject>().Where(i => JsonTools.Text(i["protocol"]) is "http" or "socks"))
            if (inbound["sniffing"] == null) inbound["sniffing"] = new JsonObject { ["enabled"] = true, ["routeOnly"] = true, ["destOverride"] = new JsonArray("http","tls","quic") };
        return root.ToJsonString(JsonTools.Options);
    }
}
