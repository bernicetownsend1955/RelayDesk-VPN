using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using RelayDesk;

if (args.Length == 3 && args[0] is "--subscription-probe" or "--subscription-runtime-check")
{
    try
    {
        var source = new SubscriptionSource { Url = File.ReadAllText(args[1]).Trim() };
        var result = args[0] == "--subscription-runtime-check" ? new SubscriptionDownload(new ImportBatch(System.Text.Json.JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(args[1]))!,new()), "") : await Subscriptions.Download(source, Subscriptions.InstallationId(args[2]), CancellationToken.None);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Profiles=result.Batch.Profiles.Count, Warnings=result.Batch.Warnings.Count, Protocols=result.Batch.Profiles.Select(p => p.Protocol).Distinct() }));
        File.WriteAllText(Path.Combine(args[2],"subscription-review","parsed-profiles.json"),System.Text.Json.JsonSerializer.Serialize(result.Batch.Profiles));
        var settings = JsonTools.Object(File.ReadAllText(Path.Combine(args[2],"settings.json")));
        int verified=0, unavailable=0;
        foreach (var profile in result.Batch.Profiles)
        {
            var executable = profile.Core == "sing-box" ? JsonTools.Text(settings["Engines"]?["sing-box"]) : Path.Combine(args[2],"subscription-review","xray-qa","xray.exe");
            if (!File.Exists(executable)) { unavailable++; continue; }
            var path = Path.Combine(args[2],"subscription-review",profile.Id+".json");
            var config=RuntimePolicy.Config(profile,new Preferences { ConditionalRouting=true, ZapretApplications=[@"C:\\Apps\\Fixture.exe"] },ConnectionMode.Combined); ProgramJournal.RegisterConfig(config); File.WriteAllText(path,config);
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true,CreateNoWindow=true };
            foreach (var argument in profile.Core == "sing-box" ? new[] { "check","-c",path } : new[] { "run","-test","-c",path }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); var output = await stdout; var error = await stderr;
            if (process.ExitCode != 0) throw new Exception("Subscription config check failed ("+profile.Core+"): "+ProgramJournal.Clean(output+error));
            verified++;
        }
        Console.WriteLine($"Real core configuration checks: {verified}; core unavailable: {unavailable}. No connection started.");
    }
    catch (Exception ex) { Console.WriteLine(ProgramJournal.Clean(ex.Message)); Environment.ExitCode=1; }
    return;
}

if (args.Length == 3 && args[0] == "--startup-registration-fixture")
{
    var resultPath = Path.Combine(args[1], "startup-registration-result.json");
    var taskName = "RelayDesk.Startup.Test." + Guid.NewGuid().ToString("N");
    try
    {
        StartupRegistration.Apply(true, args[2], taskName);
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!; scheduler.Connect();
        dynamic task = scheduler.GetFolder("\\").GetTask(taskName);
        task.Enabled = false;
        var taskXml = System.Xml.Linq.XDocument.Parse((string)task.Xml);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        if (taskXml.Descendants(ns + "RunLevel").Single().Value != "HighestAvailable" || taskXml.Descendants(ns + "LogonType").Single().Value != "InteractiveToken" || taskXml.Descendants(ns + "Arguments").Single().Value != "--windows-startup" || taskXml.Descendants(ns + "Delay").Single().Value != "PT15S") throw new Exception("Registered startup task differs.");
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(task); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(scheduler);
        StartupRegistration.Apply(false, args[2], taskName);
        StartupRegistration.Apply(false, args[2], taskName);
        File.WriteAllText(resultPath, "{\"registeredAndRemoved\":true,\"neverRun\":true}");
    }
    catch (Exception ex) { File.WriteAllText(resultPath, System.Text.Json.JsonSerializer.Serialize(new { error = ex.ToString() })); Environment.ExitCode = 1; }
    finally { StartupRegistration.Apply(false, args[2], taskName); }
    return;
}

