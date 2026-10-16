using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RelayDesk;

public record ComponentInstall(string Revision, string Executable, string PreviousExecutable, DateTimeOffset InstalledAt);
public record UpdateFile(string Name, string Url, long Size, string Hash, bool GitBlob = false);
public record ComponentUpdate(string Id, string Name, string Revision, string Current, string Status, bool Available, string Page, List<UpdateFile> Files);

public sealed class ComponentUpdates(Store store)
{
    static readonly HttpClient http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RelayDesk/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json"); return client;
    }
    static async Task<JsonObject> Get(string url, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return JsonTools.Object(await http.GetStringAsync(url, timeout.Token));
    }
    public static Version? NumericVersion(string value)
    {
        var match = Regex.Match(value, @"(?<!\d)(\d+\.\d+\.\d+)(?:\.\d+)?");
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }
    public static bool Newer(string latest, string current) => NumericVersion(latest) is Version remote && NumericVersion(current) is Version local && (remote > local || remote == local && current.Contains('-') && !latest.Contains('-'));
    public async Task<ComponentUpdate> CheckSingBox(CancellationToken cancellation)
    {
        var release = await Get("https://api.github.com/repos/SagerNet/sing-box/releases/latest", cancellation);
        if (JsonTools.Text(release["prerelease"]) == "true" || JsonTools.Text(release["draft"]) == "true") throw new InvalidDataException("Получен нестабильный релиз sing-box.");
        var version = JsonTools.Text(release["tag_name"]);
        var asset = release["assets"]!.AsArray().FirstOrDefault(n => JsonTools.Text(n?["name"]).EndsWith("windows-amd64.zip", StringComparison.Ordinal)) ?? throw new InvalidDataException("Нет Windows x64-архива sing-box.");
        var path = store.Settings.Engines.GetValueOrDefault("sing-box", ""); var current = await LocalSingBox(path, cancellation);
        var available = !File.Exists(path) || current == "Версия не определена" || Newer(version, current);
        var hash = JsonTools.Text(asset["digest"]);
        if (!hash.StartsWith("sha256:") || hash.Length != 71) throw new InvalidDataException("Для архива sing-box не опубликован SHA-256.");
        var file = new UpdateFile(JsonTools.Text(asset["name"]), JsonTools.Text(asset["browser_download_url"]), asset["size"]!.GetValue<long>(), hash[7..]);
        return new("sing-box", "sing-box", version, current, available ? File.Exists(path) ? current == "Версия не определена" ? "Можно установить официальный релиз; сравнить версии не удалось" : "Доступно обновление" : "Не установлен" : "Обновление не требуется", available, JsonTools.Text(release["html_url"]), [file]);
    }
    public async Task<ComponentUpdate> CheckZapret(CancellationToken cancellation)
    {
        var release = await Get("https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest", cancellation);
        return FlowsealRelease(release, store.Settings.ZapretExe, store.Settings.ComponentInstalls.GetValueOrDefault("zapret"));
    }
    public static string RevisionLabel(string revision) => revision.StartsWith("flowseal:", StringComparison.Ordinal) ? "Flowseal " + revision[9..] : Regex.IsMatch(revision, "^[0-9a-f]{40}$") ? revision[..10] : revision;
    public static bool NewerFlowseal(string latest, string current)
    {
        var remote = NumericVersion(latest); var local = NumericVersion(current);
        if (remote == null || local == null) return false;
        if (remote != local) return remote > local;
        return string.Compare(Regex.Match(latest, @"\d+\.\d+\.\d+([a-z]*)$").Groups[1].Value, Regex.Match(current, @"\d+\.\d+\.\d+([a-z]*)$").Groups[1].Value, StringComparison.Ordinal) > 0;
    }
    public static ComponentUpdate FlowsealRelease(JsonObject release, string local, ComponentInstall? installed)
    {
        if (release["prerelease"]?.GetValue<bool>() == true || release["draft"]?.GetValue<bool>() == true) throw new InvalidDataException("Получен нестабильный релиз Flowseal.");
        var tag = JsonTools.Text(release["tag_name"]);
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+[a-z]?$") ) throw new InvalidDataException("Неизвестный формат версии Flowseal.");
        var name = "zapret-discord-youtube-" + tag.TrimStart('v') + ".zip";
        var asset = release["assets"]?.AsArray().SingleOrDefault(n => JsonTools.Text(n?["name"]) == name) ?? throw new InvalidDataException("В релизе Flowseal нет Windows ZIP-архива.");
        var hash = JsonTools.Text(asset["digest"]);
        if (!Regex.IsMatch(hash, "^sha256:[0-9a-fA-F]{64}$")) throw new InvalidDataException("Для архива Flowseal не опубликован SHA-256.");
        var file = new UpdateFile(name, JsonTools.Text(asset["browser_download_url"]), asset["size"]!.GetValue<long>(), hash[7..]);
        ValidateUrl(file, "zapret");
        var page = "https://github.com/Flowseal/zapret-discord-youtube/releases/tag/" + tag;
        bool exists = File.Exists(local);
        bool known = exists && installed != null && installed.Revision.StartsWith("flowseal:", StringComparison.Ordinal) && string.Equals(installed.Executable, local, StringComparison.OrdinalIgnoreCase);
        var current = !exists ? "Не установлен" : known ? RevisionLabel(installed!.Revision) : "Внешняя сборка; версия Flowseal не определена";
        var complete = exists && new[] { "cygwin1.dll", "WinDivert.dll", "WinDivert64.sys" }.All(n => File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(local))!, n)));
        bool newerLocal = known && NewerFlowseal(installed!.Revision[9..], tag);
        var available = !newerLocal && (!exists || !known || !complete || NewerFlowseal(tag, installed!.Revision[9..]));
        var status = !exists ? "Не установлен" : !known ? "Можно перейти на релиз Flowseal; сравнить версии внешней сборки не удалось" : newerLocal ? "Установленная версия новее релиза источника; понижение не предлагается" : !complete ? "Не хватает файлов; доступна повторная установка" : available ? "Доступно обновление Flowseal" : "Обновление не требуется";
        return new("zapret", "zapret / Flowseal", "flowseal:" + tag, current, status, available, page, [file]);
    }
    static async Task<string> LocalSingBox(string path, CancellationToken cancellation)
    {
        if (!File.Exists(path)) return "Не установлен";
        var info = new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))! };
        info.ArgumentList.Add("version"); using var process = new Process { StartInfo = info };
        try
        {
            process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(cancellation); var stderr = process.StandardError.ReadToEndAsync(cancellation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(4));
            await process.WaitForExitAsync(timeout.Token); var text = await stdout; await stderr;
            var match = Regex.Match(text, @"sing-box version\s+([^\s]+)", RegexOptions.IgnoreCase);
            return process.ExitCode == 0 && match.Success ? match.Groups[1].Value : "Версия не определена";
        }
        catch (Exception) when (!cancellation.IsCancellationRequested) { return "Версия не определена"; }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    }
    public static string Destination(string root, string name)
    {
        if (name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p => p is ".." or "." or "")) throw new InvalidDataException("Некорректный путь внутри пакета.");
        var full = Path.GetFullPath(Path.Combine(root, name));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Файл выходит за папку пакета.");
        return full;
    }
    public static async Task<string> HashFile(string path, bool gitBlob, CancellationToken cancellation)
    {
        using var hash = IncrementalHash.CreateHash(gitBlob ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256);
        await using var stream = File.OpenRead(path);
        if (gitBlob) hash.AppendData(Encoding.UTF8.GetBytes("blob " + stream.Length + "\0"));
        var buffer = new byte[65536]; int read; while ((read = await stream.ReadAsync(buffer, cancellation)) > 0) hash.AppendData(buffer.AsSpan(0, read));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    static void ValidateUrl(UpdateFile file, string id)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0) throw new InvalidDataException("Некорректный источник обновления.");
        var prefix = id == "zapret" ? "https://github.com/Flowseal/zapret-discord-youtube/releases/download/" : "https://github.com/SagerNet/sing-box/releases/download/";
        if (file.GitBlob) throw new InvalidDataException("Для обновления требуется релизный архив с SHA-256.");
        if (!file.Url.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Пакет получен не из официального источника.");
        if (file.Size is <= 0 or > 150_000_000 || !Regex.IsMatch(file.Hash, file.GitBlob ? "^[0-9a-fA-F]{40}$" : "^[0-9a-fA-F]{64}$")) throw new InvalidDataException("Некорректные размер или хеш пакета.");
    }
    public async Task<string> PrepareApproved(ComponentUpdate update, Action<string> progress, CancellationToken cancellation)
    {
        if (update.Id is not ("sing-box" or "zapret") || update.Files.Count is 0 or > 1500 || update.Files.Sum(f => f.Size) > 250_000_000) throw new InvalidDataException("Некорректный пакет обновления.");
        foreach (var file in update.Files) ValidateUrl(file, update.Id);
        var destination = Path.Combine(store.Folder, "components", update.Id, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(destination);
        var package = Path.Combine(destination, "package"); Directory.CreateDirectory(package);
        for (var i = 0; i < update.Files.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested(); var file = update.Files[i]; var output = Destination(package, file.Name); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            progress($"Загрузка {update.Name}: {i + 1} / {update.Files.Count}");
            using var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellation); response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var target = File.Create(output))
            {
                var buffer = new byte[65536]; long size = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0) { size += read; if (size > file.Size) throw new InvalidDataException("Размер загрузки больше опубликованного."); await target.WriteAsync(buffer.AsMemory(0, read), cancellation); }
                if (size != file.Size) throw new InvalidDataException("Загрузка неполная.");
            }
            if (!string.Equals(await HashFile(output, file.GitBlob, cancellation), file.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Контрольная сумма не совпала. Обновление не применено.");
        }
        string executable;
        if (update.Id == "sing-box")
        {
            var unpacked = Path.Combine(destination, "unpacked"); Directory.CreateDirectory(unpacked);
            Extract(Path.Combine(package, update.Files.Single().Name), unpacked);
            executable = Directory.GetFiles(unpacked, "sing-box.exe", SearchOption.AllDirectories).Single();
        }
        else
        {
            var unpacked = Path.Combine(destination, "unpacked"); Directory.CreateDirectory(unpacked);
            Extract(Path.Combine(package, update.Files.Single().Name), unpacked);
            executable = FlowsealExecutable(unpacked);
        }
        if (new FileInfo(executable).Length < 1024) throw new InvalidDataException("Исполняемый файл повреждён.");
        File.WriteAllText(Path.Combine(destination, "receipt.json"), System.Text.Json.JsonSerializer.Serialize(update, JsonTools.Options));
        return executable;
    }
    public static string FlowsealExecutable(string unpacked)
    {
        var candidates = Directory.GetFiles(unpacked, "winws.exe", SearchOption.AllDirectories).Where(p => string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "bin", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("В архиве Flowseal должен быть один bin/winws.exe.");
        var executable = candidates[0]; var bin = Path.GetDirectoryName(executable)!;
        foreach (var required in new[] { "cygwin1.dll", "WinDivert.dll", "WinDivert64.sys" }) if (!File.Exists(Path.Combine(bin, required))) throw new InvalidDataException("В сборке отсутствует " + required);
        return executable;
    }
    public static void Extract(string zip, string folder)
    {
        using var archive = ZipFile.OpenRead(zip); long total = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Символические ссылки в пакете не поддерживаются.");
            total += entry.Length; if (total > 500_000_000 || archive.Entries.Count > 5000) throw new InvalidDataException("Архив превышает допустимый размер.");
            var path = Destination(folder, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(path)!); entry.ExtractToFile(path, false);
        }
    }
}
