using System.Net;
using System.Text.Json.Nodes;

namespace RelayDesk;

public record QuickRoute(string Kind, string Value, string Outbound, bool Direct = false, bool Zapret = false);

public static class QuickRouting
{
    public static string Apply(string config, string core, IEnumerable<QuickRoute> routes, bool zapretEnabled = true)
    {
        var entries = routes.Where(r => !r.Zapret || zapretEnabled).ToArray(); if (entries.Length == 0) return config;
        if (core == "Xray") return ApplyXray(config, entries);
        if (core != "sing-box") throw new InvalidOperationException("Быстрые правила доступны для sing-box.");
        var root = JsonTools.Object(config);
        if (entries.Any(r => r.Kind is "process_path" or "process_name") && (root["inbounds"] as JsonArray)?.Any(n => JsonTools.Text(n?["type"]) == "tun" && JsonTools.Text(n?["auto_route"]) == "true") != true) throw new InvalidOperationException("Для маршрута приложения нужен TUN с auto_route=true. Включи его в настройках профиля.");
        var outbounds = root["outbounds"] as JsonArray ?? throw new FormatException("В профиле нет outbounds.");
        if (root["route"] is not null and not JsonObject || root["route"]?["rules"] is not null and not JsonArray) throw new FormatException("Некорректный раздел route / rules.");
        var route = root["route"] as JsonObject ?? new JsonObject(); root["route"] ??= route;
        var original = route["rules"] as JsonArray ?? new JsonArray();
        var result = new JsonArray();
        var leading = original.TakeWhile(n => JsonTools.Text(n?["action"]) is "sniff" or "hijack-dns" or "resolve").Count();
        foreach (var item in original.Take(leading)) result.Add(item?.DeepClone());
        foreach (var item in entries)
        {
            Validate(item);
            var tag = item.Outbound;
            if (item.Direct || item.Zapret)
            {
                var direct = outbounds.OfType<JsonObject>().Where(o => JsonTools.Text(o["type"]) == "direct" && string.IsNullOrEmpty(JsonTools.Text(o["detour"]))).Select(o => JsonTools.Text(o["tag"])).FirstOrDefault(t => t.Length > 0);
                if (direct != null) tag = direct;
                else
                {
                    tag = "relay-quick-direct";
                    while (outbounds.Any(o => JsonTools.Text(o?["tag"]) == tag)) tag += "-1";
                    outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = tag });
                }
            }
            else if (!outbounds.Any(o => JsonTools.Text(o?["tag"]) == tag && JsonTools.Text(o?["type"]) is not ("block" or "dns" or "direct"))) throw new FormatException("Выход быстрого правила удалён из профиля: " + tag);
            result.Add(new JsonObject { [item.Kind] = new JsonArray(item.Value), ["action"] = "route", ["outbound"] = tag });
        }
        foreach (var item in original.Skip(leading)) result.Add(item?.DeepClone());
        route["rules"] = result; return root.ToJsonString(JsonTools.Options);
    }
    static string ApplyXray(string config, QuickRoute[] entries)
    {
        var root = JsonTools.Object(config);
        var outbounds = root["outbounds"] as JsonArray ?? throw new FormatException("В профиле нет outbounds.");
        if (root["routing"] is not null and not JsonObject || root["routing"]?["rules"] is not null and not JsonArray) throw new FormatException("Некорректный раздел routing / rules.");
        var routing = root["routing"] as JsonObject ?? new JsonObject(); root["routing"] ??= routing;
        var result = new JsonArray();
        foreach (var entry in entries)
        {
            Validate(entry);
            if (entry.Kind is not ("domain" or "domain_suffix" or "ip_cidr")) throw new InvalidOperationException("Для Xray выбери домен или IP: журнал не определяет приложение.");
            var tag = entry.Outbound;
            if (entry.Direct || entry.Zapret)
            {
                var direct = outbounds.OfType<JsonObject>().FirstOrDefault(o => JsonTools.Text(o["protocol"]) == "freedom" && JsonTools.Text(o["tag"]).Length > 0 && o["proxySettings"] == null && string.IsNullOrEmpty(JsonTools.Text(o["streamSettings"]?["sockopt"]?["dialerProxy"])));
                if (direct != null) tag = JsonTools.Text(direct["tag"]);
                else
                {
                    tag = "relay-quick-direct";
                    while (outbounds.Any(o => JsonTools.Text(o?["tag"]) == tag)) tag += "-1";
                    outbounds.Add(new JsonObject { ["protocol"] = "freedom", ["tag"] = tag, ["settings"] = new JsonObject() });
                }
            }
            else if (!outbounds.Any(o => JsonTools.Text(o?["tag"]) == tag && JsonTools.Text(o?["protocol"]) is not ("freedom" or "blackhole" or "dns"))) throw new FormatException("Выход быстрого правила удалён из профиля: " + tag);
            result.Add(new JsonObject { ["type"] = "field", [entry.Kind == "ip_cidr" ? "ip" : "domain"] = new JsonArray(entry.Kind == "ip_cidr" ? entry.Value : (entry.Kind == "domain" ? "full:" : "domain:") + entry.Value), ["outboundTag"] = tag });
        }
        foreach (var rule in routing["rules"] as JsonArray ?? []) result.Add(rule?.DeepClone());
        routing["rules"] = result;
        return root.ToJsonString(JsonTools.Options);
    }
    public static void Validate(QuickRoute route)
    {
        if (string.IsNullOrWhiteSpace(route.Value) || string.IsNullOrWhiteSpace(route.Outbound)) throw new FormatException("Пустое быстрое правило.");
        switch (route.Kind)
        {
            case "process_path": _ = ApplicationRoutes.Paths([route.Value]); break;
            case "process_name": if (route.Value.IndexOfAny(['/', '\\', '\r', '\n']) >= 0 || !route.Value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Некорректное имя процесса."); break;
            case "domain": case "domain_suffix": if (Uri.CheckHostName(route.Value) != UriHostNameType.Dns || route.Value.Contains(':')) throw new FormatException("Некорректное имя домена."); break;
            case "ip_cidr": var parts = route.Value.Split('/'); if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || parts[1] != (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "32" : "128")) throw new FormatException("Укажи отдельный IP-адрес."); break;
            default: throw new FormatException("Неизвестный тип быстрого правила.");
        }
    }
}
