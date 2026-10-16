using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace RelayDesk;

public record ServerScore(Profile Profile, long? Milliseconds, string Result);

public static class ServerPicker
{
    public static Profile? Best(IEnumerable<ServerScore> results) => results.Where(x => x.Milliseconds.HasValue).OrderBy(x => x.Milliseconds).ThenBy(x => x.Profile.Id, StringComparer.Ordinal).FirstOrDefault()?.Profile;
    public static JsonObject TestConfig(Profile p, int port)
    {
        if (!p.Managed && p.Core == "sing-box")
        {
            var raw = JsonTools.Object(p.Config);
            var servers = raw["outbounds"]?.AsArray().OfType<JsonObject>().Where(o => o.ContainsKey("server") && o.ContainsKey("server_port")).ToArray() ?? [];
            if (servers.Length != 1 || string.IsNullOrEmpty(JsonTools.Text(servers[0]["tag"]))) throw new InvalidOperationException("Для проверки JSON нужен один явно заданный удалённый сервер с тегом.");
            raw["inbounds"] = new JsonArray(new JsonObject { ["type"] = "mixed", ["listen"] = "127.0.0.1", ["listen_port"] = port });
            var resolver = raw["route"]?["default_domain_resolver"]?.DeepClone();
            raw["route"] = new JsonObject { ["final"] = JsonTools.Text(servers[0]["tag"]), ["auto_detect_interface"] = true };
            if (resolver != null) raw["route"]!["default_domain_resolver"] = resolver;
            raw.Remove("experimental"); raw["log"] = new JsonObject { ["level"] = "error" };
            return raw;
        }
        var copy = System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(p))!;
        if (!p.Managed || p.Core != "sing-box") throw new InvalidOperationException("Автовыбор поддерживает профили sing-box из ссылок. Ручные JSON не тестируются автоматически, чтобы не менять их маршруты.");
        copy.Tun = false; copy.SystemProxy = false; copy.LocalPort = port;
        var root = JsonTools.Object(JsonTools.Build(copy));
        var outbound = root["outbounds"]?.AsArray();
        if (outbound?.Count != 1 || JsonTools.Text(outbound[0]?["type"]) is "direct" or "selector" or "urltest") throw new InvalidOperationException("Нужен один удалённый сервер в профиле.");
        return root;
    }
    public static bool Eligible(Profile p)
    {
        if (p.Core != "sing-box" || !p.AutoCandidate) return false;
        try { _ = TestConfig(p, 32080); return true; } catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException) { return false; }
    }
    public static async Task<List<ServerScore>> Measure(Store store, Action<string> log, CancellationToken cancellation)
    {
        var candidates = store.Profiles.Where(Eligible).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException("Нет серверов для автовыбора. Подойдут ссылки или JSON sing-box с одним удалённым сервером.");
        var exe = store.Settings.Engines.GetValueOrDefault("sing-box", "");
        if (!File.Exists(exe)) throw new InvalidOperationException("Укажи sing-box.exe для проверки серверов.");
        var results = new List<ServerScore>();
        foreach (var p in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            Process? process = null; var file = Path.Combine(store.Folder, p.Id + ".probe.json");
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                var config = TestConfig(p, port);
                var network = DirectNetwork.Interface();
                foreach (var outbound in config["outbounds"]!.AsArray().OfType<JsonObject>().Where(o => o.ContainsKey("server"))) outbound["bind_interface"] = network.Name;
                File.WriteAllText(file, config.ToJsonString(JsonTools.Options));
                var info = new ProcessStartInfo(Path.GetFullPath(exe)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe))!, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "run", "-c", file }) info.ArgumentList.Add(arg);
                process = new Process { StartInfo = info };
                var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
                process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, e) => { if (e.Data != null && errors.Count < 8) errors.Enqueue(e.Data); };
                process.Start(); CoreLifetime.Attach(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                using var ready = CancellationTokenSource.CreateLinkedTokenSource(cancellation); ready.CancelAfter(TimeSpan.FromSeconds(8));
                while (true)
                {
                    if (process.HasExited) { process.WaitForExit(); throw new InvalidOperationException("Ядро отклонило тестовый профиль: " + ProgramJournal.Clean(string.Join(" ", errors))); }
                    try { await Diagnostics.Tcp("127.0.0.1", port, ready.Token); break; } catch (SocketException) { await Task.Delay(100, ready.Token); }
                }
                var samples = new List<long>();
                for (var i = 0; i < 3; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try { var response = await Diagnostics.Https(new Uri($"http://127.0.0.1:{port}"), store.Settings.TestUrl, cancellation); if (response.Status is >= 200 and < 300) samples.Add(response.Ms); }
                    catch (Exception) when (!cancellation.IsCancellationRequested) { }
                }
                var ordered = samples.Order().ToArray();
                var ms = samples.Count >= 2 ? (long?)((ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) / 2) : null;
                results.Add(new(p, ms, ms.HasValue ? $"{ms} мс / {samples.Count} из 3" : "Меньше двух успешных HTTPS-ответов"));
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested) { results.Add(new(p, null, ex.Message)); }
            finally
            {
                if (process != null) { try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } } catch (InvalidOperationException) { } process.Dispose(); }
                if (File.Exists(file)) File.Delete(file);
            }
            log($"Автовыбор / {p.Name}: {results[^1].Result}");
        }
        return results;
    }
}