int count = 0;
Environment.SetEnvironmentVariable("RELAYDESK_DATA_DIR", Path.Combine(Path.GetTempPath(), "RelayDesk-JournalChecks-" + Guid.NewGuid().ToString("N")));
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is FormatException or InvalidOperationException or System.Text.Json.JsonException) { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
const string id = "11111111-2222-4333-8444-555555555555";
ProgramJournal.RegisterConfig("{\"password\":\"fixture-credential\",\"tls\":{\"short_id\":\"ab12\"}}");
Check(!ProgramJournal.Clean("core error fixture-credential ab12").Contains("fixture-credential"), "Known profile credentials removed from free text");
Check(!ProgramJournal.Clean("vless://secret@host:443 password=hello " + id).Contains("hello"), "Links, passwords and UUID removed");
ProgramJournal.Write("password=fixture-secret");
Check(!string.Join("", ProgramJournal.Snapshot()).Contains("fixture-secret"), "Stored journal redacted before disk write");
File.WriteAllText(Path.Combine(ProgramJournal.Folder, "2000-01-01.ndjson"), "old");
File.SetLastWriteTimeUtc(Path.Combine(ProgramJournal.Folder, "2000-01-01.ndjson"), DateTime.UtcNow.AddDays(-30));
ProgramJournal.Write("retention");
Check(!File.Exists(Path.Combine(ProgramJournal.Folder, "2000-01-01.ndjson")), "Journal retention removes old files");
var pending = ProgramJournal.Pending();
Check(pending.Logs.Length > 0 && ProgramJournal.Pending().Logs.Length == pending.Logs.Length, "Unacknowledged logs stay pending after transmission failure");
ProgramJournal.Acknowledge(pending);
Check(ProgramJournal.Pending().Logs.Length == 0, "Only acknowledged log records removed from upload queue");
ProgramJournal.Write("next batch");
Check(ProgramJournal.Pending().Logs.Length == 1, "New journal records upload incrementally");
var vless = ImportParser.Parse($"vless://{id}@example.com:443?security=reality&sni=www.example.org&fp=chrome&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sid=ab&flow=xtls-rprx-vision#My%20server");
Check(vless.Profile.Managed && vless.Profile.Name == "My server" && vless.Profile.Host == "example.com", "VLESS Reality preview");
var json = JsonTools.Object(JsonTools.Build(vless.Profile));
Check(JsonTools.Text(json["outbounds"]![0]!["tls"]!["reality"]!["public_key"]) == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "Reality parameters preserved");
Check(JsonTools.Text(json["outbounds"]![0]!["flow"]) == "xtls-rprx-vision", "Vision preserved");
Check(json["inbounds"]!.AsArray().Count == 1, "No TUN by default");
vless.Profile.Tun = true; vless.Profile.Dns = "9.9.9.9"; vless.Profile.LocalPort = 31080;
json = JsonTools.Object(JsonTools.Build(vless.Profile));
Check(json["inbounds"]!.AsArray().Count == 2, "TUN generated");
Check(JsonTools.Text(json["dns"]!["servers"]![0]!["server"]) == "9.9.9.9", "DNS field applied");
Check(JsonTools.Text(json["inbounds"]![0]!["listen_port"]) == "31080", "Local port applied");
Check(JsonTools.Text(json["route"]!["auto_detect_interface"]) == "true", "TUN loop prevention");
var ws = ImportParser.Parse($"vless://{id}@[2001:db8::1]:8443?type=ws&security=tls&host=cdn.example.org&path=%2Fws#IPv6");
json = JsonTools.Object(ws.Profile.Config);
Check(ws.Profile.Host == "2001:db8::1" && ws.Profile.Port == 8443, "IPv6 address");
Check(JsonTools.Text(json["outbounds"]![0]!["transport"]!["headers"]!["Host"]) == "cdn.example.org", "WebSocket Host");
Check(JsonTools.Text(json["outbounds"]![0]!["transport"]!["path"]) == "/ws", "WebSocket path decoded");
var grpc = ImportParser.Parse($"vless://{id}@example.com:443?type=grpc&security=tls&serviceName=service");
Check(JsonTools.Text(JsonTools.Object(grpc.Profile.Config)["outbounds"]![0]!["transport"]!["service_name"]) == "service", "gRPC service");
var trojan = ImportParser.Parse("trojan://p%40ss%3Aword@example.com:443?sni=example.org#Trojan");
Check(JsonTools.Text(JsonTools.Object(trojan.Profile.Config)["outbounds"]![0]!["password"]) == "p@ss:word", "Trojan password encoding");
var ss = ImportParser.Parse("ss://" + B64("aes-128-gcm:p@ss:word").TrimEnd('=') + "@example.com:8388#SS");
Check(JsonTools.Text(JsonTools.Object(ss.Profile.Config)["outbounds"]![0]!["password"]) == "p@ss:word", "SIP002 password with colon");
var legacy = ImportParser.Parse("ss://" + B64("aes-128-gcm:password@example.com:8388") + "#Old");
Check(legacy.Profile.Host == "example.com", "Legacy Shadowsocks");
var vmess = ImportParser.Parse("vmess://" + B64(new JsonObject { ["v"] = "2", ["ps"] = "VMess", ["add"] = "example.com", ["port"] = "443", ["id"] = id, ["aid"] = "0", ["scy"] = "auto", ["net"] = "ws", ["type"] = "none", ["path"] = "/ws", ["tls"] = "tls" }.ToJsonString()));
Check(vmess.Profile.Protocol == "VMESS" && vmess.Profile.Managed, "VMess AEAD base64");
var raw = ImportParser.Parse("{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":2081}],\"outbounds\":[{\"type\":\"vless\",\"server\":\"example.com\",\"server_port\":443}],\"route\":{\"rules\":[{\"domain_suffix\":[\"example.org\"],\"outbound\":\"direct\"}]}}");
Check(!raw.Profile.Managed && raw.Profile.Core == "sing-box" && raw.Profile.Host == "example.com", "sing-box JSON detection");
Check(raw.Profile.ProxyUri == "http://127.0.0.1:2081", "Local inbound detection");
Check(JsonTools.Build(raw.Profile) == raw.Profile.Config, "Raw JSON remains intact");
var xray = ImportParser.Parse("{\"outbounds\":[{\"protocol\":\"vless\",\"settings\":{\"vnext\":[{\"address\":\"example.net\",\"port\":443}]}}]}");
Check(xray.Profile.Core == "Xray" && xray.Profile.Host == "example.net", "Xray JSON detection");
Check(ImportParser.Parse("{\"log\":{}}").NeedsCoreChoice, "Ambiguous JSON requires choice");
Check(ImportParser.Parse("mixed-port: 7890\nproxies: []").Profile.Core == "mihomo", "YAML detection");
Reject(() => ImportParser.Parse($"vless://{id}@example.com:443?type=xhttp"), "Unsupported transport rejected");
Reject(() => ImportParser.Parse($"vless://{id}@example.com:443?unknown=value"), "Unknown parameters rejected");
Reject(() => ImportParser.Parse($"vless://{id}@example.com:443?type=tcp&type=ws"), "Duplicate parameters rejected");
Reject(() => ImportParser.Parse($"vless://{id}@example.com:443?security=reality"), "Missing Reality key rejected");
Reject(() => ImportParser.Parse("vless://bad@example.com:443"), "Bad UUID rejected");
Reject(() => ImportParser.Parse($"vless://{id}@example.com"), "Missing port rejected");
Reject(() => ImportParser.Parse("ss://" + B64("aes-128-gcm:password") + "@example.com:8388?plugin=v2ray-plugin"), "SS plugin rejected");
Reject(() => ImportParser.Parse("{invalid json}"), "Broken JSON rejected");
Reject(() => ImportParser.Parse(""), "Empty clipboard rejected");
Reject(() => JsonTools.LocalProxy(new Profile { ProxyUri = "http://example.org:8080" }), "Remote proxy cannot be used as local test endpoint");
Reject(() => JsonTools.LocalProxy(new Profile { ProxyUri = "http://127.0.0.1:8080/path" }), "Proxy URL path rejected");
var temp = Path.Combine(Path.GetTempPath(), "RelayDesk-checks-" + Guid.NewGuid().ToString("N"));
var store = new Store(temp); store.Profiles.Add(vless.Profile); store.Save(); var restored = new Store(temp);
Check(restored.Profiles.Single().Tun && restored.Profiles.Single().LocalPort == 31080, "Settings survive save and reload");
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var localPort = ((IPEndPoint)listener.LocalEndpoint).Port;
var accept = listener.AcceptTcpClientAsync(); var latency = await Diagnostics.Tcp("127.0.0.1", localPort); using var accepted = await accept; listener.Stop();
Check(latency >= 0, "TCP probe against real local listener");
try { await Diagnostics.Https(new Uri("http://127.0.0.1:" + localPort), "https://example.com"); throw new Exception("Direct fallback detected"); }
catch (System.Net.Http.HttpRequestException) { Check(true, "HTTPS fails closed when proxy unavailable"); }
Console.WriteLine($"{count} checks passed. Artifacts: {temp}");
Check(ComponentUpdates.Newer("v1.14.3", "1.13.9"), "Update compares numeric versions");
Check(!ComponentUpdates.Newer("v1.14.3", "1.15.0"), "Update never proposes older stable over newer local version");
Check(!ComponentUpdates.Newer("v1.14.3", "1.14.3"), "Equal stable version not marked new");
Check(ComponentUpdates.Newer("v1.14.3", "1.14.3-beta.1"), "Stable replaces prerelease of same version");
Check(ComponentUpdates.NewerFlowseal("1.9.9d", "1.9.9c") && ComponentUpdates.NewerFlowseal("1.10.0", "1.9.9d"), "Flowseal letter suffixes and numeric versions ordered correctly");
Check(ComponentUpdates.RevisionLabel("flowseal:1.10.3") == "Flowseal 1.10.3", "Flowseal version label not truncated");
var flowRelease = JsonTools.Object("""
{"tag_name":"1.10.3","prerelease":false,"draft":false,"assets":[{"name":"zapret-discord-youtube-1.10.3.zip","size":100,"digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","browser_download_url":"https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.3/zapret-discord-youtube-1.10.3.zip"}]}
""");
var flowMissing = ComponentUpdates.FlowsealRelease(flowRelease, "", null);
Check(flowMissing.Available && flowMissing.Files.Count == 1 && flowMissing.Files[0].Hash.Length == 64 && !flowMissing.Files[0].GitBlob, "Flowseal release uses ZIP and SHA256 metadata without binary download");
var flowBin = Path.Combine(temp, "flowseal", "bin"); Directory.CreateDirectory(flowBin);
foreach (var name in new[] { "winws.exe", "cygwin1.dll", "WinDivert.dll", "WinDivert64.sys" }) File.WriteAllText(Path.Combine(flowBin, name), "fixture");
var flowExe = Path.Combine(flowBin, "winws.exe");
Check(ComponentUpdates.FlowsealExecutable(Path.GetDirectoryName(flowBin)!) == flowExe, "Flowseal archive resolves bin executable and dependencies");
var flowReceipt = new ComponentInstall("flowseal:1.10.3", flowExe, "", DateTimeOffset.UtcNow);
Check(!ComponentUpdates.FlowsealRelease(flowRelease, flowExe, flowReceipt).Available, "Same Flowseal release does not trigger repeat installation");
Check(ComponentUpdates.FlowsealRelease(flowRelease, flowExe, flowReceipt with { Revision = "flowseal:1.10.2" }).Available, "Older managed Flowseal release receives update offer");
Check(!ComponentUpdates.FlowsealRelease(flowRelease, flowExe, flowReceipt with { Revision = "flowseal:1.11.0" }).Available, "Newer managed Flowseal release never downgraded");
Check(ComponentUpdates.FlowsealRelease(flowRelease, flowExe, flowReceipt with { Revision = new string('b', 40) }).Current.Contains("не определена"), "Legacy bol-van installation treated as unknown Flowseal version");
File.Delete(Path.Combine(flowBin, "WinDivert.dll"));
Check(ComponentUpdates.FlowsealRelease(flowRelease, flowExe, flowReceipt).Available, "Missing Flowseal dependency offers repair installation");
try { ComponentUpdates.FlowsealExecutable(Path.GetDirectoryName(flowBin)!); throw new Exception("Incomplete bundle accepted"); } catch (InvalidDataException) { Check(true, "Incomplete Flowseal archive rejected"); }
foreach (var invalid in new[] { "hash", "url", "prerelease" })
{
    var bad = (JsonObject)flowRelease.DeepClone();
    if (invalid == "hash") bad["assets"]![0]!["digest"] = "";
    if (invalid == "url") bad["assets"]![0]!["browser_download_url"] = "https://example.org/package.zip";
    if (invalid == "prerelease") bad["prerelease"] = true;
    try { ComponentUpdates.FlowsealRelease(bad, "", null); throw new Exception("Invalid release accepted"); } catch (InvalidDataException) { Check(true, "Flowseal rejects invalid " + invalid); }
}
var packageRoot = Path.Combine(temp, "package"); Directory.CreateDirectory(packageRoot);
Check(ComponentUpdates.Destination(packageRoot, "sub/winws.exe").StartsWith(packageRoot), "Package target stays inside staging folder");
foreach (var badPath in new[] { "../escape.exe", "/escape.exe", "C:/escape.exe", "sub/../../escape.exe", "sub\\escape.exe", "core.exe:ads" })
{
    try { ComponentUpdates.Destination(packageRoot, badPath); throw new Exception("Unsafe package path accepted"); } catch (InvalidDataException) { Check(true, "Package path traversal rejected"); }
}
var blob = Path.Combine(temp, "hash-fixture.txt"); File.WriteAllText(blob, "hello\n", new UTF8Encoding(false));
Check(await ComponentUpdates.HashFile(blob, true, default) == "ce013625030ba8dba906f756967f9e9ca394464a", "Git blob hash verification");
Check(await ComponentUpdates.HashFile(blob, false, default) == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(blob))).ToLowerInvariant(), "SHA256 package verification");
var hostileZip = Path.Combine(temp, "unsafe.zip");
using (var archive = System.IO.Compression.ZipFile.Open(hostileZip, System.IO.Compression.ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("../escape.exe").Open()); writer.Write("blocked"); }
try { ComponentUpdates.Extract(hostileZip, packageRoot); throw new Exception("Unsafe ZIP accepted"); } catch (InvalidDataException) { Check(!File.Exists(Path.Combine(temp, "escape.exe")), "ZIP traversal cannot write outside package"); }
var originalRaw = new JsonObject { ["dns"] = new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["type"] = "https", ["server"] = "1.1.1.1", ["tag"] = "dns-0", ["detour"] = "proxy" }), ["final"] = "dns-0" }, ["inbounds"] = new JsonArray(new JsonObject { ["type"] = "tun", ["auto_route"] = true }), ["outbounds"] = new JsonArray(new JsonObject { ["type"] = "direct", ["tag"] = "direct" }, new JsonObject { ["type"] = "vless", ["tag"] = "proxy" }), ["route"] = new JsonObject { ["final"] = "proxy", ["rules"] = new JsonArray(new JsonObject { ["action"] = "hijack-dns", ["protocol"] = "dns" }, new JsonObject { ["action"] = "route", ["outbound"] = "proxy", ["process_name"] = new JsonArray("vesktop.exe") }) } };
var exeSettings = new Preferences { DiscordSplit = true, ZapretApplications = [@"C:\Apps\Client\Client.exe"] };
Check(RuntimePolicy.Mode(false, false) == ConnectionMode.Off && RuntimePolicy.Mode(false, true) == ConnectionMode.ZapretOnly && RuntimePolicy.Mode(true, false) == ConnectionMode.VpnOnly && RuntimePolicy.Mode(true, true) == ConnectionMode.Combined, "All four connection modes resolve correctly");
var conditional = new Preferences { ConditionalRouting = true, DiscordSplit = true, ZapretDiscordOnly = true, ZapretArgs = "--dpi-desync=multisplit\n--dpi-desync-split-pos=1,midsld", ZapretApplications = [@"C:\Apps\Client\Client.exe"] };
var rawText = originalRaw.ToJsonString();
var noTunSource = originalRaw.DeepClone().AsObject(); noTunSource["inbounds"] = new JsonArray(new JsonObject { ["type"] = "mixed", ["tag"] = "relay-tun", ["listen"] = "127.0.0.1", ["listen_port"] = 2080 });
var noTunProfile = new Profile { Config = noTunSource.ToJsonString() };
var autoTunRuntime = JsonTools.Object(RuntimePolicy.Config(noTunProfile,conditional,ConnectionMode.Combined));
Check(autoTunRuntime["inbounds"]!.AsArray().Count(i=>JsonTools.Text(i?["type"])=="tun" && JsonTools.Text(i?["auto_route"])=="true")==1, "Full JSON combined mode adds runtime TUN automatically");
Check(JsonTools.Text(autoTunRuntime["inbounds"]![1]!["tag"])=="relay-tun-1" && JsonNode.DeepEquals(autoTunRuntime["dns"],noTunSource["dns"]), "Runtime TUN avoids tag collisions and preserves DNS");
Check(noTunProfile.Config==noTunSource.ToJsonString() && RuntimePolicy.Config(noTunProfile,conditional,ConnectionMode.VpnOnly)==noTunProfile.Config, "Runtime TUN never changes saved JSON or VPN-only profile");
var disabledTun = originalRaw.DeepClone().AsObject(); disabledTun["inbounds"]![0]!["auto_route"]=false;
var enabledTun = JsonTools.Object(ApplicationRoutes.PrepareTun(disabledTun.ToJsonString()));
Check(enabledTun["inbounds"]!.AsArray().Count==1 && JsonTools.Text(enabledTun["inbounds"]![0]!["auto_route"])=="true", "Existing TUN is enabled without duplication");
Check(JsonNode.DeepEquals(enabledTun,JsonTools.Object(ApplicationRoutes.PrepareTun(enabledTun.ToJsonString()))), "Runtime TUN preparation is idempotent");
Check(RuntimePolicy.Config(new Profile { Config = rawText }, conditional, ConnectionMode.VpnOnly) == rawText, "VPN-only restores original raw JSON byte for byte");
var combo = JsonTools.Object(RuntimePolicy.Config(new Profile { Config = rawText }, conditional, ConnectionMode.Combined));
Check(combo["route"]!["rules"]!.AsArray().Any(r => r?["process_path"] != null), "Combined mode inserts selected EXE exceptions");
Check(JsonNode.DeepEquals(originalRaw["dns"], combo["dns"]), "Combined mode retains user DNS");
var broad = RuntimePolicy.ZapretArguments(conditional, ConnectionMode.ZapretOnly);
Check(broad.Contains("--wf-tcp=1-65535") && broad.Contains("--wf-udp=1-65535"), "Standalone zapret covers all TCP and UDP ports");
Check(!broad.Any(a => a.StartsWith("--hostlist") || a.StartsWith("--ipset")), "Standalone zapret removes Discord host and IP scope");
var combinedZapret = RuntimePolicy.ZapretArguments(conditional, ConnectionMode.Combined);
Check(combinedZapret.Any(a => a.StartsWith("--hostlist-domains=")), "Combined mode restores Discord zapret scope");
Check(conditional.DiscordSplit && conditional.ZapretDiscordOnly && conditional.ConditionalRouting, "Mode changes do not mutate saved preferences");
var manual = new Preferences { ConditionalRouting = false, DiscordSplit = true };
Check(RuntimePolicy.Settings(manual, ConnectionMode.VpnOnly).DiscordSplit, "Disabling conditional routing retains manual settings");
var modifiedRaw = JsonTools.Object(ApplicationRoutes.Apply(originalRaw.ToJsonString(), "sing-box", exeSettings));
Check(JsonNode.DeepEquals(originalRaw["dns"], modifiedRaw["dns"]), "Raw DNS block preserved including proxy detour");
Check(JsonNode.DeepEquals(originalRaw["outbounds"], modifiedRaw["outbounds"]), "Raw server credentials and outbounds preserved");
Check(JsonNode.DeepEquals(modifiedRaw, JsonTools.Object(ApplicationRoutes.Apply(modifiedRaw.ToJsonString(), "sing-box", exeSettings))), "Raw EXE policy is idempotent");
Check(JsonTools.Text(modifiedRaw["route"]!["rules"]![0]!["action"]) == "hijack-dns", "Existing DNS setup retains priority");
var rawRules = modifiedRaw["route"]!["rules"]!.AsArray();
Check(rawRules.Any(n => n?["process_path"] != null && JsonTools.Text(n["outbound"]) == "direct"), "Selected EXE gets exact path direct route");
var directDiscordIndex = rawRules.ToList().FindIndex(n => n?["process_name"] is JsonArray list && list.Any(v => JsonTools.Text(v) == "vesktop.exe") && JsonTools.Text(n["outbound"]) == "direct");
var proxyDiscordIndex = rawRules.ToList().FindIndex(n => JsonTools.Text(n?["outbound"]) == "proxy");
Check(directDiscordIndex >= 0 && directDiscordIndex < proxyDiscordIndex, "Vesktop direct exception wins over old VPN rule");
Reject(() => ApplicationRoutes.Paths(["Client.exe"]), "EXE rules reject ambiguous relative path");
Reject(() => ApplicationRoutes.Apply("{\"inbounds\":[],\"route\":{},\"outbounds\":[]}", "sing-box", exeSettings), "Raw EXE routing requires TUN");
Reject(() => ApplicationRoutes.Apply(originalRaw.ToJsonString(), "Xray", exeSettings), "Unsupported raw core not silently rewritten");
var scope = new Preferences { DiscordSplit = true, ZapretDiscordOnly = true, DiscordVoiceCidrs = "203.0.113.5\n2001:db8::1/128" };
var split = JsonTools.Object(JsonTools.Build(ws.Profile, scope));
Check(split["inbounds"]!.AsArray().Any(n => JsonTools.Text(n?["type"]) == "tun"), "Discord split automatically enables TUN");
Check(split["route"]!["rules"]!.AsArray().Any(n => n?["process_name"] != null && JsonTools.Text(n["outbound"]) == "discord-direct"), "Discord process routes direct");
Check(JsonTools.Text(split["route"]!["final"]) == "proxy", "Other traffic stays on VPN");
Check(split["route"]!["rules"]!.AsArray().Any(n => n?["domain_suffix"] != null), "Discord domain route");
Check(split["route"]!["rules"]!.AsArray().Any(n => n?["ip_cidr"] != null), "Voice CIDRs routed direct");
var scopedArgs = DiscordScope.Arguments(scope, "--dpi-desync=multisplit\n--dpi-desync-split-pos=1,midsld");
var unscopedDpi = DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = false }, "--dpi-desync=multisplit\n--dpi-desync-split-pos=1,midsld");
Check(unscopedDpi.Contains("--wf-tcp=1-65535") && unscopedDpi.Contains("--filter-tcp=1-65535"), "DPI-only manual strategy always receives capture and TCP filters");
Reject(() => DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = false }, "--filter-tcp=443\n--dpi-desync=fake"), "Full manual command without capture filter rejected before winws starts");
Reject(() => DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = false }, "--wf-tcp=\n--dpi-desync=fake"), "Empty manual capture filter rejected");
var manualCommand = "--wf-tcp=80,443\n--filter-tcp=443\n--dpi-desync=fake";
Check(DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = false }, manualCommand).SequenceEqual(manualCommand.Split('\n')), "Complete manual capture command remains intact");
foreach (var mode in new[] { ConnectionMode.ZapretOnly, ConnectionMode.Combined })
foreach (var conditionalFlag in new[] { true, false })
foreach (var scoped in new[] { true, false })
{
    var policyArgs = RuntimePolicy.ZapretArguments(new Preferences { ConditionalRouting = conditionalFlag, ZapretDiscordOnly = scoped, ZapretArgs = "--dpi-desync=multisplit" }, mode);
    Check(policyArgs.Any(a => a.StartsWith("--wf-tcp=")), "Capture filter exists for " + mode + "/conditional=" + conditionalFlag + "/scoped=" + scoped);
}
Check(scopedArgs.Contains("--filter-tcp=443") && scopedArgs.Any(x => x.StartsWith("--hostlist-domains=discord.com")), "zapret TCP scoped to Discord hosts");
Check(scopedArgs.Contains("--filter-udp=1-65535") && scopedArgs.Any(x => x.StartsWith("--ipset-ip=203.0.113.5/32")), "zapret UDP scoped to explicit voice IPs");
Check(scopedArgs.Contains("--wf-udp=1-65535") && scopedArgs.Contains("--filter-l7=discord,stun"), "Voice scope captures low WebRTC ports while retaining protocol and IP filters");
var staleVoice = new Preferences { VoiceEndpoint="203.0.113.5:19326", VoiceSsrc=17918 };
Check(!ZapretAudit.ShouldProbeVoice(staleVoice), "Existing WebRTC endpoint never triggers legacy UDP discovery by default");
staleVoice.VoiceDiscoveryEnabled=true;
Check(ZapretAudit.ShouldProbeVoice(staleVoice), "Legacy UDP discovery requires explicit opt-in and both session values");
staleVoice.VoiceSsrc=0;
Check(!ZapretAudit.ShouldProbeVoice(staleVoice), "Incomplete legacy session is marked untested");
Check(!DiscordScope.Arguments(new Preferences(), "--dpi-desync=multisplit").Any(x => x.StartsWith("--wf-udp")), "No broad UDP capture without voice IPs");
Reject(() => DiscordScope.Arguments(scope, "--wf-tcp=1-65535\n--dpi-desync=fake"), "Strategy cannot widen scope");
Reject(() => DiscordScope.Arguments(scope, "--new\n--dpi-desync=fake"), "Strategy cannot inject unscoped profile");
Reject(() => DiscordScope.Cidrs("0.0.0.0/0"), "Voice scope rejects blanket CIDRs");
Check(ZapretAudit.Parse(ZapretAudit.DefaultStrategies).Count == 3, "Strategy list parsed");
var packet = ZapretAudit.DiscoveryPacket(1234);
Check(packet.Length == 74 && packet[0] == 0 && packet[1] == 1 && packet[3] == 70, "Discord UDP discovery request structure");
packet[1] = 2;
Check(ZapretAudit.ValidDiscovery(packet, 1234) && !ZapretAudit.ValidDiscovery(packet, 1235), "UDP discovery SSRC validated");
var probeConfig = ServerPicker.TestConfig(vless.Profile, 32080);
Check(probeConfig["inbounds"]!.AsArray().Count == 1 && JsonTools.Text(probeConfig["inbounds"]![0]!["listen_port"]) == "32080", "Server probe disables TUN and uses isolated port");
Reject(() => ServerPicker.TestConfig(raw.Profile, 32080), "Raw JSON without outbound tag excluded from probing");
var rawProbeSource = JsonTools.Object(raw.Profile.Config); rawProbeSource["outbounds"]![0]!["tag"] = "proxy";
rawProbeSource["route"]!["default_domain_resolver"] = "dns-test";
var rawProbeProfile = new Profile { Config = rawProbeSource.ToJsonString() };
var originalRawText = rawProbeProfile.Config;
var rawProbe = ServerPicker.TestConfig(rawProbeProfile, 32080);
Check(rawProbe["inbounds"]!.AsArray().Count == 1 && JsonTools.Text(rawProbe["inbounds"]![0]!["type"]) == "mixed", "Raw JSON probe removes TUN and uses isolated local input");
Check(rawProbeProfile.Config == originalRawText && JsonTools.Text(rawProbe["route"]!["final"]) == "proxy", "Raw JSON probe leaves original routes untouched and tests remote outbound");
Check(JsonTools.Text(rawProbe["route"]!["default_domain_resolver"]) == "dns-test", "Raw JSON probe preserves outbound DNS resolver");
Check(ServerPicker.Best(new[] { new ServerScore(vless.Profile, null, "failed"), new ServerScore(ws.Profile, 140, "ok"), new ServerScore(grpc.Profile, 80, "ok") }) == grpc.Profile, "Best server excludes failed candidates");
Check(ServerPicker.Best(new[] { new ServerScore(vless.Profile, null, "failed") }) == null, "No false winner when all servers fail");
var vRow = new JsonObject { ["configType"] = 5, ["indexId"] = "server-1", ["remarks"] = "Fixture", ["address"] = "example.com", ["port"] = 443, ["id"] = id, ["network"] = "tcp", ["streamSecurity"] = "tls", ["sni"] = "example.org" };
Check(AppImports.V2ray(vRow).Managed && AppImports.V2ray(vRow).Host == "example.com", "v2rayN legacy profile converted");
var nRow = new JsonObject { ["type"] = "vless", ["id"] = 4, ["bean"] = new JsonObject { ["name"] = "Neko fixture", ["addr"] = "example.net", ["port"] = 443, ["pass"] = id, ["stream"] = new JsonObject { ["net"] = "ws", ["sec"] = "tls", ["path"] = "/ws", ["sni"] = "example.org" } } };
Check(AppImports.Neko(nRow).Host == "example.net", "NekoRay bean converted");
var sourceFile = Path.Combine(temp, "guiNConfig.json"); File.WriteAllText(sourceFile, new JsonObject { ["vmess"] = new JsonArray(vRow) }.ToJsonString());
var sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourceFile)));
var batch = AppImports.Read(sourceFile); Check(batch.Profiles.Count == 1 && batch.Warnings.Count == 0, "v2rayN source read");
var imported = AppImports.Apply(store, batch.Profiles); var linked = store.Profiles.Single(p => p.SourceKey.Length > 0); linked.Tun = true; linked.LocalPort = 23080;
var refreshed = AppImports.Apply(store, AppImports.Read(sourceFile).Profiles);
Check(imported.Added == 1 && refreshed.Added == 0 && refreshed.Updated == 1, "Refresh deduplicates source records");
Check(store.Profiles.Single(p => p.SourceKey.Length > 0).Tun && store.Profiles.Single(p => p.SourceKey.Length > 0).LocalPort == 23080, "Refresh preserves local connection options");
Check(sourceHash == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourceFile))), "Source app config never modified");
var settingsFile = Path.Combine(temp, "not-profiles.json"); File.WriteAllText(settingsFile, "{\"theme\":\"dark\"}");
Check(AppImports.Read(settingsFile).Profiles.Count == 0 && AppImports.Read(settingsFile).Warnings.Count == 1, "App UI settings not mistaken for server config");
var sqliteFile = Path.Combine(temp, "guiNDB.db");
FixtureDb.Create(sqliteFile, id);
Check(AppImports.Read(sqliteFile).Profiles.Count == 1, "Read-only SQLite v2rayN import");
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    foreach (var p in new[] { vless.Profile, ws.Profile, grpc.Profile, trojan.Profile, ss.Profile, vmess.Profile }) File.WriteAllText(Path.Combine(args[0], p.Protocol + "-" + p.Id + ".json"), JsonTools.Build(p));
}
if (args.Length > 1)
{
    store.Settings.Engines["sing-box"] = args[1];
    var engine = new EngineSession(store, Console.WriteLine);
    foreach (var p in new[] { vless.Profile, ws.Profile, grpc.Profile, trojan.Profile, ss.Profile, vmess.Profile }) { await engine.Check(p); Check(!engine.Busy, "Real sing-box accepts " + p.Protocol); }
    store.Settings.DiscordSplit = true; store.Settings.DiscordVoiceCidrs = "203.0.113.5/32";
    await engine.Check(ws.Profile); Check(true, "Real sing-box accepts Discord split routes");
    var rawWithoutTun = new Profile { Config = RuntimePolicy.Config(new Profile { Config = JsonTools.Build(ws.Profile), Managed = false }, conditional, ConnectionMode.Combined), Managed = false };
    await engine.Check(rawWithoutTun); Check(true, "Real sing-box accepts runtime TUN added to full JSON");
    store.Settings.DiscordSplit = false;
    var listenerPort = new TcpListener(IPAddress.Loopback, 0); listenerPort.Start(); var port = ((IPEndPoint)listenerPort.LocalEndpoint).Port; listenerPort.Stop();
    var direct = new Profile { Managed = true, Name = "Test direct", LocalPort = port, Config = "{\"outbounds\":[{\"type\":\"direct\",\"tag\":\"proxy\"}]}" };
    await engine.Connect(direct, false);
    Check(engine.ProxyRunning && engine.ActiveProfile == direct, "Real engine launches and local listener becomes ready");
    Check(engine.Mode == ConnectionMode.VpnOnly, "Live VPN session reports VPN-only mode");
    Check(new Store(store.Folder).Settings.LastConnectedProfileId == direct.Id, "Successful connection persists last server across Store reload");
    var monitorRuntime = File.ReadAllText(Path.Combine(store.Folder, direct.Id + ".runtime.json"));
    using (var liveMonitor = new ConnectionMonitor())
    {
        var targetListener = new TcpListener(IPAddress.Loopback, 0); targetListener.Start();
        try
        {
            var targetPort = ((IPEndPoint)targetListener.LocalEndpoint).Port;
            using var tunnel = new TcpClient(); await tunnel.ConnectAsync(IPAddress.Loopback, port);
            var request = Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{targetPort} HTTP/1.1\r\nHost: 127.0.0.1:{targetPort}\r\n\r\n");
            await tunnel.GetStream().WriteAsync(request);
            using var monitorAccepted = await targetListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var activeConnections = await liveMonitor.Read(monitorRuntime, false, [], default);
            Check(activeConnections.Any(r => r.Destination.EndsWith(":" + targetPort) && r.Route == "Напрямую"), "Real sing-box API exposes active direct connection through authenticated loopback feed");
            var apiEndpoint = ConnectionMonitor.Endpoint(monitorRuntime);
            using var unauthorized = new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler { UseProxy = false });
            using var unauthorizedResponse = await unauthorized.GetAsync(new Uri(apiEndpoint.Address, "/connections"));
            Check(unauthorizedResponse.StatusCode == HttpStatusCode.Unauthorized, "Generated monitor API denies requests without secret");
        }
        finally { targetListener.Stop(); }
    }
    var oldPid = engine.Proxy!.Id;
    store.Settings.ZapretExe = Path.Combine(temp, "missing-winws.exe");
    try { await engine.StartZapret(); throw new Exception("Missing zapret accepted"); } catch (InvalidOperationException) { Check(engine.ProxyRunning && engine.Proxy!.Id == oldPid && engine.Mode == ConnectionMode.VpnOnly && !engine.Busy, "Failed combined-mode preflight leaves live VPN intact"); }
    await engine.StopAll();
    Check(!engine.ProxyRunning && engine.ActiveProfile == null, "Real engine stops without orphan process");
    Check(engine.Mode == ConnectionMode.Off, "Stop all resets mode");
    await engine.Connect(direct, false); engine.Proxy!.Kill(true);
    for (var attempt = 0; attempt < 50 && (engine.Mode != ConnectionMode.Off || engine.Busy || engine.ProxyRunning); attempt++) await Task.Delay(100);
    Check(engine.Mode == ConnectionMode.Off && !engine.ProxyRunning && !engine.Busy, "Unexpected VPN exit reconciles to off without restart loop");
    var pickStore = new Store(Path.Combine(temp, "picker")); pickStore.Settings.Engines["sing-box"] = args[1];
    var refused = ImportParser.Parse($"vless://{id}@127.0.0.1:9?security=none#Refused").Profile; pickStore.Profiles.Add(refused);
    using (var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
    {
        var scores = await ServerPicker.Measure(pickStore, Console.WriteLine, probeTimeout.Token);
        Check(scores.Count == 1 && ServerPicker.Best(scores) == null, "Real server scan rejects unreachable server");
        Check(!File.Exists(Path.Combine(pickStore.Folder, refused.Id + ".probe.json")), "Server scan removes temporary config");
    }
    store.Settings.Engines["sing-box"] = Path.Combine(temp, "missing.exe");
    try { await engine.Check(direct); throw new Exception("Missing engine accepted"); } catch (InvalidOperationException) { Check(!engine.Busy, "Missing engine does not leave session busy"); }
    Console.WriteLine($"TOTAL: {count} checks passed.");
}

