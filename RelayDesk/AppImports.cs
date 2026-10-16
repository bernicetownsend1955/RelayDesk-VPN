using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RelayDesk;

public record ImportSource(string Path, string Kind, string Status);
public record ImportBatch(List<Profile> Profiles, List<string> Warnings);

public static class AppImports
{
    public static List<ImportSource> Discover(string extraFolder)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parent in new[] { roaming, local }) foreach (var name in new[] { "NekoBox", "nekoray", "v2rayN" }) roots.Add(System.IO.Path.Combine(parent, name));
        if (!string.IsNullOrWhiteSpace(extraFolder)) roots.Add(System.IO.Path.GetFullPath(extraFolder));
        foreach (var name in new[] { "nekobox", "nekoray", "v2rayN" }) foreach (var process in Process.GetProcessesByName(name))
        {
            try { if (process.MainModule?.FileName is string file) roots.Add(System.IO.Path.GetDirectoryName(file)!); } catch { } finally { process.Dispose(); }
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var sources = new List<ImportSource>();
        foreach (var root in roots.Where(Directory.Exists)) Walk(root, 0);
        return sources;
        void Walk(string folder, int depth)
        {
            if (depth > 4 || files.Count > 2000) return;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!files.Add(file)) continue;
                    var info = new FileInfo(file); if (info.Length is 0 or > 50_000_000) continue;
                    var name = info.Name.ToLowerInvariant(); var ext = info.Extension.ToLowerInvariant();
                    if (name is "guindb.db" or "guindb.db3") sources.Add(new(file, "v2rayN / SQLite", "Только чтение, предпросмотр перед импортом"));
                    else if (ext is ".json" or ".txt" && (name.Contains("guinconfig") || System.IO.Path.GetFileName(folder).ToLowerInvariant() is "profiles" or "beans" || name.Contains("export"))) sources.Add(new(file, "JSON / ссылки", "Предпросмотр перед импортом"));
                }
                foreach (var directory in Directory.EnumerateDirectories(folder))
                {
                    var info = new DirectoryInfo(directory); if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var name = info.Name.ToLowerInvariant();
                    if (name.Contains("lmdb") || name.Contains("leveldb")) { sources.Add(new(directory, "База NekoBox", "Прямое чтение этой версии не поддержано. Экспортируй ссылки в TXT и выбери файл.")); continue; }
                    if (name is "cache" or "qml" or "resources" or "dashboard" or "public" or "downloads" or "logs" or "temp") continue;
                    Walk(directory, depth + 1);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
    public static ImportBatch Read(string file)
    {
        file = System.IO.Path.GetFullPath(file);
        if (!File.Exists(file)) throw new FileNotFoundException("Файл источника недоступен.");
        if (new FileInfo(file).Length > 50_000_000) throw new FormatException("Файл источника больше 50 МБ.");
        var profiles = new List<Profile>(); var warnings = new List<string>();
        if (System.IO.Path.GetExtension(file).ToLowerInvariant() is ".db" or ".db3")
        {
            var rows = SqliteProfiles.Read(file);
            for (var i = 0; i < rows.Count; i++) Add(() => V2ray(rows[i]), "v2rayN:" + Get(rows[i], "IndexId", i.ToString()), i + 1);
        }
        else
        {
            var text = File.ReadAllText(file);
            if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
            {
                var root = JsonNode.Parse(text) ?? throw new FormatException("Пустой JSON источника.");
                var items = root is JsonArray array ? array : Find(root, "vmess") as JsonArray ?? Find(root, "profiles") as JsonArray ?? new JsonArray(root.DeepClone());
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i]; if (item is not JsonObject obj) continue;
                    var index = i;
                    var recordId = Get(obj, "IndexId", Find(obj, "bean") != null ? Get(obj, "id", index.ToString()) : index.ToString());
                    Add(() => Find(obj, "bean") is JsonObject ? Neko(obj) : Find(obj, "configType") != null ? V2ray(obj) : FullConfig(obj), recordId, index + 1);
                }
            }
            else
            {
                var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < lines.Length; i++) { var line = lines[i]; var index = i; Add(() => ImportParser.Parse(line).Profile, "link-line:" + index, index + 1); }
            }
        }
        if (profiles.Count == 0 && warnings.Count == 0) warnings.Add("В источнике нет профилей поддерживаемого формата.");
        return new(profiles, warnings);
        void Add(Func<Profile> read, string record, int line)
        {
            try { var p = read(); p.SourceKey = file.ToUpperInvariant() + "|" + record; profiles.Add(p); }
            catch (Exception ex) { warnings.Add($"Запись {line}: {ex.Message}"); }
        }
    }
    static JsonNode? Find(JsonNode? node, string key) => node is JsonObject obj ? obj.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value : null;
    static Profile FullConfig(JsonObject obj)
    {
        if (!obj.ContainsKey("outbounds") && !obj.ContainsKey("proxies") && !obj.ContainsKey("proxy-providers")) throw new FormatException("Это настройки интерфейса приложения, а не конфигурация сервера. Экспортируй профили или ссылки.");
        var parsed = ImportParser.Parse(obj.ToJsonString());
        if (parsed.NeedsCoreChoice) throw new FormatException("Ядро JSON неоднозначно. Используй одиночный импорт с выбором ядра.");
        return parsed.Profile;
    }
    static string Get(JsonNode? node, string key, string fallback = "") => Find(node, key)?.ToString() is string value && value.Length > 0 ? value : fallback;
    static void RejectExtras(JsonNode? obj, params string[] keys)
    {
        foreach (var key in keys) { var value = Get(obj, key); if (value.Length > 0 && value is not ("false" or "0" or "{}" or "[]")) throw new FormatException("Поле " + key + " требует экспорта полной конфигурации."); }
    }
    public static Profile V2ray(JsonObject row)
    {
        var protocol = Get(row, "ConfigType") switch { "1" or "VMess" => "vmess", "3" or "Shadowsocks" => "ss", "5" or "VLESS" => "vless", "6" or "Trojan" => "trojan", _ => throw new FormatException("Этот тип v2rayN пока импортируется полной конфигурацией.") };
        RejectExtras(row, "Cert", "CertSha", "EchConfigList", "Mldsa65Verify", "VerifyPeerCertByName", "Finalmask", "MuxEnabled", "Extra");
        var proto = Get(row, "ProtoExtra"); var extra = proto.Length > 0 ? JsonTools.Object(proto) : new JsonObject();
        var transportText = Get(row, "TransportExtra"); var transport = transportText.Length > 0 ? JsonTools.Object(transportText) : new JsonObject();
        RejectExtras(transport, "XhttpExtra", "KcpSeed", "GrpcAuthority");
        var network = Get(row, "Network", "tcp");
        var query = new Dictionary<string, string> { ["type"] = network, ["security"] = Get(row, "StreamSecurity", protocol == "trojan" ? "tls" : "none") };
        void Map(string to, string from) { var value = Get(row, from); if (value.Length > 0) query[to] = value; }
        foreach (var pair in new[] { ("sni", "Sni"), ("fp", "Fingerprint"), ("pbk", "PublicKey"), ("sid", "ShortId"), ("spx", "SpiderX"), ("alpn", "Alpn"), ("allowInsecure", "AllowInsecure") }) Map(pair.Item1, pair.Item2);
        var path = Get(transport, network == "grpc" ? "GrpcServiceName" : "Path", Get(row, "Path"));
        var host = Get(transport, "Host", Get(row, "RequestHost"));
        if (path.Length > 0) query[network == "grpc" ? "serviceName" : "path"] = path;
        if (host.Length > 0) query["host"] = host;
        var header = Get(transport, "RawHeaderType", Get(row, "HeaderType")); if (header.Length > 0) query["headerType"] = header;
        var flow = Get(extra, "Flow", Get(row, "Flow")); if (flow.Length > 0) query["flow"] = flow;
        var credential = Get(row, "Password", Get(row, "Id")); var address = Get(row, "Address"); var port = JsonTools.Port(Get(row, "Port")); var name = Get(row, "Remarks", address);
        if (protocol == "ss") return ImportParser.Parse(Link("ss", Convert.ToBase64String(Encoding.UTF8.GetBytes(Get(extra, "SsMethod", Get(row, "Security")) + ":" + credential)), address, port, name, new())).Profile;
        if (protocol == "vmess")
        {
            var vm = new JsonObject { ["v"] = "2", ["ps"] = name, ["add"] = address, ["port"] = port, ["id"] = credential, ["aid"] = Get(extra, "AlterId", Get(row, "AlterId", "0")), ["scy"] = Get(extra, "Security", Get(row, "Security", "auto")), ["net"] = network, ["path"] = path, ["host"] = host, ["type"] = header.Length > 0 ? header : "none", ["tls"] = Get(row, "StreamSecurity"), ["sni"] = Get(row, "Sni"), ["fp"] = Get(row, "Fingerprint"), ["alpn"] = Get(row, "Alpn") };
            if (query.ContainsKey("allowInsecure") || query.ContainsKey("pbk")) throw new FormatException("Этот VMess требует экспорта полной конфигурации.");
            return ImportParser.Parse("vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vm.ToJsonString()))).Profile;
        }
        return ImportParser.Parse(Link(protocol, credential, address, port, name, query)).Profile;
    }
    public static Profile Neko(JsonObject row)
    {
        var bean = Find(row, "bean") as JsonObject ?? throw new FormatException("Нет bean."); var stream = Find(bean, "stream") as JsonObject ?? new JsonObject();
        RejectExtras(bean, "c_cfg", "c_out", "plugin", "uot"); RejectExtras(stream, "cert", "ed_len", "mux_s");
        var type = Get(row, "type").ToLowerInvariant();
        var protocol = type switch { "vless" => "vless", "trojan" => "trojan", "vmess" => "vmess", "shadowsocks" or "ss" => "ss", _ => throw new FormatException("Тип NekoRay " + type + " требует экспорта полной конфигурации.") };
        var translated = new JsonObject { ["ConfigType"] = protocol switch { "vless" => 5, "trojan" => 6, "vmess" => 1, _ => 3 }, ["Address"] = Get(bean, "addr"), ["Port"] = Get(bean, "port"), ["Password"] = Get(bean, protocol == "vmess" ? "id" : "pass"), ["Remarks"] = Get(bean, "name"), ["Network"] = Get(stream, "net", "tcp"), ["StreamSecurity"] = Get(stream, "pbk").Length > 0 ? "reality" : Get(stream, "sec"), ["Sni"] = Get(stream, "sni"), ["Fingerprint"] = Get(stream, "utls"), ["PublicKey"] = Get(stream, "pbk"), ["ShortId"] = Get(stream, "sid"), ["SpiderX"] = Get(stream, "spx"), ["Alpn"] = Get(stream, "alpn"), ["Path"] = Get(stream, "path"), ["RequestHost"] = Get(stream, "host"), ["HeaderType"] = Get(stream, "h_type"), ["Flow"] = Get(bean, "flow"), ["AlterId"] = Get(bean, "aid", "0"), ["Security"] = Get(bean, protocol == "ss" ? "method" : "sec", "auto") };
        if (Get(stream, "insecure") == "true") translated["AllowInsecure"] = "true";
        var result = V2ray(translated);
        if (Get(stream, "pac_enc").Length > 0) { var root = JsonTools.Object(result.Config); root["outbounds"]![0]!["packet_encoding"] = Get(stream, "pac_enc"); result.Config = root.ToJsonString(JsonTools.Options); }
        return result;
    }
    static string Link(string scheme, string credential, string host, int port, string name, Dictionary<string, string> query) => scheme + "://" + Uri.EscapeDataString(credential) + "@" + (host.Contains(':') ? "[" + host + "]" : host) + ":" + port + (query.Count == 0 ? "" : "?" + string.Join('&', query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)))) + "#" + Uri.EscapeDataString(name);
    public static (int Added, int Updated) Apply(Store store, IEnumerable<Profile> profiles)
    {
        int added = 0, updated = 0;
        foreach (var p in profiles.GroupBy(p => p.SourceKey).Select(g => g.Last()))
        {
            var old = store.Profiles.FirstOrDefault(x => x.SourceKey.Length > 0 && x.SourceKey == p.SourceKey);
            if (old == null) { store.Profiles.Add(p); added++; }
            else { p.Id = old.Id; p.Tun = old.Tun; p.SystemProxy = old.SystemProxy; p.Dns = old.Dns; p.LocalPort = old.LocalPort; p.AutoCandidate = old.AutoCandidate; p.QuickRoutes = old.QuickRoutes.ToList(); store.Profiles[store.Profiles.IndexOf(old)] = p; updated++; }
        }
        store.Save(); return (added, updated);
    }
}

