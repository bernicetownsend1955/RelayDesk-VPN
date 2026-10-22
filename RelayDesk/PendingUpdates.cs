using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace RelayDesk;

public record PendingCore(string Id,string Revision,string Executable,string Root,Dictionary<string,string> Files);
public record PendingApp(AppRelease Release,string Installer);
public static class PendingUpdates
{
    public static void Cancel(Store store)
    {
        foreach(var path in new[]{CorePath(store),AppPath(store.Folder)})if(File.Exists(path))File.Delete(path);
    }
    static string CorePath(Store store) => Path.Combine(store.Folder,"pending-core-updates.json");
    static string AppPath(string folder) => Path.Combine(folder,"pending-app-update.json");
    static string Hash(string path) { using var stream=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static void Save<T>(string path,T value) { var temporary=path+".tmp"; File.WriteAllText(temporary,JsonSerializer.Serialize(value,JsonTools.Options)); File.Move(temporary,path,true); }
    public static List<PendingCore> Cores(Store store) => File.Exists(CorePath(store)) ? JsonSerializer.Deserialize<List<PendingCore>>(File.ReadAllText(CorePath(store))) ?? [] : [];
    public static bool HasCore(Store store,ComponentUpdate update) => Cores(store).Any(p=>p.Id==update.Id && p.Revision==update.Revision);
    public static void StageCore(Store store,ComponentUpdate update,string executable)
    {
        _=ComponentUpdates.OfficialRepository(update.Id);
        var relative=Path.GetRelativePath(store.Folder,executable).Replace('\\','/').Split('/');
        if(relative.Length<5 || relative[0]!="components" || relative[1]!=update.Id || !Guid.TryParseExact(relative[2],"N",out _)) throw new InvalidDataException("Обновление вне каталога ядра.");
        var root=ComponentUpdates.Destination(store.Folder,string.Join('/',relative.Take(3))+"/unpacked");
        var files=Directory.GetFiles(root,"*",SearchOption.AllDirectories);
        if(files.Length>5000 || files.Sum(f=>new FileInfo(f).Length)>500_000_000) throw new InvalidDataException("Пакет ядра слишком большой.");
        var pending=new PendingCore(update.Id,update.Revision,Path.GetRelativePath(store.Folder,executable).Replace('\\','/'),Path.GetRelativePath(store.Folder,root).Replace('\\','/'),files.ToDictionary(f=>Path.GetRelativePath(root,f).Replace('\\','/'),Hash));
        var list=Cores(store);list.RemoveAll(p=>p.Id==update.Id);list.Add(pending);Save(CorePath(store),list);
    }
    public static void ApplyCores(Store store,Action<string> log)
    {
        if(!File.Exists(CorePath(store))) return;
        List<PendingCore> staged;
        try { staged=Cores(store); }
        catch(Exception ex) { log("Повреждён список обновлений ядер: "+ex.Message);File.Move(CorePath(store),CorePath(store)+".bad",true);return; }
        foreach(var pending in staged)
        {
            try
            {
                _=ComponentUpdates.OfficialRepository(pending.Id);
                if(!pending.Root.StartsWith("components/"+pending.Id+"/",StringComparison.Ordinal) || pending.Files.Count is 0 or >5000) throw new InvalidDataException("Некорректный пакет ядра.");
                var root=ComponentUpdates.Destination(store.Folder,pending.Root);
                foreach(var file in pending.Files)
                    if(!Hash(ComponentUpdates.Destination(root,file.Key)).Equals(file.Value,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Файлы ядра изменены после загрузки.");
                var exe=ComponentUpdates.Destination(store.Folder,pending.Executable);
                if(!exe.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !pending.Files.Keys.Any(f=>ComponentUpdates.Destination(root,f)==exe)) throw new InvalidDataException("EXE отсутствует в пакете.");
                var old=pending.Id=="zapret" ? store.Settings.ZapretExe : store.Settings.Engines.GetValueOrDefault(pending.Id,"");
                var receipt=store.Settings.ComponentInstalls.GetValueOrDefault(pending.Id);
                if(old==exe && receipt?.Revision==pending.Revision)continue;
                var oldArgs=store.Settings.ZapretArgs;var oldStrategies=store.Settings.StrategyList;
                try
                {
                    if(pending.Id=="zapret")
                    {
                        // Rebind only paths belonging to the previous distribution.
                        var oldBin=Path.GetDirectoryName(old);var newBin=Path.GetDirectoryName(exe)!;
                        if(!string.IsNullOrEmpty(oldBin)) { store.Settings.ZapretArgs=oldArgs.Replace(oldBin,newBin,StringComparison.OrdinalIgnoreCase);store.Settings.StrategyList=oldStrategies.Replace(oldBin,newBin,StringComparison.OrdinalIgnoreCase); }
                        if(string.IsNullOrWhiteSpace(store.Settings.ZapretArgs))
                        {
                            var strategies=FlowsealStrategies.Load(exe);
                            store.Settings.ZapretArgs=strategies[0].Arguments;
                            if(string.IsNullOrWhiteSpace(store.Settings.StrategyList))store.Settings.StrategyList=FlowsealStrategies.Format(strategies);
                        }
                        store.Settings.ZapretExe=exe;
                    }
                    else store.Settings.Engines[pending.Id]=exe;
                    store.Settings.ComponentInstalls[pending.Id]=new(pending.Revision,exe,old,DateTimeOffset.UtcNow);store.Save();
                }
                catch
                {
                    if(pending.Id=="zapret") { store.Settings.ZapretExe=old;store.Settings.ZapretArgs=oldArgs;store.Settings.StrategyList=oldStrategies; } else store.Settings.Engines[pending.Id]=old;
                    if(receipt==null)store.Settings.ComponentInstalls.Remove(pending.Id);else store.Settings.ComponentInstalls[pending.Id]=receipt;
                    throw;
                }
                log("Автоматически обновлено ядро "+pending.Id+" до "+pending.Revision);
            }
            catch(Exception ex) { log("Обновление ядра не применено: "+ex.Message); }
        }
        File.Delete(CorePath(store));
    }
    public static PendingApp? App(string folder)
    {
        if(!File.Exists(AppPath(folder)))return null;
        try { return JsonSerializer.Deserialize<PendingApp>(File.ReadAllText(AppPath(folder))); }
        catch(JsonException) { File.Move(AppPath(folder),AppPath(folder)+".bad",true);return null; }
    }
    public static void StageApp(string folder,AppRelease release,string installer)
    {
        ValidateApp(folder,new(release,installer));Save(AppPath(folder),new PendingApp(release,installer));
    }
    public static void ValidateApp(string folder,PendingApp pending)
    {
        var r=pending.Release;
        if(r.Repository!=AppUpdates.DefaultRepository || !Version.TryParse(r.Version,out _) || r.File.Name!=$"RelayDesk-Setup-{r.Version}-x64.exe" || r.File.Url!=$"https://github.com/{AppUpdates.DefaultRepository}/releases/download/v{r.Version}/{r.File.Name}") throw new InvalidDataException("Источник установщика не совпадает с закреплённым репозиторием.");
        var root=Path.GetFullPath(Path.Combine(folder,"application-updates"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(pending.Installer).StartsWith(root,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(pending.Installer)!=r.File.Name || new FileInfo(pending.Installer).Length!=r.File.Size || !Hash(pending.Installer).Equals(r.File.Hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Проверка отложенного установщика не пройдена.");
    }
    public static bool StartApp(string folder,string applicationFolder)
    {
        var pending=App(folder);if(pending==null)return false;
        if(!ComponentUpdates.Newer(pending.Release.Version,AppUpdates.CurrentVersion)){File.Delete(AppPath(folder));return false;}
        ValidateApp(folder,pending);
        var helper=Path.Combine(applicationFolder,"Apply-Update.ps1");if(!File.Exists(helper))throw new FileNotFoundException("Не найден помощник обновления.");
        var start=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true };
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",helper,"-ParentId",Environment.ProcessId.ToString(),"-Installer",pending.Installer,"-Sha256",pending.Release.File.Hash,"-DataFolder",folder})start.ArgumentList.Add(arg);
        // Install into the directory used by this copy, including portable/current copies.
        start.ArgumentList.Add("-InstallDirectory");start.ArgumentList.Add(Path.GetFullPath(applicationFolder));
        using var process=Process.Start(start)??throw new InvalidOperationException("Не удалось запустить установку обновления.");return true;
    }
}
