using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RelayDesk;

public static class ProgramJournal
{
    static readonly object Gate = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Secrets = new();
    public static void RegisterConfig(string config)
    {
        try
        {
            using var doc = JsonDocument.Parse(config);
            void Walk(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.Object)
                    foreach (var field in node.EnumerateObject())
                    {
                        if (Regex.IsMatch(field.Name, "(?i)password|uuid|token|secret|key|short.?id") && field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is { Length: >= 4 } secret) Secrets.TryAdd(secret, 0);
                        Walk(field.Value);
                    }
                else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) Walk(item);
            }
            Walk(doc.RootElement);
        }
        catch (JsonException) { }
    }
    public static string Folder => Path.Combine(Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelayDesk"), "logs");
    public static string Clean(string value)
    {
        foreach (var secret in Secrets.Keys.OrderByDescending(x => x.Length)) value = value.Replace(secret, "[скрыто]", StringComparison.Ordinal);
        value = Regex.Replace(value, @"\x1B\[[0-9;]*[A-Za-z]", "");
        value = Regex.Replace(value, @"(?i)\b(?:https?|vless|vmess|trojan|ss|ssr)://[^\s<>""']+", "[ссылка скрыта]");
        value = Regex.Replace(value, @"(?i)([""']?(?:password|passwd|uuid|token|authorization|secret|private_key|public_key|short_id|subscription)[""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;}]+)", "$1[скрыто]");
        value = Regex.Replace(value, @"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b|\b[0-9a-fA-F]{32,}\b|\b[A-Za-z0-9_+/=-]{40,}\b", "[идентификатор скрыт]");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 3) value = value.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return value.Length > 12000 ? value[..12000] + " [обрезано]" : value;
    }
    public static void Write(string message, string category = "app")
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".ndjson");
                if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024) return;
                File.AppendAllText(path, JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, category, message = Clean(message) }) + "\n");
                foreach (var old in new DirectoryInfo(Folder).GetFiles("*.ndjson").Where(f => f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-14))) old.Delete();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static string[] Snapshot()
    {
        lock (Gate)
        {
            if (!Directory.Exists(Folder)) return [];
            var rows = Directory.GetFiles(Folder, "*.ndjson").Order().TakeLast(2).SelectMany(File.ReadLines).TakeLast(1500).ToArray();
            var result = new List<string>(); int bytes = 0;
            foreach (var row in rows.Reverse())
            {
                var safe = Clean(row); bytes += System.Text.Encoding.UTF8.GetByteCount(safe);
                if (bytes > 450000) break;
                result.Add(safe);
            }
            result.Reverse(); return result.ToArray();
        }
    }
    public sealed record Batch(string[] Logs, Dictionary<string, int> Positions);
    static string CursorPath => Path.Combine(Directory.GetParent(Folder)!.FullName, "support-cursor.json");
    public static Batch Pending()
    {
        lock (Gate)
        {
            var positions = new Dictionary<string, int>();
            try { if (File.Exists(CursorPath)) positions = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(CursorPath)) ?? new(); }
            catch (JsonException) { }
            var result = new List<string>(); int bytes = 0;
            if (!Directory.Exists(Folder)) return new([], positions);
            foreach (var file in Directory.GetFiles(Folder, "*.ndjson").Order())
            {
                var name = Path.GetFileName(file); int line = 0, skip = positions.GetValueOrDefault(name);
                foreach (var row in File.ReadLines(file))
                {
                    if (line++ < skip) continue;
                    var safe = Clean(row); bytes += System.Text.Encoding.UTF8.GetByteCount(safe);
                    if (bytes > 450000 || result.Count == 1500) return new(result.ToArray(), positions);
                    result.Add(safe); positions[name] = line;
                }
            }
            return new(result.ToArray(), positions);
        }
    }
    public static void Acknowledge(Batch batch)
    {
        lock (Gate)
        {
            File.WriteAllText(CursorPath + ".tmp", JsonSerializer.Serialize(batch.Positions));
            File.Move(CursorPath + ".tmp", CursorPath, true);
        }
    }
}