if (args.Length > 2)
{
    var daily = File.ReadAllText(args[2]); var rootDaily = JsonTools.Object(daily);
    var prepared = ApplicationRoutes.Apply(daily, "sing-box", new Preferences { DiscordSplit = true });
    var rootPrepared = JsonTools.Object(prepared);
    Check(JsonNode.DeepEquals(rootDaily["dns"], rootPrepared["dns"]), "User daily DNS preserved exactly");
    Check(JsonNode.DeepEquals(rootDaily["outbounds"], rootPrepared["outbounds"]), "User daily server preserved exactly");
    foreach (var rule in rootDaily["route"]!["rules"]!.AsArray()) Check(rootPrepared["route"]!["rules"]!.AsArray().Any(n => JsonNode.DeepEquals(n, rule)), "User original route preserved");
    if (args.Length > 3)
    {
        var template = JsonTools.Object(File.ReadAllText(args[3]));
        Check(template["rules"]!.AsArray().All(r => rootDaily["route"]!["rules"]!.AsArray().Any(n => JsonNode.DeepEquals(n, r))), "Route template already embedded in full user JSON");
    }
    var output = Path.Combine(args[0], "daily-discord.json"); File.WriteAllText(output, prepared);
    File.WriteAllText(Path.Combine(args[0], "daily-original.json"), daily);
    Check(RuntimePolicy.Config(new Profile { Config = daily }, new Preferences { ConditionalRouting = true, DiscordSplit = true }, ConnectionMode.VpnOnly) == daily, "User daily profile exactly restored in VPN-only mode");
    var dailyStore = new Store(Path.Combine(temp, "daily")); dailyStore.Settings.Engines["sing-box"] = args[1];
    await new EngineSession(dailyStore, Console.WriteLine).Check(new Profile { Name = "Daily fixture", Core = "sing-box", Config = prepared });
    Console.WriteLine($"FINAL: {count} checks passed. Prepared config: {output}");
}

