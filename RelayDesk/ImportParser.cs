using System.Text;
using System.Text.Json.Nodes;

namespace RelayDesk;

public record ImportResult(Profile Profile, string Summary, bool NeedsCoreChoice = false);

public static class ImportParser
{
    static string Decode(string s) => Uri.UnescapeDataString(s);
    public static string Base64(string value)
    {
        var s = value.Trim().Replace('-', '+').Replace('_', '/');
        s = s.PadRight((s.Length + 3) / 4 * 4, '=');
        return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(s));
    }
    public static ImportResult Parse(string value)
    {
        var text = value.Trim().TrimStart('\uFEFF');
        if (text.Length == 0) throw new FormatException("Буфер пуст. Скопируй ссылку или конфигурацию.");
        if (text.Length > 2_000_000) throw new FormatException("Конфигурация больше 2 МБ. Раздели её на отдельные профили.");
        if (text.StartsWith('{')) return Json(text);
        if (text.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
        {
            var body = text[8..];
            if (!body.Contains('@')) return Vmess(JsonTools.Object(Base64(body)));
        }
        if (text.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) return Shadowsocks(text);
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "vless" or "trojan" or "vmess") return Link(uri);
        if (text.Contains("proxies:") || text.Contains("proxy-providers:") || text.Contains("mixed-port:"))
            return new(new Profile { Name = "Импорт mihomo", Core = "mihomo", Config = text, Protocol = "YAML" }, "YAML / mihomo. Содержимое проверит ядро. Адрес TCP и локальный прокси задай в настройках профиля.");
        throw new FormatException("Формат не распознан. Поддерживаются VLESS, VMess, Trojan, Shadowsocks, JSON и YAML mihomo. Для HTTPS-подписки используй «Подписки / обновить».");
    }
    static ImportResult Json(string text)
    {
        var root = JsonTools.Object(text);
        var types = root["outbounds"] as JsonArray;
        var sing = types?.Any(x => x is JsonObject o && o.ContainsKey("type")) == true || root.ContainsKey("route");
        var xray = types?.Any(x => x is JsonObject o && o.ContainsKey("protocol")) == true || root.ContainsKey("routing");
        var clash = root.ContainsKey("proxies") || root.ContainsKey("proxy-providers");
        var ambiguous = (!sing && !xray && !clash) || (sing && xray);
        var core = clash ? "mihomo" : xray && !sing ? "Xray" : "sing-box";
        var p = new Profile { Name = "Импорт JSON", Core = core, Config = root.ToJsonString(JsonTools.Options), Protocol = "JSON" };
        var endpoints = JsonTools.Endpoints(p);
        if (endpoints.Count == 1) { p.Host = endpoints[0].Host; p.Port = endpoints[0].Port; }
        if (core == "sing-box" || core == "Xray")
        {
            var input = (root["inbounds"] as JsonArray)?.FirstOrDefault(n => n is JsonObject && (core == "sing-box" ? JsonTools.Text(n["type"]) is "mixed" or "http" or "socks" : JsonTools.Text(n["protocol"]) is "http" or "socks"));
            if (input != null)
            {
                var port = JsonTools.Text(input[core == "sing-box" ? "listen_port" : "port"]);
                if (int.TryParse(port, out var local) && local > 0) p.ProxyUri = $"{(JsonTools.Text(input[core == "sing-box" ? "type" : "protocol"]) == "socks" ? "socks5" : "http")}://127.0.0.1:{local}";
            }
        }
        return new(p, $"JSON / {(ambiguous ? "нужно выбрать ядро" : core)}. Конфигурация сохраняется целиком. Удалённых адресов: {endpoints.Count}. Поля TUN и DNS её не переписывают.", ambiguous);
    }
    static Dictionary<string, string> Query(Uri uri)
    {
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2); var key = Decode(pair[0]);
            if (!q.TryAdd(key, pair.Length == 2 ? Decode(pair[1]) : "")) throw new FormatException("В ссылке повторяется параметр: " + key);
        }
        return q;
    }
    static ImportResult Link(Uri uri)
    {
        var q = Query(uri);
        var known = new HashSet<string>(new[] { "type", "security", "sni", "peer", "fp", "pbk", "sid", "spx", "flow", "encryption", "path", "host", "serviceName", "alpn", "allowInsecure", "insecure", "headerType", "packetEncoding" }, StringComparer.OrdinalIgnoreCase);
        var unknown = q.Keys.Where(k => !known.Contains(k)).ToArray();
        if (unknown.Length > 0) throw new FormatException("Не поддерживаются параметры ссылки: " + string.Join(", ", unknown) + ". Импортируй полную конфигурацию ядра.");
        string Get(string k, string fallback = "") => q.GetValueOrDefault(k, fallback);
        var protocol = uri.Scheme;
        var host = uri.IdnHost.Trim('[', ']');
        if (host.Length == 0) throw new FormatException("В ссылке нет сервера.");
        var port = JsonTools.Port(uri.Port.ToString());
        var credential = Decode(uri.UserInfo);
        if (credential.Length == 0) throw new FormatException("В ссылке нет UUID или пароля.");
        if (protocol is "vless" or "vmess" && !Guid.TryParse(credential, out _)) throw new FormatException("Некорректный UUID.");
        var outbound = new JsonObject { ["type"] = protocol, ["tag"] = "proxy", ["server"] = host, ["server_port"] = port };
        outbound[protocol == "trojan" ? "password" : "uuid"] = credential;
        if (protocol == "vmess") outbound["security"] = "auto";
        if (Get("encryption", "none") != "none") throw new FormatException("Этот вариант encryption не поддерживается импортом. Используй полную конфигурацию.");
        if (Get("headerType", "none") != "none" || Get("spx").Length > 0) throw new FormatException("headerType / spiderX требуют полной конфигурации; они не будут отброшены при импорте.");
        if (Get("flow").Length > 0) { if (protocol != "vless") throw new FormatException("flow допустим только для VLESS."); outbound["flow"] = Get("flow"); }
        if (Get("packetEncoding").Length > 0) outbound["packet_encoding"] = Get("packetEncoding");
        var security = Get("security", protocol == "trojan" ? "tls" : "none");
        if (security is not ("none" or "tls" or "reality")) throw new FormatException("Неизвестный режим TLS: " + security);
        if (security != "none")
        {
            var insecure = Get("allowInsecure", Get("insecure", "0"));
            if (insecure is not ("0" or "false" or "1" or "true")) throw new FormatException("Неизвестное значение allowInsecure.");
            var tls = new JsonObject { ["enabled"] = true, ["server_name"] = Get("sni", Get("peer", host)), ["insecure"] = insecure is "1" or "true" };
            if (Get("alpn").Length > 0) tls["alpn"] = new JsonArray(Get("alpn").Split(',').Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            if (Get("fp").Length > 0 || security == "reality") tls["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = Get("fp", "chrome") };
            if (security == "reality")
            {
                if (Get("pbk").Length == 0) throw new FormatException("У Reality отсутствует публичный ключ pbk.");
                tls["reality"] = new JsonObject { ["enabled"] = true, ["public_key"] = Get("pbk"), ["short_id"] = Get("sid") };
            }
            outbound["tls"] = tls;
        }
        else if (protocol == "trojan" || new[] { "sni", "peer", "fp", "pbk", "sid", "alpn", "insecure", "allowInsecure" }.Any(q.ContainsKey)) throw new FormatException("Параметры TLS заданы при security=none. Исправь ссылку или импортируй JSON.");
        var transport = Get("type", "tcp");
        if (transport is "ws" or "httpupgrade")
        {
            var t = new JsonObject { ["type"] = transport, ["path"] = Get("path", "/") };
            if (Get("host").Length > 0) { if (transport == "ws") t["headers"] = new JsonObject { ["Host"] = Get("host") }; else t["host"] = Get("host"); }
            outbound["transport"] = t;
        }
        else if (transport == "grpc") outbound["transport"] = new JsonObject { ["type"] = "grpc", ["service_name"] = Get("serviceName") };
        else if (transport is not ("tcp" or "raw")) throw new FormatException("Транспорт " + transport + " не поддержан импортом. Используй JSON нужного ядра.");
        if (transport is "tcp" or "raw" && new[] { "path", "host", "serviceName" }.Any(k => Get(k).Length > 0)) throw new FormatException("В TCP-ссылке есть параметры другого транспорта. Используй JSON.");
        if (transport is "ws" or "httpupgrade" && Get("serviceName").Length > 0 || transport == "grpc" && new[] { "path", "host" }.Any(k => Get(k).Length > 0)) throw new FormatException("Ссылка содержит параметры разных транспортов. Используй JSON.");
        return Managed(outbound, Decode(uri.Fragment.TrimStart('#')), host, port, protocol.ToUpperInvariant());
    }
    static ImportResult Vmess(JsonObject source)
    {
        string Get(string key, string fallback = "") => source[key]?.ToString() ?? fallback;
        var allowed = new[] { "v", "ps", "add", "port", "id", "aid", "scy", "net", "type", "host", "path", "tls", "sni", "alpn", "fp" };
        var unsupported = source.Select(x => x.Key).Where(k => !allowed.Contains(k)).ToArray();
        if (unsupported.Length > 0) throw new FormatException("Не поддерживаются поля VMess: " + string.Join(", ", unsupported));
        if (Get("type", "none") is not ("none" or "")) throw new FormatException("VMess с маскировкой заголовка импортируй как JSON.");
        if (!int.TryParse(Get("aid", "0"), out var aid) || aid != 0) throw new FormatException("Импорт поддерживает VMess AEAD (alterId=0).");
        var transport = Get("net", "tcp");
        var query = new Dictionary<string, string> { ["type"] = transport, ["security"] = Get("tls") == "tls" ? "tls" : "none" };
        if (Get("tls") is not ("" or "none" or "tls")) throw new FormatException("Неизвестный TLS VMess.");
        foreach (var k in new[] { "host", "path", "sni", "alpn", "fp" }) if (Get(k).Length > 0) query[k == "path" && transport == "grpc" ? "serviceName" : k] = Get(k);
        var host = Get("add"); var authority = host.Contains(':') ? "[" + host + "]" : host;
        var uri = new Uri("vmess://" + Uri.EscapeDataString(Get("id")) + "@" + authority + ":" + JsonTools.Port(Get("port")) + "?" + string.Join('&', query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))) + "#" + Uri.EscapeDataString(Get("ps")));
        var result = Link(uri);
        var root = JsonTools.Object(result.Profile.Config); root["outbounds"]![0]!["security"] = Get("scy", "auto"); result.Profile.Config = root.ToJsonString(JsonTools.Options);
        return result;
    }
    static ImportResult Shadowsocks(string text)
    {
        var body = text[5..];
        if (!body.Split('#')[0].Contains('@'))
        {
            var parts = body.Split('#', 2); body = Base64(parts[0]) + (parts.Length == 2 ? "#" + parts[1] : "");
        }
        var uri = new Uri("ss://" + body);
        if (Query(uri).Count > 0) throw new FormatException("Shadowsocks с плагином или параметрами импортируй полной конфигурацией.");
        var user = Decode(uri.UserInfo); if (!user.Contains(':')) user = Base64(user);
        var split = user.Split(':', 2); if (split.Length != 2 || split.Any(string.IsNullOrEmpty)) throw new FormatException("Shadowsocks: нужны метод шифрования и пароль.");
        var host = uri.IdnHost.Trim('[', ']'); var port = JsonTools.Port(uri.Port.ToString());
        var outbound = new JsonObject { ["type"] = "shadowsocks", ["tag"] = "proxy", ["server"] = host, ["server_port"] = port, ["method"] = split[0], ["password"] = split[1] };
        return Managed(outbound, Decode(uri.Fragment.TrimStart('#')), host, port, "Shadowsocks");
    }
    static ImportResult Managed(JsonObject outbound, string name, string host, int port, string protocol)
    {
        var p = new Profile { Name = string.IsNullOrWhiteSpace(name) ? protocol + " / " + host : name, Managed = true, Host = host, Port = port, Protocol = protocol, Config = new JsonObject { ["log"] = new JsonObject { ["level"] = "info" }, ["outbounds"] = new JsonArray(outbound) }.ToJsonString(JsonTools.Options) };
        var tls = outbound["tls"] as JsonObject;
        var protection = tls == null ? "без TLS" : tls["reality"] != null ? "Reality" : "TLS";
        var transport = JsonTools.Text(outbound["transport"]?["type"]); if (transport.Length == 0) transport = "TCP";
        var warning = JsonTools.Text(tls?["insecure"]) == "true" ? "\nВ ссылке отключена проверка TLS-сертификата." : "";
        return new(p, $"{protocol} / {host}:{port}\n{protection} / {transport}{warning}\nЯдро: sing-box. TUN, DNS и локальный порт доступны в форме.\nПароль и UUID скрыты; конфигурация хранится локально без шифрования.");
    }
}
