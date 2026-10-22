using System.Net;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RelayDesk;

public sealed class SubscriptionSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Group { get; set; } = "";
    public bool SendDeviceId { get; set; } = true;
    public DateTimeOffset? UpdatedAt { get; set; }
    public override string ToString() => Group;
}

public record SubscriptionDownload(ImportBatch Batch, string Title);

public static class Subscriptions
{
    public static string InstallationId(string folder)
    {
        var path = Path.Combine(folder, "subscription-device.id"); Directory.CreateDirectory(folder);
        if (!File.Exists(path))
        {
            try { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); var bytes = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")); file.Write(bytes); }
            catch (IOException) when (File.Exists(path)) { }
        }
        var value = File.ReadAllText(path).Trim();
        if (!Guid.TryParseExact(value,"N",out _)) throw new InvalidOperationException("Повреждён сохранённый идентификатор подписок. Он не был заменён, чтобы не занять новое место в лимите устройств.");
        return value;
    }
    public static Uri Address(string text)
    {
        text = text.Trim();
        if (text.StartsWith("happ://add/", StringComparison.OrdinalIgnoreCase)) text = text[11..];
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new FormatException("Укажи HTTPS-ссылку подписки или happ://add/https://… без логина и пароля в адресе.");
        return uri;
    }

    public static string DeviceId(string installationId, string host) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installationId + "|" + host.ToLowerInvariant()))).ToLowerInvariant();

    public static async Task<SubscriptionDownload> Download(SubscriptionSource source, string installationId, CancellationToken cancellation, HttpMessageHandler? handler = null)
    {
        using var client = handler == null ? new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = false }) : new HttpClient(handler, false);
        client.Timeout = TimeSpan.FromSeconds(30);
        var uri = Address(source.Url);
        try
        {
            for (int redirect = 0; redirect < 5; redirect++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("RelayDesk/" + AppUpdates.CurrentVersion);
                request.Headers.Accept.ParseAdd("text/plain, application/json, application/yaml;q=0.9, */*;q=0.5");
                if (source.SendDeviceId)
                {
                    request.Headers.Add("x-hwid", DeviceId(installationId, uri.IdnHost));
                    request.Headers.Add("x-device-os", "Windows");
                    request.Headers.Add("x-ver-os", Environment.OSVersion.Version.ToString());
                    request.Headers.Add("x-device-model", "RelayDesk Desktop");
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
                string Header(string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() ?? "" : "";
                if (Header("x-hwid-max-devices-reached").Equals("true", StringComparison.OrdinalIgnoreCase) || Header("x-hwid-limit").Equals("true", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Достигнут лимит устройств подписки. Освободи устройство в кабинете провайдера.");
                if (Header("x-hwid-not-supported").Equals("true", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Провайдер требует идентификатор устройства. Включи отправку HWID в настройках подписки.");
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    uri = Address(new Uri(uri, location).AbsoluteUri); continue;
                }
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Сервис подписки ответил HTTP " + (int)response.StatusCode + ".");
                if (response.Content.Headers.ContentLength > 2_000_000) throw new FormatException("Подписка больше 2 МБ.");
                await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
                using var data = new MemoryStream(); var buffer = new byte[16384];
                int length;
                while ((length = await stream.ReadAsync(buffer, cancellation)) > 0)
                { if (data.Length + length > 2_000_000) throw new FormatException("Подписка больше 2 МБ."); await data.WriteAsync(buffer.AsMemory(0,length), cancellation); }
                var title = Header("profile-title");
                if (title.StartsWith("base64:", StringComparison.OrdinalIgnoreCase)) { try { title = ImportParser.Base64(title[7..]); } catch (FormatException) { title = ""; } }
                title = new string(title.Where(c => !char.IsControl(c)).Take(80).ToArray());
                return new(Parse(new UTF8Encoding(false,true).GetString(data.ToArray())), title);
            }
            throw new InvalidOperationException("Слишком много перенаправлений подписки.");
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Не удалось загрузить подписку. Проверь сеть и доступность сервиса."); }
    }

    public static ImportBatch Parse(string body)
    {
        if (body.Length > 2_000_000) throw new FormatException("Подписка больше 2 МБ.");
        body = body.Trim().TrimStart('\uFEFF');
        if (body.Length == 0) throw new FormatException("Сервис вернул пустую подписку.");
        if (!body.StartsWith('{') && !body.StartsWith('[') && !body.Contains("://") && !body.Contains("proxies:"))
            try { body = ImportParser.Base64(body).Trim(); } catch (FormatException) { throw new FormatException("Формат подписки не распознан."); }
        if (body.StartsWith('<')) throw new FormatException("Сервис вернул веб-страницу вместо подписки.");
        var profiles = new List<Profile>(); var warnings = new List<string>();
        var candidates = body.StartsWith('[') ? (JsonNode.Parse(body) as JsonArray ?? throw new FormatException("Некорректный список JSON.")).Select(n => n?.ToJsonString() ?? "").ToArray()
            : body.StartsWith('{') || body.Contains("proxies:") ? [body]
            : body.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => !s.StartsWith('#')).ToArray();
        if (candidates.Length > 2000) throw new FormatException("В подписке больше 2000 записей.");
        var seen = new HashSet<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                ImportResult result;
                try { result = ImportParser.Parse(candidate); }
                catch (FormatException) when (candidate.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) { result = XrayLink(candidate); }
                if (result.NeedsCoreChoice) throw new FormatException();
                var p = result.Profile;
                if (p.Host is "0.0.0.0" or "127.0.0.1" or "::1" or "localhost" || p.Name.Contains("App not supported", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Сервис вернул заглушку вместо VPN. Провайдер не разрешил этому клиенту получить подписку.");
                if (seen.Add(p.Config)) profiles.Add(p);
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
            { var protocol = Uri.TryCreate(candidate,UriKind.Absolute,out var entryUri) && entryUri.Scheme.Length < 20 ? entryUri.Scheme : "JSON"; warnings.Add("Запись " + (profiles.Count + warnings.Count + 1) + " (" + protocol + "): формат или параметры не поддерживаются."); }
        }
        if (profiles.Count == 0) throw new FormatException("В подписке нет поддерживаемых серверов. " + (warnings.Count > 0 ? "Проверь формат у провайдера." : ""));
        return new(profiles, warnings);
    }

    static ImportResult XrayLink(string text)
    {
        var uri = new Uri(text); var q = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        { var parts = pair.Split('=',2); if (!q.TryAdd(Uri.UnescapeDataString(parts[0]), parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "")) throw new FormatException(); }
        string Get(string key, string fallback = "") => q.GetValueOrDefault(key,fallback);
        var known = new[] { "encryption","flow","fp","pbk","security","sid","sni","spx","type","mode","serviceName","alpn","path","host","heartbeatPeriod","allowInsecure","insecure" };
        if (q.Keys.Any(k => !known.Contains(k,StringComparer.OrdinalIgnoreCase)) || Get("encryption","none") != "none" || !Guid.TryParse(Uri.UnescapeDataString(uri.UserInfo),out _)) throw new FormatException();
        var security = Get("security","none"); if (security is not ("none" or "tls" or "reality")) throw new FormatException();
        var transport = Get("type","tcp"); if (transport is not ("tcp" or "raw" or "ws" or "grpc" or "xhttp")) throw new FormatException();
        if (transport is "tcp" or "raw" && new[] { "mode","path","host","serviceName","heartbeatPeriod" }.Any(k => Get(k).Length > 0)) throw new FormatException();
        if (transport == "grpc" && new[] { "path","host","heartbeatPeriod" }.Any(k => Get(k).Length > 0)) throw new FormatException();
        if (transport == "ws" && new[] { "mode","serviceName" }.Any(k => Get(k).Length > 0)) throw new FormatException();
        if (transport == "xhttp" && new[] { "serviceName","heartbeatPeriod" }.Any(k => Get(k).Length > 0)) throw new FormatException();
        if (security != "reality" && new[] { "spx","pbk","sid" }.Any(k => Get(k).Length > 0)) throw new FormatException();
        if (security == "none" && new[] { "sni","fp","alpn","insecure","allowInsecure" }.Any(q.ContainsKey)) throw new FormatException();
        var stream = new JsonObject { ["network"] = transport, ["security"] = security };
        if (security == "reality")
        {
            if (Get("pbk").Length == 0) throw new FormatException();
            stream["realitySettings"] = new JsonObject { ["serverName"] = Get("sni",uri.IdnHost), ["fingerprint"] = Get("fp","chrome"), ["publicKey"] = Get("pbk"), ["shortId"] = Get("sid"), ["spiderX"] = Get("spx") };
        }
        if (security == "tls")
        {
            var insecure = Get("allowInsecure",Get("insecure","0")); if (insecure is not ("0" or "1" or "false" or "true")) throw new FormatException();
            var tls = new JsonObject { ["serverName"] = Get("sni",uri.IdnHost), ["fingerprint"] = Get("fp","chrome"), ["allowInsecure"] = insecure is "1" or "true" };
            if (Get("alpn").Length > 0) tls["alpn"] = new JsonArray(Get("alpn").Split(',').Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
            stream["tlsSettings"] = tls;
        }
        if (transport == "grpc")
        {
            var mode = Get("mode","gun"); if (mode is not ("gun" or "multi")) throw new FormatException();
            stream["grpcSettings"] = new JsonObject { ["serviceName"] = Get("serviceName"), ["multiMode"] = mode == "multi" };
        }
        if (transport == "xhttp")
        {
            var mode = Get("mode","auto"); if (mode is not ("auto" or "packet-up" or "stream-up" or "stream-one")) throw new FormatException();
            stream["xhttpSettings"] = new JsonObject { ["path"] = Get("path","/"), ["host"] = Get("host"), ["mode"] = mode };
        }
        if (transport == "ws")
        {
            var ws = new JsonObject { ["path"] = Get("path","/"), ["headers"] = new JsonObject { ["Host"] = Get("host",uri.IdnHost) } };
            if (q.ContainsKey("heartbeatPeriod")) { if (!int.TryParse(Get("heartbeatPeriod"),out var seconds) || seconds is < 0 or > 3600) throw new FormatException(); ws["heartbeatPeriod"] = seconds; }
            stream["wsSettings"] = ws;
        }
        var user = new JsonObject { ["id"] = Uri.UnescapeDataString(uri.UserInfo), ["encryption"] = "none" };
        if (Get("flow").Length > 0) user["flow"] = Get("flow");
        var port = JsonTools.Port(uri.Port.ToString());
        var config = new JsonObject {
            ["log"] = new JsonObject { ["loglevel"] = "warning" },
            ["inbounds"] = new JsonArray(new JsonObject { ["listen"] = "127.0.0.1", ["port"] = 2080, ["protocol"] = "http", ["tag"] = "relay-http", ["settings"] = new JsonObject() }),
            ["outbounds"] = new JsonArray(new JsonObject { ["protocol"] = "vless", ["tag"] = "proxy", ["settings"] = new JsonObject { ["vnext"] = new JsonArray(new JsonObject { ["address"] = uri.IdnHost, ["port"] = port, ["users"] = new JsonArray(user) }) }, ["streamSettings"] = stream }) };
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        return new(new Profile { Name = name.Length > 0 ? name : "VLESS / " + uri.IdnHost, Core = "Xray", Protocol = "VLESS", Host = uri.IdnHost, Port = port, SystemProxy = true, ProxyUri = "http://127.0.0.1:2080", Config = config.ToJsonString(JsonTools.Options) }, "Xray: параметры транспорта сохранены в JSON. Для этого профиля нужно ядро Xray; TUN sing-box к нему не применяется.");
    }

    public static (int Added, int Updated) Apply(Store store, SubscriptionSource source, IEnumerable<Profile> chosen)
    {
        var entries = chosen.ToArray(); int added = 0, updated = 0;
        foreach (var p in entries)
        {
            var identity = JsonTools.Object(p.Config)["outbounds"]?.DeepClone();
            void Strip(JsonNode? node)
            {
                if (node is JsonObject obj) foreach (var field in obj.ToArray())
                { if (new[] { "id","uuid","password","publicKey","public_key","shortId","short_id" }.Contains(field.Key)) obj.Remove(field.Key); else Strip(field.Value); }
                else if (node is JsonArray array) foreach (var item in array) Strip(item);
            }
            Strip(identity);
            var key = "subscription:" + source.Id + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Core + "|" + p.Protocol + "|" + p.Name + "|" + p.Host + "|" + p.Port + "|" + identity?.ToJsonString())));
            var old = store.Profiles.FirstOrDefault(x => x.SourceKey == key);
            p.SubscriptionId = source.Id; p.SourceKey = key; p.SubscriptionGroup = source.Group;
            if (old != null)
            {
                p.Id = old.Id; p.SubscriptionGroup = old.SubscriptionGroup; p.Tun = old.Tun; p.SystemProxy = old.SystemProxy; p.LocalPort = old.LocalPort; p.Dns = old.Dns; p.AutoCandidate = old.AutoCandidate; p.QuickRoutes = old.QuickRoutes.ToList();
                store.Profiles[store.Profiles.IndexOf(old)] = p; updated++;
            }
            else { store.Profiles.Add(p); added++; }
        }
        source.UpdatedAt = DateTimeOffset.UtcNow;
        var index = store.Settings.Subscriptions.FindIndex(s => s.Id == source.Id);
        if (index < 0) store.Settings.Subscriptions.Add(source); else store.Settings.Subscriptions[index] = source;
        if (!store.Settings.SubscriptionGroups.Contains(source.Group)) store.Settings.SubscriptionGroups.Add(source.Group);
        store.Save(); return (added, updated);
    }
}