if (args.Contains("--updates") || Environment.GetEnvironmentVariable("RELAYDESK_CHECK_UPDATES") == "1")
{
    var updateStore = new Store(Path.Combine(temp, "update-check")); updateStore.Settings.Engines["sing-box"] = args.Length > 1 ? args[1] : ""; updateStore.Save();
    var beforeSettings = File.ReadAllText(Path.Combine(updateStore.Folder, "settings.json"));
    var updater = new ComponentUpdates(updateStore); using var updateTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
    var singRelease = await updater.CheckSingBox(updateTimeout.Token); var zapretRelease = await updater.CheckZapret(updateTimeout.Token);
    Check(singRelease.Files.Count == 1 && singRelease.Files[0].Hash.Length == 64, "Live sing-box metadata includes pinned SHA256");
    Check(zapretRelease.Revision.StartsWith("flowseal:") && zapretRelease.Files.Count == 1 && zapretRelease.Files[0].Name.EndsWith(".zip") && zapretRelease.Files[0].Hash.Length == 64, "Live Flowseal release metadata pins ZIP by SHA256");
    Check(!Directory.Exists(Path.Combine(updateStore.Folder, "components")), "Automatic metadata check downloads no component binaries");
    Check(beforeSettings == File.ReadAllText(Path.Combine(updateStore.Folder, "settings.json")), "Automatic update check does not activate or replace components");
    Console.WriteLine($"UPDATES TOTAL: {count} checks passed.");
}

