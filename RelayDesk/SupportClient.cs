using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace RelayDesk;

public sealed class SupportClient : IDisposable
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(25) };
    Process? tunnel;
    async Task<HttpResponseMessage> Post(string path, object body, CancellationToken ct)
    {
        using var content = new ByteArrayContent(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await http.PostAsync(path, content, ct);
    }
    public void Configure(string host, string token)
    {
        if (!System.Net.IPAddress.TryParse(host, out _)) throw new InvalidOperationException("Укажи IP ВДС.");
        http.BaseAddress ??= new Uri("http://127.0.0.1:18765/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        if (tunnel is { HasExited: false }) return;
        var start = new ProcessStartInfo("ssh.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-N", "-T", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "-o", "ExitOnForwardFailure=yes", "-o", "ServerAliveInterval=30", "-o", "ConnectTimeout=10", "-L", "127.0.0.1:18765:127.0.0.1:18764", "Admin@" + host }) start.ArgumentList.Add(arg);
        tunnel = Process.Start(start) ?? throw new IOException("Не удалось открыть SSH-туннель.");
    }
    public async Task<string> Upload(CancellationToken ct, bool context = false)
    {
        var batch = ProgramJournal.Pending();
        var report = new { version = typeof(SupportClient).Assembly.GetName().Version?.ToString(), os = Environment.OSVersion.VersionString, logs = context ? ProgramJournal.Snapshot() : batch.Logs };
        using var response = await Post("reports", report, ct);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonObject>(ct))!["id"]!.ToString();
        if (!context) ProgramJournal.Acknowledge(batch);
        return id;
    }
    public async Task<JsonObject> Repair(string report, string issue, string requestId, CancellationToken ct)
    {
        using var response = await Post("jobs", new { report, issue = ProgramJournal.Clean(issue), request_id = requestId }, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>(ct))!;
    }
    public async Task<JsonObject> Status(string id, CancellationToken ct) => await http.GetFromJsonAsync<JsonObject>("jobs/" + Guid.ParseExact(id, "N").ToString("N"), ct) ?? throw new IOException("Пустой ответ ВДС.");
    public async Task<string> Download(string id, string folder, CancellationToken ct)
    {
        var job = await Status(id, ct);
        if (job["state"]?.ToString() != "ready") throw new IOException("Сборка ещё не готова.");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "RelayDesk-" + Guid.ParseExact(id, "N").ToString("N") + ".zip");
        var temp = file + ".part";
        try
        {
            using var response = await http.GetAsync("artifacts/" + id, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(temp))
            {
                byte[] buffer = new byte[65536]; long total = 0; int n;
                while ((n = await input.ReadAsync(buffer, ct)) != 0)
                {
                    total += n; if (total > 300L * 1024 * 1024) throw new IOException("Сборка превышает 300 МБ.");
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            await using var check = File.OpenRead(temp);
            var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(check, ct));
            if (!hash.Equals(job["sha256"]?.ToString(), StringComparison.OrdinalIgnoreCase)) throw new IOException("Контрольная сумма сборки не совпала.");
            check.Close(); File.Move(temp, file, true); return file;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose()
    {
        http.Dispose();
        try { if (tunnel is { HasExited: false }) tunnel.Kill(); } catch (InvalidOperationException) { }
        tunnel?.Dispose();
    }
}
