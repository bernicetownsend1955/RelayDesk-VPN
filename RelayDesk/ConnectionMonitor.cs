using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace RelayDesk;

public sealed record ConnectionRow(string Id, string Application, string Destination, string Network, string Route, string Outbound, string Rule, string Zapret, string Traffic)
{
    public string ProcessPath { get; init; } = "";
    public string Host { get; init; } = "";
    public string Address { get; init; } = "";
    public long TotalBytes { get; init; }
    public bool CanEdit { get; init; } = true;
    public bool CanEditApplication => CanEdit && (ProcessPath.Length > 0 || Application.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
}

public sealed class ConnectionMonitor : IDisposable
{
    readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
    public static (Uri Address, string Secret) Endpoint(string config)
    {
        var api = JsonTools.Object(config)["experimental"]?["clash_api"];
        var controller = JsonTools.Text(api?["external_controller"]);
        if (!Uri.TryCreate("http://" + controller, UriKind.Absolute, out var uri) || !IPAddress.TryParse(uri.Host, out var ip) || !IPAddress.IsLoopback(ip) || uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Port < 1) throw new InvalidOperationException("Для наблюдения нужен локальный Clash API sing-box. Он будет добавлен при следующем запуске VPN в этой версии.");
        return (uri, JsonTools.Text(api?["secret"]));
    }
    public static string Configure(string config, int port)
    {
        var root = JsonTools.Object(config);
        if (!string.IsNullOrEmpty(JsonTools.Text(root["experimental"]?["clash_api"]?["external_controller"]))) return config;
        if (root["experimental"] == null) root["experimental"] = new JsonObject();
        if (root["experimental"]!["clash_api"] == null) root["experimental"]!["clash_api"] = new JsonObject();
        var api = root["experimental"]!["clash_api"]!;
        api["external_controller"] = "127.0.0.1:" + port;
        api["secret"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        return root.ToJsonString(JsonTools.Options);
    }
    public async Task<List<ConnectionRow>> Read(string config, bool? zapret, string[] arguments, CancellationToken cancellation)
    {
        var endpoint = Endpoint(config);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint.Address, "/connections"));
        if (endpoint.Secret.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Secret);
        using var response = await http.SendAsync(request, cancellation); response.EnsureSuccessStatusCode();
        var data = JsonTools.Object(await response.Content.ReadAsStringAsync(cancellation));
        return Parse(data, JsonTools.Object(config), zapret, arguments);
    }
    public static List<ConnectionRow> Parse(JsonObject data, JsonObject config, bool? zapret, string[] arguments)
    {
        var types = config["outbounds"]?.AsArray().OfType<JsonObject>().Where(o => o["tag"] != null).ToDictionary(o => JsonTools.Text(o["tag"]), o => JsonTools.Text(o["type"]) == "direct" && !string.IsNullOrEmpty(JsonTools.Text(o["detour"])) ? "chain" : JsonTools.Text(o["type"])) ?? new();
        var rows = new List<ConnectionRow>();
        foreach (var item in (data["connections"] as JsonArray ?? []).OfType<JsonObject>().Take(3000))
        {
            var metadata = item["metadata"];
            var path = JsonTools.Text(metadata?["processPath"]); var app = path.Length > 0 ? Path.GetFileName(path) : JsonTools.Text(metadata?["process"]);
            if (app.Length == 0) app = "Не определено ядром";
            var host = JsonTools.Text(metadata?["host"]); var ip = JsonTools.Text(metadata?["destinationIP"]); var port = JsonTools.Text(metadata?["destinationPort"]);
            var network = JsonTools.Text(metadata?["network"]).ToUpperInvariant();
            var chains = (item["chains"] as JsonArray ?? []).Select(n => JsonTools.Text(n)).ToArray();
            var route = Classify(chains, types);
            string scope = zapret == null ? "Состояние zapret неизвестно" : zapret == false ? "В RelayDesk выключен" : route != "Напрямую" ? "Не определено для внешнего туннеля" : MatchZapret(network, port, host, arguments);
            long.TryParse(JsonTools.Text(item["upload"]), out var upload); long.TryParse(JsonTools.Text(item["download"]), out var download);
            rows.Add(new(JsonTools.Text(item["id"]), app, (host.Length > 0 ? host : ip) + ":" + port, network, route, string.Join(" → ", chains), JsonTools.Text(item["rule"]) + " " + JsonTools.Text(item["rulePayload"]), scope, $"↑ {JsonTools.Text(item["upload"])} / ↓ {JsonTools.Text(item["download"])} Б") { ProcessPath = path, Host = host, Address = ip, TotalBytes = Math.Max(0,upload) + Math.Max(0,download) });
        }
        return rows;
    }
    public static string MatchZapret(string network, string port, string host, string[] args)
    {
        if (args.Length == 0 || args.Any(a => a.StartsWith("--wf-raw") || a.StartsWith("--hostlist=") || a.StartsWith("--hostlist-exclude") || a.StartsWith("--ipset"))) return "Сложный фильтр: совпадение не определено";
        if (network != "TCP") return "UDP: обработка не подтверждена";
        var ports = args.Where(a => a.StartsWith("--wf-tcp=")).SelectMany(a => a[9..].Split(',')).ToArray();
        if (!int.TryParse(port, out var number) || !ports.Any(p => { var bounds = p.Split('-'); return int.TryParse(bounds[0], out var lo) && (bounds.Length == 1 ? number == lo : bounds.Length == 2 && int.TryParse(bounds[1], out var hi) && number >= lo && number <= hi); })) return "TCP-порт вне фильтра";
        var domains = args.Where(a => a.StartsWith("--hostlist-domains=")).SelectMany(a => a[19..].Split(',')).ToArray();
        if (domains.Length > 0 && host.Length == 0) return "Домен не определён ядром";
        if (domains.Length > 0 && !domains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase))) return "Домен вне фильтра";
        return "Совпадает с TCP-фильтром; обработка не подтверждена";
    }
    public static string Classify(string[] chains, Dictionary<string, string> types)
    {
        // An unrecognised selector or chain is not evidence of VPN or direct egress.
        var resolved = chains.Select(tag => types.GetValueOrDefault(tag, tag.Equals("DIRECT", StringComparison.OrdinalIgnoreCase) ? "direct" : "")).ToArray();
        foreach (var type in resolved)
        {
            if (type is "block" or "reject") return "Блокировка";
            if (type is "vless" or "vmess" or "trojan" or "shadowsocks" or "hysteria" or "hysteria2" or "tuic" or "wireguard" or "socks" or "http" or "ssr" or "anytls" or "snell" or "ssh" or "mieru") return "Через VPN / прокси";
        }
        if (resolved.Contains("direct")) return "Напрямую";
        return "Маршрут не определён";
    }
    public void Dispose() => http.Dispose();
}