var strategyBin = Path.Combine(temp, "Flowseal with spaces", "bin"); Directory.CreateDirectory(strategyBin);
File.WriteAllText(Path.Combine(strategyBin, "tls.bin"), "fixture");
File.WriteAllText(Path.Combine(strategyBin, "winws.exe"), "fixture");
var batFixture = "@echo off\n--filter-tcp=443 --hostlist=\"%LISTS%list-google.txt\" --dpi-desync=fake --new ^\n--filter-tcp=80,443 --hostlist=\"%LISTS%list-general.txt\" --dpi-desync=fake,multisplit --dpi-desync-fake-tls=\"%BIN%tls.bin\" --dpi-desync-repeats=8 --new ^\n--filter-udp=443 --dpi-desync=fake";
var adapted = FlowsealStrategies.ParseTcp(batFixture, strategyBin);
Check(adapted.Contains("--dpi-desync=fake,multisplit") && adapted.Contains(Path.Combine(strategyBin, "tls.bin")) && !adapted.Contains("--filter") && !adapted.Contains("--hostlist"), "Flowseal imports general TCP parameters with full fake paths and owns filtering separately");
Check(!adapted.Contains('"') && adapted.Contains("Flowseal with spaces"), "Flowseal paths with spaces remain single argument values");
Check(FlowsealStrategies.ParseTcp(batFixture.Replace("%BIN%tls.bin", "%BIN%../outside.bin"), strategyBin) == "", "Flowseal fake file traversal rejected");
Check(FlowsealStrategies.ParseTcp(batFixture.Replace("%BIN%tls.bin", "%UNKNOWN%file.bin"), strategyBin) == "", "Unresolved BAT variables rejected");
File.WriteAllText(Path.Combine(Path.GetDirectoryName(strategyBin)!, "general (ALT11).bat"), batFixture);
File.WriteAllText(Path.Combine(Path.GetDirectoryName(strategyBin)!, "general.bat"), batFixture);
var importedStrategies = FlowsealStrategies.Load(Path.Combine(strategyBin, "winws.exe"));
Check(importedStrategies.Count == 1 && ZapretAudit.Parse(FlowsealStrategies.Format(importedStrategies)).Count == 1, "Flowseal deduplicates strategies and formats a valid audit list");
Check(FlowsealStrategies.Resolve(Path.Combine(strategyBin, "winws.exe"), ZapretAudit.DefaultStrategies).All(s => s.Name.StartsWith("Flowseal /")), "Legacy three-strategy default migrates to installed Flowseal before audit");
Check(FlowsealStrategies.Resolve(Path.Combine(strategyBin, "winws.exe"), "Custom\n--dpi-desync=fake")[0].Name == "Custom", "Custom strategy lists are preserved during migration");
foreach (var udpCase in new[] { RuntimePolicy.ZapretArguments(new Preferences { ZapretArgs = "--dpi-desync=multisplit" }, ConnectionMode.ZapretOnly), DiscordScope.Arguments(new Preferences { DiscordVoiceCidrs = "203.0.113.5" }, "--dpi-desync=multisplit") })
{
    var segments = string.Join('\n', udpCase).Split("--new");
    Check(segments.Where(s => s.Contains("--dpi-desync-any-protocol=1")).All(s => s.Split('\n',StringSplitOptions.TrimEntries).Any(a=>a.StartsWith("--dpi-desync-cutoff=n") && int.TryParse(a["--dpi-desync-cutoff=n".Length..],out var limit) && limit is > 0 and <= 32)), "Generated any-protocol UDP profiles always limit packet count");
}
var successfulAudit = new[] { new DiscordReport("Baseline", "error", "error", "not checked", null, false), new DiscordReport("ALT11", "ok", "ok", "not checked", 350, true), new DiscordReport("ALT", "ok", "ok", "not checked", 220, true), new DiscordReport("Last failed", "error", "error", "not checked", null, false) };
var recoveryFolder = Path.Combine(temp, "Recovery with spaces");
var ownerListener = new TcpListener(IPAddress.Loopback, 0); ownerListener.Start();
try { Check(CoreRecovery.PortOwner(((IPEndPoint)ownerListener.LocalEndpoint).Port).Contains("PID " + Environment.ProcessId), "Busy-port diagnostics identify the real listener PID"); }
finally { ownerListener.Stop(); }
var recoveryConfig = Path.Combine(recoveryFolder, new string('a',32)+".runtime.json");
Check(CoreRecovery.OwnConfig(recoveryFolder, "\"C:\\Core\\sing-box.exe\" run -c \""+recoveryConfig+"\""), "Recovery recognises exact RelayDesk runtime config including spaces");
Check(!CoreRecovery.OwnConfig(recoveryFolder, "sing-box.exe run -c C:\\OtherApp\\config.json"), "Recovery never claims another application's config");
Check(!CoreRecovery.OwnConfig(recoveryFolder, "sing-box.exe run -c \""+Path.Combine(recoveryFolder,"custom.json")+"\""), "Recovery rejects arbitrary configs in the same folder");
var monitorOriginal = "{\"inbounds\":[],\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}],\"dns\":{\"servers\":[]},\"route\":{\"final\":\"direct\"}}";
var monitored = ConnectionMonitor.Configure(monitorOriginal, 19381);
var monitoredJson = JsonTools.Object(monitored);
Check(JsonTools.Text(monitoredJson["route"]) == JsonTools.Text(JsonTools.Object(monitorOriginal)["route"]) && JsonTools.Text(monitoredJson["dns"]) == JsonTools.Text(JsonTools.Object(monitorOriginal)["dns"]), "Connection instrumentation preserves routes and DNS");
Check(ConnectionMonitor.Endpoint(monitored).Address.Host == "127.0.0.1" && ConnectionMonitor.Endpoint(monitored).Secret.Length == 64, "New connection API binds loopback with random secret");
Check(ConnectionMonitor.Configure(monitored, 19382) == monitored, "Existing controller and authentication preserved");
Reject(() => ConnectionMonitor.Endpoint("{\"experimental\":{\"clash_api\":{\"external_controller\":\"0.0.0.0:9090\"}}}"), "Monitor rejects non-loopback API targets");
var monitorTypes = new Dictionary<string,string> { ["direct"] = "direct", ["proxy"] = "vless", ["group"] = "selector" };
Check(ConnectionMonitor.Classify(["proxy", "group"], monitorTypes) == "Через VPN / прокси" && ConnectionMonitor.Classify(["direct"], monitorTypes) == "Напрямую", "Observed outbound types classify direct and proxy routes");
Check(ConnectionMonitor.Classify(["group"], monitorTypes) == "Маршрут не определён", "Unresolved selector is not falsely classified as VPN");
string[] discordFilter = ["--wf-tcp=443", "--hostlist-domains=discord.com,discord.gg"];
Check(ConnectionMonitor.MatchZapret("TCP", "443", "gateway.discord.gg", discordFilter).StartsWith("Совпадает"), "Discord subdomain matches active TCP capture filter");
Check(ConnectionMonitor.MatchZapret("TCP", "443", "discord.com.example.org", discordFilter) == "Домен вне фильтра", "Domain suffix lookalike does not match zapret");
Check(ConnectionMonitor.MatchZapret("TCP", "80", "discord.com", discordFilter) == "TCP-порт вне фильтра", "Ports outside active capture filter not marked captured");
Check(ConnectionMonitor.MatchZapret("UDP", "50000", "voice.discord.media", discordFilter).Contains("не подтверждена"), "UDP processing never claimed from connection metadata");
var feed = JsonTools.Object("{\"connections\":[{\"id\":\"fixture\",\"metadata\":{\"processPath\":\"C:\\\\Apps\\\\Discord.exe\",\"host\":\"discord.com\",\"destinationPort\":\"443\",\"network\":\"tcp\"},\"chains\":[\"direct\"],\"upload\":23,\"download\":45}]}" );
var parsedFeed = ConnectionMonitor.Parse(feed, monitoredJson, true, discordFilter);
Check(parsedFeed.Count == 1 && parsedFeed[0].Application == "Discord.exe" && parsedFeed[0].Route == "Напрямую" && parsedFeed[0].Zapret.Contains("не подтверждена"), "Connection feed parses process and actual outbound without claiming zapret packet processing");
Check(ZapretAudit.Best(successfulAudit)?.Name == "ALT", "Failed last strategy does not overwrite successful faster selection");
Check(ZapretAudit.Best(successfulAudit.Where(r => !r.Passed)) == null, "No successful strategy produces no selection");
Check(ZapretAudit.Best(new[] { successfulAudit[0], successfulAudit[1] with { Ms = null } }) == null, "Successful flag without timing cannot win strategy selection");
var knownProxyEndpoints = new[] { new Uri("http://127.0.0.1:37418") };
Check(ProxyLease.UnavailableProfileProxy("http://127.0.0.1:37418", knownProxyEndpoints, []) != null, "Recognise dead Windows proxy matching profile endpoint");
Check(ProxyLease.UnavailableProfileProxy("127.0.0.1:37418", knownProxyEndpoints, []) != null, "Recognise Windows proxy host-port format");
Check(ProxyLease.UnavailableProfileProxy("127.0.0.1:37418", knownProxyEndpoints, [new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 37418)]) == null, "Never disable a proxy with a live listener");
Check(ProxyLease.UnavailableProfileProxy("127.0.0.1:1080", knownProxyEndpoints, []) == null, "Never claim another proxy port");
Check(ProxyLease.UnavailableProfileProxy("http=127.0.0.1:37418;https=127.0.0.1:37418", knownProxyEndpoints, []) == null, "Leave protocol-specific Windows proxy mappings intact");
Check(ProxyLease.UnavailableProfileProxy("http://example.org:37418", knownProxyEndpoints, []) == null, "Never disable a remote proxy");
var voiceBin = Path.Combine(temp, "voice-payload-fixture"); Directory.CreateDirectory(voiceBin);
File.WriteAllBytes(Path.Combine(voiceBin, "ACTIVE_DISCORD_UDP.bin"), [0xC5,0,0,0,1,0,0]);
byte[] voiceStunPayload = [0,1,0,0,0x21,0x12,0xA4,0x42,0,0,0,0,0,0,0,0,0,0,0,0];
File.WriteAllBytes(Path.Combine(voiceBin, "stun.bin"), voiceStunPayload);
File.WriteAllBytes(Path.Combine(voiceBin, "quic_initial_www_google_com.bin"), [0]);
var voiceArgs = RuntimePolicy.ZapretArguments(new Preferences { ZapretExe = Path.Combine(voiceBin, "winws.exe"), ZapretArgs = "--dpi-desync=fake" }, ConnectionMode.ZapretOnly);
Check(voiceArgs.Any(a => a.StartsWith("--dpi-desync-fake-discord=")) && voiceArgs.Any(a => a.StartsWith("--dpi-desync-fake-stun=")), "Standalone zapret uses installed Discord voice payload");
Check(voiceArgs.Contains("--filter-l7=quic") && voiceArgs.Contains("--filter-l7=discord,stun"), "QUIC and voice use separate UDP profiles");
Check(voiceArgs.Where(a=>a.StartsWith("--dpi-desync-fake-discord=") || a.StartsWith("--dpi-desync-fake-stun=") || a.StartsWith("--dpi-desync-fake-unknown-udp=")).All(a=>a.EndsWith("stun.bin")) && !voiceArgs.Any(a=>a.Contains("ACTIVE_DISCORD_UDP.bin")),"Voice profiles use verified STUN instead of QUIC masquerading as Discord payload");
Check(DiscordScope.IsStunPayload(voiceStunPayload) && !DiscordScope.IsStunPayload([0xC5,0,0,0,1]) && !DiscordScope.IsStunPayload(voiceStunPayload[..19]),"Voice payload verification rejects QUIC and malformed STUN");
File.WriteAllBytes(Path.Combine(voiceBin,"stun.bin"),[0xC5,0,0,0,1,0,0]);
var rejectedPayloadArgs=RuntimePolicy.ZapretArguments(new Preferences { ZapretExe=Path.Combine(voiceBin,"winws.exe"),ZapretArgs="--dpi-desync=fake" },ConnectionMode.ZapretOnly);
Check(!rejectedPayloadArgs.Any(a=>a.StartsWith("--dpi-desync-fake-stun=") || a.StartsWith("--dpi-desync-fake-unknown-udp=")),"Misnamed QUIC payload never overrides built-in voice fake");
File.WriteAllBytes(Path.Combine(voiceBin,"stun.bin"),voiceStunPayload);
var voiceBypass = new Preferences { ConditionalRouting=true, VoiceUdpDesync=false, DiscordVoiceCidrs="203.0.113.5", ZapretExe=Path.Combine(voiceBin,"winws.exe"), ZapretArgs="--dpi-desync=fake" };
foreach (var bypassMode in new[] { ConnectionMode.ZapretOnly, ConnectionMode.Combined })
{
    var bypassArgs=ZapretQuickRoutes.Apply(RuntimePolicy.ZapretArguments(voiceBypass,bypassMode),[new QuickRoute("process_name","vesktop.exe","@zapret",Zapret:true),new QuickRoute("ip_cidr","203.0.113.5/32","@zapret",Zapret:true)],voiceBypass.ZapretExe,voiceBypass.VoiceUdpDesync);
    Check(!bypassArgs.Any(a=>a.StartsWith("--filter-udp=") && a!="--filter-udp=443") && !bypassArgs.Contains("--filter-l7=unknown") && !bypassArgs.Any(a=>a.StartsWith("--dpi-desync-fake-stun=")),"Voice UDP control removes main and quick voice profiles in "+bypassMode);
    Check(bypassArgs.Contains("--filter-tcp=1-65535") && bypassArgs.Contains("--dpi-desync=fake"),"Voice UDP control retains TCP desync in "+bypassMode);
}
Check(!DiscordScope.Arguments(new Preferences { VoiceUdpDesync=false,DiscordVoiceCidrs="203.0.113.5" },"--dpi-desync=fake").Any(a=>a.StartsWith("--wf-udp=")),"Scoped voice bypass has no UDP capture when no QUIC profile is requested");
Reject(()=>DiscordScope.Arguments(new Preferences { VoiceUdpDesync=false,ZapretDiscordOnly=false },"--wf-udp=19319\n--filter-udp=19319\n--dpi-desync=fake"),"Manual UDP command cannot silently override disabled voice processing");
Check(voiceArgs.Contains("--filter-l7=unknown") && voiceArgs.Count(a=>a=="--dpi-desync-cutoff=n12")==2 && voiceArgs.Count(a=>a.StartsWith("--dpi-desync-fake-unknown-udp="))==2,"Standalone voice handles initial unknown DTLS packets with installed payload and bounded cutoff");
var scopedDtls = DiscordScope.Arguments(new Preferences { ZapretExe=Path.Combine(voiceBin,"winws.exe"),DiscordVoiceCidrs="203.0.113.5" },"--dpi-desync=multisplit");
Check(scopedDtls.Count(a=>a=="--ipset-ip=203.0.113.5/32")==2 && scopedDtls.Contains("--filter-l7=unknown"),"Both scoped voice profiles retain explicit IP restrictions");
var updateBytes = new byte[2048]; updateBytes[0] = (byte)'M'; updateBytes[1] = (byte)'Z';
var updateDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(updateBytes)).ToLowerInvariant();
var releaseFixture = new JsonObject {
    ["tag_name"] = "v9.8.7", ["name"] = "Fix connection", ["body"] = "Fixed standalone Discord.\nSettings preserved.", ["draft"] = false, ["prerelease"] = false,
    ["assets"] = new JsonArray(new JsonObject { ["name"] = "RelayDesk-Setup-9.8.7-x64.exe", ["size"] = (long)updateBytes.Length, ["digest"] = "sha256:" + updateDigest, ["browser_download_url"] = "https://github.com/bernicetownsend1955/RelayDesk-VPN/releases/download/v9.8.7/RelayDesk-Setup-9.8.7-x64.exe" })
};
void RejectRelease(Action action, string label) { try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or InvalidOperationException) { Check(true, label); return; } throw new Exception("Expected release rejection: " + label); }
Check(AppUpdates.Repository("https://github.com/bernicetownsend1955/RelayDesk-VPN/") == "bernicetownsend1955/RelayDesk-VPN", "App update repository normalises GitHub URL");
RejectRelease(() => AppUpdates.Repository("test-owner/../relay"), "App updates reject repository traversal");
RejectRelease(() => AppUpdates.Repository("http://localhost/repo"), "App updates reject arbitrary update servers");
var parsedRelease = AppUpdates.Parse(releaseFixture, "bernicetownsend1955/RelayDesk-VPN", "0.8.0");
Check(AppUpdates.Parse(releaseFixture, "BerniceTownsend1955/RelayDesk-VPN", "0.8.0").Available, "GitHub repository casing does not break app updates");
Check(parsedRelease.Available && parsedRelease.Notes.Contains("Discord") && parsedRelease.Version == "9.8.7", "Stable app update includes version and patch notes");
Check(!AppUpdates.Parse(releaseFixture, "bernicetownsend1955/RelayDesk-VPN", "9.8.7").Available && !AppUpdates.Parse(releaseFixture, "bernicetownsend1955/RelayDesk-VPN", "10.0.0").Available, "App updates never offer equal or older versions");
foreach (var flag in new[] { "draft", "prerelease" }) { var invalid = (JsonObject)releaseFixture.DeepClone(); invalid[flag] = true; RejectRelease(() => AppUpdates.Parse(invalid, "bernicetownsend1955/RelayDesk-VPN", "0.8.0"), "App updates reject " + flag); }
var noDigest = (JsonObject)releaseFixture.DeepClone(); noDigest["assets"]![0]!["digest"] = "";
RejectRelease(() => AppUpdates.Parse(noDigest, "bernicetownsend1955/RelayDesk-VPN", "0.8.0"), "App updates require GitHub SHA-256");
var wrongHost = (JsonObject)releaseFixture.DeepClone(); wrongHost["assets"]![0]!["browser_download_url"] = "https://example.com/RelayDesk.exe";
RejectRelease(() => AppUpdates.Parse(wrongHost, "bernicetownsend1955/RelayDesk-VPN", "0.8.0"), "App updates reject assets outside selected release");
var duplicateAsset = (JsonObject)releaseFixture.DeepClone(); duplicateAsset["assets"]!.AsArray().Add(duplicateAsset["assets"]![0]!.DeepClone());
RejectRelease(() => AppUpdates.Parse(duplicateAsset, "bernicetownsend1955/RelayDesk-VPN", "0.8.0"), "App updates reject duplicate installers");
int releaseRequests = 0, installerRequests = 0;
using var updateClient = new System.Net.Http.HttpClient(new ReleaseHttpHandler(request => {
    if (request.RequestUri!.Host == "api.github.com") { releaseRequests++; return new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(releaseFixture.ToJsonString()) }; }
    installerRequests++; return new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent(updateBytes) };
}));
var appUpdater = new AppUpdates(updateClient);
var checkedRelease = await appUpdater.Check("bernicetownsend1955/RelayDesk-VPN", "0.8.0", CancellationToken.None);
Check(releaseRequests == 1 && installerRequests == 0, "App update checks download metadata only");
var appDownloadFolder = Path.Combine(temp, "app-update-downloads"); var progressText = "";
var downloadedApp = await appUpdater.Download(checkedRelease, appDownloadFolder, text => progressText = text, CancellationToken.None);
Check(File.ReadAllBytes(downloadedApp).SequenceEqual(updateBytes) && progressText.Contains("100%"), "App installer download verifies bytes and reports progress");
var beforeExecutables = Directory.GetFiles(appDownloadFolder, "*.exe", SearchOption.AllDirectories).Length;
bool corruptRejected = false;
try { await appUpdater.Download(checkedRelease with { File = checkedRelease.File with { Hash = new string('0',64) } }, appDownloadFolder, _ => { }, CancellationToken.None); }
catch (InvalidDataException) { corruptRejected = true; }
Check(corruptRejected && Directory.GetFiles(appDownloadFolder, "*.exe", SearchOption.AllDirectories).Length == beforeExecutables && Directory.GetFiles(appDownloadFolder, "*.partial", SearchOption.AllDirectories).Length == 0, "App hash mismatch leaves no executable or partial download");
using var cancelledDownload = new CancellationTokenSource(); cancelledDownload.Cancel(); bool cancelledApp = false;
try { await appUpdater.Download(checkedRelease, appDownloadFolder, _ => { }, cancelledDownload.Token); } catch (OperationCanceledException) { cancelledApp = true; }
Check(cancelledApp && Directory.GetFiles(appDownloadFolder, "*.partial", SearchOption.AllDirectories).Length == 0, "App update cancellation leaves no partial installer");
Check(AppUpdates.PlainText("abc\0\u001bdef", 4) == "abcd…", "App release text is bounded and strips control characters");
bool fixedSourceRejected=false;
try { await appUpdater.Check("other-owner/other-repo","0.8.0",CancellationToken.None); } catch(InvalidDataException) { fixedSourceRejected=true; }
Check(fixedSourceRejected && releaseRequests==1,"Application updater refuses a different repository before making a request");
foreach(var coreId in new[]{"Xray","mihomo"})
{
    var coreName=coreId=="Xray" ? "Xray-windows-64.zip" : "mihomo-windows-amd64-compatible-v9.8.7.zip";
    var coreFixture=new JsonObject { ["tag_name"]="v9.8.7",["assets"]=new JsonArray(new JsonObject { ["name"]=coreName,["size"]=2048L,["digest"]="sha256:"+updateDigest,["browser_download_url"]=$"https://github.com/{ComponentUpdates.OfficialRepository(coreId)}/releases/download/v9.8.7/{coreName}" }) };
    Check(ComponentUpdates.ProxyCoreRelease(coreFixture,coreId,"1.0.0",true).Available,coreId+" selects the official Windows x64 update");
    Check(!ComponentUpdates.ProxyCoreRelease(coreFixture,coreId,"Версия не определена",true).Available,coreId+" preserves external cores with unknown versions");
    coreFixture["assets"]![0]!["browser_download_url"]="https://github.com/other-owner/other-repo/releases/download/v9.8.7/"+coreName;
    RejectRelease(()=>ComponentUpdates.ProxyCoreRelease(coreFixture,coreId,"1.0.0",true),coreId+" rejects a foreign update source");
}
var stagedStore=new Store(Path.Combine(temp,"pending-updates"));
stagedStore.Settings.Engines["Xray"]="previous-xray.exe";stagedStore.Save();
var stagedRoot=Path.Combine(stagedStore.Folder,"components","Xray",Guid.NewGuid().ToString("N"),"unpacked");Directory.CreateDirectory(stagedRoot);
var stagedExe=Path.Combine(stagedRoot,"xray.exe");File.WriteAllBytes(stagedExe,updateBytes);
var stagedUpdate=new ComponentUpdate("Xray","Xray","v9.8.7","1.0.0","",true,"",[]);
PendingUpdates.StageCore(stagedStore,stagedUpdate,stagedExe);
Check(stagedStore.Settings.Engines["Xray"]=="previous-xray.exe" && PendingUpdates.HasCore(stagedStore,stagedUpdate),"Staging a core keeps the running executable selected");
PendingUpdates.ApplyCores(stagedStore,_=>{});
Check(new Store(stagedStore.Folder).Settings.Engines["Xray"]==stagedExe && stagedStore.Settings.ComponentInstalls["Xray"].PreviousExecutable=="previous-xray.exe","Startup activates verified core and records previous executable");
PendingUpdates.StageCore(stagedStore,stagedUpdate with { Revision="v9.8.8" },stagedExe);
File.AppendAllText(stagedExe,"tampered");PendingUpdates.ApplyCores(stagedStore,_=>{});
Check(stagedStore.Settings.ComponentInstalls["Xray"].Revision=="v9.8.7","Tampered staged core cannot replace the installed receipt");
var pendingAppFolder=Path.Combine(stagedStore.Folder,"application-updates");Directory.CreateDirectory(pendingAppFolder);
var pendingInstaller=Path.Combine(pendingAppFolder,parsedRelease.File.Name);File.WriteAllBytes(pendingInstaller,updateBytes);
PendingUpdates.StageApp(stagedStore.Folder,parsedRelease,pendingInstaller);
Check(PendingUpdates.App(stagedStore.Folder)?.Release.Version=="9.8.7","Verified app update persists for the next launch");
RejectRelease(()=>PendingUpdates.ValidateApp(stagedStore.Folder,new(parsedRelease with { Repository="other-owner/other-repo" },pendingInstaller)),"Pending installer also requires the fixed repository");
File.AppendAllText(pendingInstaller,"tampered");
RejectRelease(()=>PendingUpdates.ValidateApp(stagedStore.Folder,PendingUpdates.App(stagedStore.Folder)!),"Pending installer is rechecked before installation");
PendingUpdates.Cancel(stagedStore);
Check(PendingUpdates.App(stagedStore.Folder)==null && File.Exists(pendingInstaller) && stagedStore.Settings.Engines["Xray"]==stagedExe,"Disabling updates clears queued activation and keeps installed configuration");
var startupXml = System.Xml.Linq.XDocument.Parse(StartupRegistration.TaskXml(@"C:\Program Files\RelayDesk & Test\RelayDesk.exe", "S-1-5-21-1-2-3-1001"));
System.Xml.Linq.XNamespace taskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
Check(startupXml.Descendants(taskNamespace + "Command").Single().Value == @"C:\Program Files\RelayDesk & Test\RelayDesk.exe", "Startup task XML escapes paths and preserves spaces");
Check(startupXml.Descendants(taskNamespace + "Arguments").Single().Value == "--windows-startup" && startupXml.Descendants(taskNamespace + "UserId").All(e => e.Value == "S-1-5-21-1-2-3-1001"), "Startup task targets only current user with explicit launch flag");
Check(startupXml.Descendants(taskNamespace + "RunLevel").Single().Value == "HighestAvailable" && startupXml.Descendants(taskNamespace + "LogonType").Single().Value == "InteractiveToken", "Startup uses elevated interactive token without storing a password");
Check(startupXml.Descendants(taskNamespace + "Delay").Single().Value == "PT15S" && startupXml.Descendants(taskNamespace + "MultipleInstancesPolicy").Single().Value == "IgnoreNew", "Startup task delays logon launch and ignores duplicate instances");
Check(!new Preferences().WindowsStartup, "Windows startup is opt-in");
var quickProfile = System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(vless.Profile))!;
quickProfile.Tun = true;
quickProfile.QuickRoutes = [new("process_path", @"C:\Apps\Discord.exe", "@direct", true), new("domain", "discord.com", "proxy"), new("ip_cidr", "203.0.113.10/32", "@direct", true)];
var baseQuickConfig = JsonTools.Build(quickProfile);
var quickConfig = RuntimePolicy.Config(quickProfile,new Preferences(),ConnectionMode.VpnOnly);
var quickRoot = JsonTools.Object(quickConfig); var quickRules = quickRoot["route"]!["rules"]!.AsArray();
Check(JsonTools.Text(quickRules[0]?["action"]) == "hijack-dns" && quickRules.Any(r => r?["process_path"] != null) && quickRules.Any(r => r?["domain"] != null), "Quick routes preserve leading DNS setup and add process and domain matches");
Check(JsonNode.DeepEquals(quickRoot["dns"],JsonTools.Object(baseQuickConfig)["dns"]), "Quick routing preserves DNS configuration");
var combinedQuick = JsonTools.Object(RuntimePolicy.Config(quickProfile,new Preferences(),ConnectionMode.Combined));
var combinedQuickRules = combinedQuick["route"]!["rules"]!.AsArray().ToList();
Check(combinedQuickRules.FindIndex(r => r?["domain"] != null) < combinedQuickRules.FindIndex(r => r?["domain_suffix"] != null), "Explicit quick route takes precedence over automatic Discord routes");
Reject(() => QuickRouting.Apply(baseQuickConfig,"sing-box",[new("domain","example.org","deleted-tag")]), "Quick routes reject missing proxy outbounds");
Reject(() => QuickRouting.Validate(new("domain","https://example.org/path","proxy")), "Quick route domain rejects URLs and paths");
var noTunQuick = JsonTools.Object(baseQuickConfig); noTunQuick["inbounds"] = new JsonArray(new JsonObject { ["type"]="mixed",["listen_port"]=2080 });
Reject(() => QuickRouting.Apply(noTunQuick.ToJsonString(),"sing-box",[new("process_name","Discord.exe","@direct",true)]), "Application quick routes require TUN instead of silently doing nothing");
var collisionQuick = JsonTools.Object(baseQuickConfig); collisionQuick["outbounds"]!.AsArray().Add(new JsonObject { ["type"]="direct",["tag"]="relay-quick-direct",["detour"]="proxy" });
var collisionResult = JsonTools.Object(QuickRouting.Apply(collisionQuick.ToJsonString(),"sing-box",[new("domain","example.org","@direct",true)]));
Check(collisionResult["outbounds"]!.AsArray().Any(o => JsonTools.Text(o?["tag"]) == "relay-quick-direct-1" && o?["detour"] == null), "Quick direct routes never reuse a direct outbound that detours through VPN");
if(args.Length > 1)
{
    var quickStore = new Store(Path.Combine(temp,"quick-check")); quickStore.Settings.Engines["sing-box"] = args[1];
    var quickSession = new EngineSession(quickStore,_ => { });
    await quickSession.Check(quickProfile);
    await quickSession.Check(new Profile { Config=combinedQuick.ToJsonString() });
    Check(true,"Real sing-box accepts quick rules in VPN and combined modes");
}
var quickImportStore = new Store(Path.Combine(temp,"quick-import"));
var preservedImport = new Profile { SourceKey="fixture-quick-source",Config=quickProfile.Config,QuickRoutes=quickProfile.QuickRoutes.ToList() };
quickImportStore.Profiles.Add(preservedImport);
AppImports.Apply(quickImportStore,[new Profile { SourceKey=preservedImport.SourceKey,Config=quickProfile.Config }]);
Check(quickImportStore.Profiles.Single().QuickRoutes.SequenceEqual(quickProfile.QuickRoutes),"Import synchronisation preserves user quick routes");
Check(parsedFeed.Single().Host == "discord.com" && parsedFeed.Single().ProcessPath.Length > 0 && parsedFeed.Single().TotalBytes == 68,"Connection actions retain real host, executable path and numeric traffic metadata");
var zapretRoute = new QuickRoute("domain", "example.org", "@zapret", Zapret: true);
var zapretProfile = System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(quickProfile))!;
zapretProfile.QuickRoutes = [zapretRoute];
var zapretCombined = JsonTools.Object(RuntimePolicy.Config(zapretProfile, new Preferences { ConditionalRouting = false }, ConnectionMode.Combined));
Check(JsonTools.Text(zapretCombined["route"]?["rules"]?.AsArray().FirstOrDefault(r => JsonTools.Text(r?["domain"]?[0]) == "example.org")?["outbound"]) != "@zapret", "Zapret destination compiles to an existing direct outbound");
Check(JsonNode.DeepEquals(JsonTools.Object(RuntimePolicy.Config(zapretProfile,new Preferences { ConditionalRouting=false },ConnectionMode.VpnOnly)), JsonTools.Object(JsonTools.Build(zapretProfile,new Preferences { ConditionalRouting=false }))), "Zapret-only override is inactive when zapret is off");
var quickZapretArgs = ZapretQuickRoutes.Apply(["--wf-tcp=80,443", "--filter-tcp=443", "--hostlist-domains=discord.com", "--dpi-desync=fake", "--dpi-desync-cutoff=n3"], [zapretRoute]);
Check(quickZapretArgs.Contains("--hostlist-domains=^example.org") && quickZapretArgs.Contains("--hostlist-domains=discord.com") && quickZapretArgs.Contains("--wf-tcp=80,443,80,443"), "Zapret rules add exact domain scope and preserve original capture ports");
var quickIpArgs = ZapretQuickRoutes.Apply(["--wf-tcp=443", "--filter-tcp=443", "--dpi-desync=fake"], [new("ip_cidr", "203.0.113.10/32", "@zapret", Zapret:true)]);
Check(quickIpArgs.Contains("--ipset-ip=203.0.113.10/32") && quickIpArgs.Contains("--filter-udp=1-65535"), "IP zapret scope includes UDP voice ports");
var appZapretArgs = ZapretQuickRoutes.Apply(["--wf-tcp=443", "--filter-tcp=443", "--dpi-desync=fake"], [new("process_name", "Test.exe", "@zapret", Zapret:true)]);
Check(appZapretArgs.Contains("--filter-tcp=1-65535") && appZapretArgs.Contains("--wf-udp=1-65535") && appZapretArgs.Contains("--dpi-desync-cutoff=n3"), "Application zapret rule expands capture with bounded UDP desync");
var appVoiceArgs = ZapretQuickRoutes.Apply(RuntimePolicy.ZapretArguments(new Preferences { ZapretArgs="--dpi-desync=fake",DiscordVoiceCidrs="203.0.113.5",ZapretExe=Path.Combine(voiceBin,"winws.exe") },ConnectionMode.Combined),[new QuickRoute("process_name","vesktop.exe","@zapret",Zapret:true)],Path.Combine(voiceBin,"winws.exe"));
var appUdpSections=string.Join('\n',appVoiceArgs).Split("--new").Where(s=>s.Contains("--filter-udp=")).ToArray();
Check(appUdpSections.Where(s=>s.Contains("--filter-l7=discord,stun")).All(s=>s.Contains("--dpi-desync-cutoff=n12") && s.Contains("--dpi-desync-fake-unknown-udp=")),"Process quick route never selects legacy STUN n3 before DTLS");
Check(appUdpSections.Any(s=>s.Contains("--filter-l7=unknown") && !s.Contains("--ipset-ip=") && s.Contains("--filter-udp=19294-19344,50000-65535")),"Application voice fallback covers new voice IPs on voice ports without broad unknown UDP capture");
var ipVoiceArgs = ZapretQuickRoutes.Apply(["--wf-tcp=443","--filter-tcp=443","--dpi-desync=fake"],[new QuickRoute("ip_cidr","203.0.113.10/32","@zapret",Zapret:true)],Path.Combine(voiceBin,"winws.exe"));
Check(string.Join('\n',ipVoiceArgs).Split("--new").Where(s=>s.Contains("--filter-l7=unknown") || s.Contains("--filter-l7=discord,stun")).All(s=>s.Contains("--ipset-ip=203.0.113.10/32") && s.Contains("--dpi-desync-cutoff=n12")),"IP quick route keeps DTLS profiles restricted to chosen IP");
Reject(() => ZapretQuickRoutes.Apply(["--wf-raw=tcp", "--filter-tcp=443", "--dpi-desync=fake"], [zapretRoute]), "Raw WinDivert filters are rejected before changing live cores");
preservedImport.SubscriptionGroup = "Work"; quickImportStore.Profiles[0].SubscriptionGroup = "Work"; quickImportStore.Save();
AppImports.Apply(quickImportStore,[new Profile { SourceKey=preservedImport.SourceKey,Config=quickProfile.Config }]);
Check(new Store(quickImportStore.Folder).Profiles.Single().SubscriptionGroup == "Work", "Group assignment survives import refresh and disk reload");
var testWinws = Environment.GetEnvironmentVariable("RELAYDESK_TEST_WINWS");
File.WriteAllText(Path.Combine(args.Length > 0 ? args[0] : temp,"zapret-quick-arguments.json"), System.Text.Json.JsonSerializer.Serialize(new[] { quickZapretArgs, appZapretArgs, quickIpArgs }));
if (File.Exists(testWinws) && EngineSession.IsAdmin())
{
    foreach (var parameters in new[] { quickZapretArgs, appZapretArgs })
    {
        var start = new System.Diagnostics.ProcessStartInfo(testWinws) { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        start.ArgumentList.Add("--dry-run"); foreach (var parameter in parameters) start.ArgumentList.Add(parameter);
        using var process = System.Diagnostics.Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync(); var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); var output = await outputTask; var error = await errorTask;
        if (process.ExitCode != 0) throw new Exception("winws dry-run failed: " + output + error);
        Check(true, "Real winws accepts generated quick route arguments without starting capture");
    }
}
Check(Subscriptions.Address("happ://add/https://subscriptions.example.org/private").AbsoluteUri == "https://subscriptions.example.org/private", "Happ add link unwraps into HTTPS subscription");
Reject(() => Subscriptions.Address("http://subscriptions.example.org/private"), "Subscriptions reject unencrypted URLs");
Reject(() => Subscriptions.Address("https://user:password@subscriptions.example.org/private"), "Subscription URLs reject embedded credentials");
var fixtureLink = $"vless://{id}@example.org:443?security=none#Fixture";
var secondFixtureLink = $"vless://{id}@example.net:443?security=none#Second";
var subscriptionFeedFixture = Subscriptions.Parse(B64(fixtureLink+"\n"+fixtureLink+"\n"+secondFixtureLink));
Check(subscriptionFeedFixture.Profiles.Count == 2 && subscriptionFeedFixture.Warnings.Count == 0,"Base64 subscription decodes and deduplicates servers");
Check(Subscriptions.Parse(fixtureLink+"\nunsupported://example.org").Warnings.Count == 1,"Unsupported entries are reported while valid servers remain available");
Reject(() => Subscriptions.Parse($"vless://{id}@127.0.0.1:1?security=none#App%20not%20supported"), "Provider denial placeholder is never imported as VPN server");
var grpcFeed = Subscriptions.Parse($"vless://{id}@example.org:443?type=grpc&mode=multi&serviceName=fixture&security=tls&sni=example.org#GRPC");
Check(grpcFeed.Profiles.Single().Core == "Xray" && JsonTools.Object(grpcFeed.Profiles[0].Config)["outbounds"]![0]!["streamSettings"]!["grpcSettings"]!["multiMode"]!.GetValue<bool>(), "gRPC multi mode retains Xray-specific transport settings");
var xhttpFeed = Subscriptions.Parse($"vless://{id}@example.org:443?type=xhttp&mode=auto&path=%2Ffixture&security=tls&sni=example.org#XHTTP");
Check(JsonTools.Text(JsonTools.Object(xhttpFeed.Profiles[0].Config)["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!["path"]) == "/fixture", "XHTTP path and mode are preserved in Xray config");
var installationFolder = Path.Combine(temp,"subscription-device");
var deviceId = Subscriptions.InstallationId(installationFolder);
Check(deviceId == Subscriptions.InstallationId(installationFolder) && Subscriptions.DeviceId(deviceId,"EXAMPLE.org") == Subscriptions.DeviceId(deviceId,"example.org") && Subscriptions.DeviceId(deviceId,"example.net") != Subscriptions.DeviceId(deviceId,"example.org"), "Subscription HWID persists and is scoped to provider host");
bool sawDeviceHeaders=false;
using (var handler = new ReleaseHttpHandler(request =>
{
    sawDeviceHeaders = request.Headers.UserAgent.ToString().StartsWith("RelayDesk/") && request.Headers.GetValues("x-hwid").Single().Length == 64 && request.Headers.GetValues("x-device-os").Single() == "Windows";
    var response = new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(B64(fixtureLink)) };
    response.Headers.Add("profile-title","base64:"+B64("Fixture group")); return response;
}))
{
    var downloaded = await Subscriptions.Download(new SubscriptionSource { Url="https://subscriptions.example.org/private" }, deviceId,CancellationToken.None,handler);
    Check(sawDeviceHeaders && downloaded.Batch.Profiles.Count==1 && downloaded.Title=="Fixture group", "Subscription download sends honest client and HWID headers and decodes title");
}
using (var handler = new ReleaseHttpHandler(_ => { var response = new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK) { Content=new System.Net.Http.StringContent("must not parse") }; response.Headers.Add("x-hwid-max-devices-reached","true"); return response; }))
{
    try { await Subscriptions.Download(new SubscriptionSource { Url="https://subscriptions.example.org/private" },deviceId,CancellationToken.None,handler); throw new Exception("HWID quota accepted"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("лимит"),"Device quota denial produces a clear error before parsing"); }
}
var subscriptionStore = new Store(Path.Combine(temp,"subscription-store"));
var subscriptionSource = new SubscriptionSource { Url="https://subscriptions.example.org/private",Group="Fixture group" };
Subscriptions.Apply(subscriptionStore,subscriptionSource,Subscriptions.Parse(fixtureLink).Profiles);
var storedSubscription = subscriptionStore.Profiles.Single(); var stableProfileId=storedSubscription.Id;
storedSubscription.SubscriptionGroup="Moved"; storedSubscription.Tun=true; storedSubscription.QuickRoutes=[new("domain","fixture.example","@direct",true)];
var rotatedLink = fixtureLink.Replace(id,"aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
var subscriptionRefreshResult = Subscriptions.Apply(subscriptionStore,subscriptionSource,Subscriptions.Parse(rotatedLink).Profiles);
var reloadedSubscription = new Store(subscriptionStore.Folder);
Check(subscriptionRefreshResult.Updated==1 && subscriptionRefreshResult.Added==0 && reloadedSubscription.Profiles.Single().Id==stableProfileId && reloadedSubscription.Profiles[0].Tun && reloadedSubscription.Profiles[0].QuickRoutes.Count==1 && reloadedSubscription.Profiles[0].SubscriptionGroup=="Moved", "Subscription refresh retains identity, group, TUN and quick rules after credential rotation");
Check(reloadedSubscription.Settings.Subscriptions.Single().UpdatedAt.HasValue,"Subscription source and update time persist locally");
var bundledFixture = Path.Combine(temp,"bundled-application"); var bundledRoot = Path.Combine(bundledFixture,"bundled-cores"); Directory.CreateDirectory(bundledRoot);
File.WriteAllText(Path.Combine(bundledRoot,"sing-box.exe"),"fixture core bytes");
var bundleHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(bundledRoot,"sing-box.exe"))));
File.WriteAllText(Path.Combine(bundledRoot,"manifest.json"),System.Text.Json.JsonSerializer.Serialize(new[] { new BundledCores.Bundle("sing-box","v1.14.3","sing-box.exe",[new("sing-box.exe",bundleHash)]) }));
var bundledStore = new Store(Path.Combine(temp,"bundled-data"));
Check(BundledCores.Bind(bundledStore,bundledFixture) && new Store(bundledStore.Folder).Settings.Engines["sing-box"] == Path.Combine(bundledRoot,"sing-box.exe"),"Bundled engine binds and persists without network access");
var externalCore = Path.Combine(temp,"external-core.exe"); File.WriteAllText(externalCore,"external fixture"); bundledStore.Settings.Engines["sing-box"]=externalCore;
Check(!BundledCores.Bind(bundledStore,bundledFixture) && bundledStore.Settings.Engines["sing-box"] == externalCore,"Bundled core preserves existing working external engine");
bundledStore.Settings.Engines.Clear(); File.WriteAllText(Path.Combine(bundledRoot,"sing-box.exe"),"tampered");
try { BundledCores.Bind(bundledStore,bundledFixture); throw new Exception("Corrupt bundled engine accepted"); } catch (System.IO.InvalidDataException) { Check(true,"Bundled engine checksum failure prevents binding"); }
var xrayCombinedProfile = xhttpFeed.Profiles.Single();
var xrayOriginal = JsonTools.Object(xrayCombinedProfile.Config);
var xrayCombinedRuntime = JsonTools.Object(RuntimePolicy.Config(xrayCombinedProfile,new Preferences { ConditionalRouting=true,ZapretApplications=[@"C:\Apps\Discord.exe"],DiscordVoiceCidrs="203.0.113.7/32" },ConnectionMode.Combined));
Check(JsonNode.DeepEquals(xrayOriginal["outbounds"]![0],xrayCombinedRuntime["outbounds"]![0]),"Xray combined mode preserves original proxy and transport");
Check(xrayCombinedRuntime["routing"]!["rules"]!.AsArray().Any(r => r?["domain"] != null) && xrayCombinedRuntime["routing"]!["rules"]!.AsArray().Any(r => r?["ip"] != null),"Xray combined mode routes Discord domains and voice IP without EXE error");
Check(!xrayCombinedRuntime.ToJsonString().Contains("process_path") && xrayCombinedProfile.SystemProxy,"Xray subscription uses system proxy and never receives sing-box process rules");
Check(JsonNode.DeepEquals(xrayOriginal,JsonTools.Object(RuntimePolicy.Config(xrayCombinedProfile,new Preferences { ConditionalRouting=true },ConnectionMode.VpnOnly))),"VPN-only Xray profile keeps its original routing");
var xrayHistory = new XrayConnectionHistory();
Check(xrayHistory.Record("2026/01/01 00:00:00 from 127.0.0.1:50100 accepted tcp:example.org:443 [relay-http -> proxy]"),"Xray access record extracts actual outbound tag");
xrayHistory.Record("2026/01/01 00:00:00 127.0.0.1:50101 accepted //discord.com:443 [relay-http -> local-direct]");
var historyConfig="{\"outbounds\":[{\"protocol\":\"vless\",\"tag\":\"proxy\"},{\"protocol\":\"freedom\",\"tag\":\"local-direct\"}]}";
var historyRows=xrayHistory.Read(historyConfig,false,[]);
Check(historyRows.Any(r=>r.Route=="Напрямую") && historyRows.Any(r=>r.Route=="Через VPN / прокси") && historyRows.All(r=>r.CanEdit && !r.CanEditApplication && r.Traffic=="Не сообщает ядро"),"Xray history allows address routes without fabricating traffic or EXE data");
var quickXraySource = xrayCombinedProfile.Config;
var xrayRules = new[] { new QuickRoute("domain","example.org","@direct",true), new QuickRoute("domain_suffix","example.net","proxy"), new QuickRoute("ip_cidr","203.0.113.1/32","@zapret",false,true) };
var quickXray = JsonTools.Object(QuickRouting.Apply(quickXraySource,"Xray",xrayRules));
Check(JsonTools.Text(quickXray["routing"]!["rules"]![0]!["domain"]![0])=="full:example.org" && JsonTools.Text(quickXray["routing"]!["rules"]![1]!["domain"]![0])=="domain:example.net", "Xray quick routes distinguish exact domains and subdomains");
Check(quickXray["routing"]!["rules"]!.AsArray().Any(r=>r?["ip"]!=null) && quickXray["outbounds"]!.AsArray().Any(o=>JsonTools.Text(o?["protocol"])=="freedom"), "Xray zapret route has an IP rule and direct outbound");
Check(JsonTools.Object(QuickRouting.Apply(quickXraySource,"Xray",xrayRules,false))["routing"]!["rules"]!.AsArray().Count==2 && xrayCombinedProfile.Config==quickXraySource, "Inactive zapret rule is excluded and source Xray JSON stays unchanged");
Reject(()=>QuickRouting.Apply(quickXraySource,"Xray",[new QuickRoute("process_name","Discord.exe","proxy")]),"Xray address routing rejects unsupported process rules");
Reject(()=>QuickRouting.Apply(quickXraySource,"Xray",[new QuickRoute("domain","example.org","missing")]),"Xray quick route validates selected outbound");
Check(!xrayHistory.Record("configuration loaded"),"Xray non-access output is not a connection");
xrayHistory.Clear(); Check(xrayHistory.Read(historyConfig,false,[]).Count==0,"Xray history clears between sessions");
var yamlFixture="mixed-port: 2080\nproxies:\n  - name: Fixture\n    type: ss\n    server: example.org\n    port: 443\n    cipher: aes-128-gcm\n    password: fixture\nrules:\n  - MATCH,DIRECT\n";
var yamlRuntime=MihomoConnections.Configure(yamlFixture,29101);
Check(ConnectionMonitor.Endpoint(MihomoConnections.MonitorConfig(yamlRuntime)).Address.Port==29101 && ConnectionMonitor.Endpoint(MihomoConnections.MonitorConfig(yamlRuntime)).Secret.Length==64,"Mihomo runtime receives authenticated loopback API");
Check(MihomoConnections.Endpoints(yamlRuntime).Single()==("example.org",443),"Mihomo YAML keeps proxy endpoints after adding monitoring");
var yamlJson=JsonTools.Object(MihomoConnections.MonitorConfig(yamlRuntime));
Check(ConnectionMonitor.Classify(["Fixture"],new Dictionary<string,string>{{"Fixture",JsonTools.Text(yamlJson["outbounds"]![0]!["type"])}})=="Через VPN / прокси","Mihomo Shadowsocks tag is classified as proxy");
var latencyListener=new TcpListener(IPAddress.Loopback,0); latencyListener.Start();
var latencyPort=((IPEndPoint)latencyListener.LocalEndpoint).Port;
var latencyRows=new List<ServerLatency>();
try { await ServerLatencyProbe.Run([new Profile { Name="Local latency",Managed=true,Host="127.0.0.1",Port=latencyPort }],r=>latencyRows.Add(r),CancellationToken.None); Check(latencyRows.Single().Milliseconds.HasValue,"Server latency probes real TCP endpoint without starting a VPN core"); }
finally { latencyListener.Stop(); }
using(var cancelledLatency=new CancellationTokenSource()) { cancelledLatency.Cancel(); try { await ServerLatencyProbe.Run([new Profile {Managed=true,Host="127.0.0.1",Port=1}],_=>{},cancelledLatency.Token); throw new Exception("Cancelled latency probe proceeded"); } catch(OperationCanceledException) {Check(true,"Server latency honours cancellation");} }
foreach(var telemetryCore in new[] { "Xray","mihomo" })
{
    var telemetryExe=Environment.GetEnvironmentVariable(telemetryCore=="Xray" ? "RELAYDESK_TEST_XRAY" : "RELAYDESK_TEST_MIHOMO");
    if(!File.Exists(telemetryExe)) continue;
    var telemetryFolder=Path.Combine(temp,"telemetry-"+telemetryCore); var telemetryStore=new Store(telemetryFolder); telemetryStore.Settings.Engines[telemetryCore]=telemetryExe;
    var portListener=new TcpListener(IPAddress.Loopback,0); portListener.Start(); var proxyPort=((IPEndPoint)portListener.LocalEndpoint).Port; portListener.Stop();
    var localTarget=new TcpListener(IPAddress.Loopback,0); localTarget.Start(); var targetPort=((IPEndPoint)localTarget.LocalEndpoint).Port;
    using var telemetryTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var requestArrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var releaseTarget=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var targetTask=Task.Run(async ()=> { using var client=await localTarget.AcceptTcpClientAsync(telemetryTimeout.Token); using var stream=client.GetStream(); var buffer=new byte[4096]; await stream.ReadAsync(buffer,telemetryTimeout.Token); requestArrived.TrySetResult(); await releaseTarget.Task.WaitAsync(telemetryTimeout.Token); await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"),telemetryTimeout.Token); });
    var telemetryProfile=new Profile { Core=telemetryCore,ProxyUri="http://127.0.0.1:"+proxyPort,Config=telemetryCore=="Xray" ? "{\"log\":{\"loglevel\":\"warning\"},\"inbounds\":[{\"listen\":\"127.0.0.1\",\"port\":"+proxyPort+",\"protocol\":\"http\",\"tag\":\"test-in\",\"settings\":{}}],\"outbounds\":[{\"protocol\":\"freedom\",\"tag\":\"test-direct\"}]}" : "mixed-port: "+proxyPort+"\nmode: rule\nlog-level: warning\nproxies: []\nrules: [\"MATCH,DIRECT\"]\n" };
    var telemetryLogs=new System.Collections.Concurrent.ConcurrentQueue<string>(); var telemetryEngine=new EngineSession(telemetryStore,s=>telemetryLogs.Enqueue(s));
    if (telemetryCore == "Xray") telemetryProfile.QuickRoutes = [new QuickRoute("ip_cidr","127.0.0.1/32","@direct",true), new QuickRoute("domain","example.org","@direct",true)];
    try
    {
        await telemetryEngine.Connect(telemetryProfile,false);
        using var client=new TcpClient(); await client.ConnectAsync(IPAddress.Loopback,proxyPort,telemetryTimeout.Token); using var stream=client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET http://127.0.0.1:{targetPort}/ HTTP/1.1\r\nHost: 127.0.0.1:{targetPort}\r\n\r\n"),telemetryTimeout.Token);
        await requestArrived.Task.WaitAsync(telemetryTimeout.Token);
        var runtimeFile=Path.Combine(telemetryFolder,telemetryProfile.Id+(telemetryCore=="Xray" ? ".runtime.json" : ".runtime.yaml")); var telemetryConfig=File.ReadAllText(runtimeFile); List<ConnectionRow> liveRows=[];
        using var telemetryMonitor=new ConnectionMonitor();
        for(int attempt=0;attempt<20 && liveRows.Count==0;attempt++) { liveRows=telemetryCore=="Xray" ? telemetryEngine.XrayConnections.Read(telemetryConfig,false,[]) : await telemetryMonitor.Read(MihomoConnections.MonitorConfig(telemetryConfig),false,[],telemetryTimeout.Token); if(liveRows.Count==0) await Task.Delay(100,telemetryTimeout.Token); }
        if(!liveRows.Any(r=>r.Route=="Напрямую" && r.Destination.EndsWith(":"+targetPort))) Console.WriteLine("Telemetry fixture: "+string.Join(" | ",telemetryLogs)+" / rows="+System.Text.Json.JsonSerializer.Serialize(liveRows));
        Check(liveRows.Any(r=>r.Route=="Напрямую" && r.Destination.EndsWith(":"+targetPort)),"Real "+telemetryCore+" reports actual direct route for local proxy request");
        if(telemetryCore=="Xray") Check(!telemetryLogs.Any(s=>s.Contains(" accepted ")),"Xray access records remain in memory and do not enter program journal");
        releaseTarget.TrySetResult(); await targetTask;
    }
    finally { releaseTarget.TrySetResult(); telemetryTimeout.Cancel(); localTarget.Stop(); await telemetryEngine.StopAll(); try { await targetTask; } catch(OperationCanceledException) {} }
}
var extendedQuick=System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(quickProfile))!;
extendedQuick.QuickRoutes=[new("domain_keyword","example","proxy"),new("ip_cidr","192.0.2.0/24","@direct",true)];
var extendedQuickJson=JsonTools.Object(RuntimePolicy.Config(extendedQuick,new Preferences(),ConnectionMode.VpnOnly));
Check(extendedQuickJson["route"]!["rules"]!.AsArray().Any(r=>r?["domain_keyword"]!=null)&&extendedQuickJson["route"]!["rules"]!.AsArray().Any(r=>JsonTools.Text(r?["ip_cidr"]?[0])=="192.0.2.0/24"),"Extended quick routes preserve domain keywords and CIDR networks");
var extendedXray=QuickRouting.Apply(quickXraySource,"Xray",[new("domain_keyword","example","proxy"),new("ip_cidr","192.0.2.0/24","@direct",true)]);
Check(JsonTools.Text(JsonTools.Object(extendedXray)["routing"]!["rules"]![0]!["domain"]![0])=="example","Xray keyword uses its substring domain syntax");
Reject(()=>ZapretQuickRoutes.Apply(["--wf-tcp=443","--filter-tcp=443","--dpi-desync=fake"],[new("domain_keyword","example","@zapret",Zapret:true)]),"Zapret keyword is rejected instead of silently capturing every domain");
if(args.Length>1)
{
    var extendedStore=new Store(Path.Combine(temp,"extended-route-validation"));extendedStore.Settings.Engines["sing-box"]=args[1];
    var checker=new EngineSession(extendedStore,_=>{});await checker.Check(extendedQuick);
    var xrayPath=Environment.GetEnvironmentVariable("RELAYDESK_TEST_XRAY");
    if(!string.IsNullOrEmpty(xrayPath)){extendedStore.Settings.Engines["Xray"]=xrayPath;await checker.Check(new Profile{Core="Xray",Config=extendedXray});}
    Check(true,"Real cores accept extended keyword and subnet rules without opening a connection");
}
Console.WriteLine($"COMPLETE: {count} checks passed.");

sealed class ReleaseHttpHandler(Func<System.Net.Http.HttpRequestMessage,System.Net.Http.HttpResponseMessage> respond) : System.Net.Http.HttpMessageHandler
{
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}

static class FixtureDb
{
    [System.Runtime.InteropServices.DllImport("winsqlite3.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)] static extern int sqlite3_open_v2([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string file, out IntPtr db, int flags, IntPtr vfs);
    [System.Runtime.InteropServices.DllImport("winsqlite3.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr db, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr user, IntPtr error);
    [System.Runtime.InteropServices.DllImport("winsqlite3.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    public static void Create(string file, string uuid)
    {
        if (sqlite3_open_v2(file, out var db, 6, IntPtr.Zero) != 0) throw new IOException("Fixture open failed");
        try { var sql = "CREATE TABLE ProfileItem(IndexId TEXT,ConfigType INT,Remarks TEXT,Address TEXT,Port INT,Password TEXT,Network TEXT,StreamSecurity TEXT); INSERT INTO ProfileItem VALUES('fixture',5,'SQLite fixture','example.org',443,'" + uuid + "','tcp','tls');"; if (sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) throw new IOException("Fixture create failed"); }
        finally { sqlite3_close(db); }
    }
}