internal static class SqliteProfiles
{
    const string Lib = "winsqlite3.dll";
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int size, out IntPtr statement, IntPtr tail);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr statement);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_name(IntPtr statement, int column);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    public static List<JsonObject> Read(string file)
    {
        IntPtr db = IntPtr.Zero, statement = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(file, out db, 1, IntPtr.Zero) != 0) throw new IOException("База занята или недоступна для чтения. Закрой исходное приложение и повтори.");
            if (sqlite3_prepare_v2(db, "SELECT * FROM ProfileItem LIMIT 5000", -1, out statement, IntPtr.Zero) != 0) throw new FormatException("В базе нет таблицы ProfileItem поддерживаемой версии v2rayN.");
            var result = new List<JsonObject>(); int code;
            while ((code = sqlite3_step(statement)) == 100)
            {
                var row = new JsonObject(); for (var i = 0; i < sqlite3_column_count(statement); i++) row[Marshal.PtrToStringUTF8(sqlite3_column_name(statement, i))!] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i)) ?? "";
                result.Add(row);
            }
            if (code != 101) throw new IOException("Не удалось завершить чтение базы. Исходный файл не изменён.");
            return result;
        }
        finally { if (statement != IntPtr.Zero) sqlite3_finalize(statement); if (db != IntPtr.Zero) sqlite3_close(db); }
    }
}
