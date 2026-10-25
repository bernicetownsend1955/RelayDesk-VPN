using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace RelayDesk;

public record WindowsApplication(string Name,string Executable,string Source,bool DefaultBrowser=false)
{
    public override string ToString()=>Name+"  ·  "+Source+(Executable.Length==0?"  ·  EXE не указан Windows":"");
}
public static class WindowsApplications
{
    public static string Executable(string? command)
    {
        if(string.IsNullOrWhiteSpace(command))return "";
        var value=Environment.ExpandEnvironmentVariables(command.Trim());
        if(value.StartsWith('"')){int end=value.IndexOf('"',1);value=end>1?value[1..end]:"";}
        else{int end=value.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);value=end>=0?value[..(end+4)]:"";}
        try{return Path.IsPathFullyQualified(value)&&File.Exists(value)?Path.GetFullPath(value):"";}catch{return "";}
    }
    public static WindowsApplication? Browser()
    {
        // Read the user's HTTPS association; never write UserChoice or its hash.
        using var association=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        string? progId=association?.GetValue("ProgId") as string;
        if(string.IsNullOrWhiteSpace(progId))return null;
        using var command=Registry.ClassesRoot.OpenSubKey(progId+@"\shell\open\command");
        string path=Executable(command?.GetValue("") as string);
        if(path.Length==0)return null;
        string name=Path.GetFileNameWithoutExtension(path);
        try{name=FileVersionInfo.GetVersionInfo(path).ProductName??name;}catch{}
        return new(name,path,"По умолчанию для HTTPS",true);
    }
    static string FindExecutable(string? installLocation,string name)
    {
        if(string.IsNullOrWhiteSpace(installLocation)||!Directory.Exists(installLocation))return "";
        try
        {
            var candidates=Directory.GetFiles(installLocation,"*.exe").Where(f=>!Path.GetFileName(f).Contains("unins",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(f).Contains("update",StringComparison.OrdinalIgnoreCase)&&!Path.GetFileName(f).Contains("setup",StringComparison.OrdinalIgnoreCase)).ToArray();
            return candidates.FirstOrDefault(f=>Path.GetFileNameWithoutExtension(f).Contains(name.Replace(" ",""),StringComparison.OrdinalIgnoreCase))??(candidates.Length==1?candidates[0]:"");
        }catch{return "";}
    }
    public static List<WindowsApplication> Scan()
    {
        var apps=new List<WindowsApplication>();
        try{var browser=Browser();if(browser!=null)apps.Add(browser);}catch{}
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            try
            {
                using var root=RegistryKey.OpenBaseKey(hive,view);
                using var uninstall=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if(uninstall==null)continue;
                foreach(string key in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var item=uninstall.OpenSubKey(key);
                        string? name=item?.GetValue("DisplayName") as string;
                        if(string.IsNullOrWhiteSpace(name)||Equals(item?.GetValue("SystemComponent"),1))continue;
                        string exe=Executable(item?.GetValue("DisplayIcon") as string);
                        if(exe.Length==0)exe=FindExecutable(item?.GetValue("InstallLocation") as string,name);
                        apps.Add(new(name,exe,"Установлено в Windows"));
                    }catch{}
                }
            }catch{}
        }
        // Discord's per-user updater often registers Update.exe rather than the client.
        foreach(string product in new[]{"Discord","DiscordPTB","DiscordCanary","Vesktop"})
        {
            string location=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),product);
            if(!Directory.Exists(location))continue;
            try
            {
                string? exe=Directory.GetDirectories(location,"app-*").OrderByDescending(Directory.GetLastWriteTimeUtc).Select(p=>Path.Combine(p,product+".exe")).FirstOrDefault(File.Exists);
                exe??=new[]{Path.Combine(location,product+".exe"),Path.Combine(location,"vesktop.exe")}.FirstOrDefault(File.Exists);
                if(exe!=null)apps.Insert(0,new(product,exe,"Найдено в профиле Windows"));
            }catch{}
        }
        // A running client supplies an exact EXE when its installer omits that field.
        foreach(var process in Process.GetProcesses())
        {
            using(process)
            {
                try
                {
                    if(!new[]{"Discord","DiscordPTB","DiscordCanary","vesktop"}.Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase))continue;
                    string? path=process.MainModule?.FileName;if(path!=null&&File.Exists(path))apps.Insert(0,new(process.ProcessName,path,"Запущено сейчас"));
                }catch{}
            }
        }
        return apps.GroupBy(a=>a.Name+"|"+a.Executable,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderByDescending(a=>a.DefaultBrowser).ThenByDescending(a=>a.Executable.Length>0).ThenBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
