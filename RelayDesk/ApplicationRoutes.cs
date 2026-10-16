using System.IO;
using System.Text.Json.Nodes;

namespace RelayDesk;

public static class ApplicationRoutes
{
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
}
