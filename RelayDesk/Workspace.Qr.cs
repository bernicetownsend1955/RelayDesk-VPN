using System.Text.Json;
using System.Windows;
using System.IO;

namespace RelayDesk;
public partial class Workspace
{
    public void ModernImportText(string text)=>ImportText(text);
    public Task ModernImportSubscription(string text)=>App.UseOrbitUi?ImportOrbitSubscription(text):ImportSubscription(text);
    public void ModernAcceptShare(QrShare share)
    {
        if(ModernBusy||HasChanges())throw new InvalidOperationException("Сохрани изменения и дождись завершения операции.");
        var settings=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(store.Settings))!;
        var profile=share.Profile==null?null:SharedRouting.Copy(share.Profile);
        if(profile!=null)
        {
            if(profile.Core is not ("sing-box" or "Xray" or "mihomo")||string.IsNullOrWhiteSpace(profile.Name)||profile.Name.Length>100)throw new FormatException("Некорректный профиль в QR-коде.");
            profile.Id=Guid.NewGuid().ToString("N");profile.Executable="";profile.SubscriptionId="";profile.SubscriptionGroup="";profile.QuickRoutes=[];
            _=JsonTools.Build(profile);
        }
        if(share.Settings is SharedNetworkSettings incoming)
        {
            if(!RoutingFallback.Valid(incoming.Fallback)||!RoutingPresets.Valid(incoming.Preset)||incoming.Rules==null||incoming.Rules.Count>3000)throw new FormatException("Некорректные общие настройки.");
            foreach(var rule in incoming.Rules)QuickRouting.Validate(rule);
            if(incoming.Connection is ConnectionSettings connection){_=JsonTools.Port(connection.LocalPort.ToString());settings.Connection=connection;}
            settings.SharedQuickRoutes=SharedRouting.Normalize(incoming.Rules);settings.FallbackRoute=incoming.Fallback;settings.RoutingPreset=incoming.Preset;
            if(settings.Connection!=null&&incoming.Rules.Any(r=>r.Kind is "process_name" or "process_path"))settings.Connection=settings.Connection with{Tun=true};
        }
        foreach(var target in profile==null?store.Profiles:store.Profiles.Append(profile))if(target.Core is "sing-box" or "Xray")_=RuntimePolicy.Config(target,settings,session.ZapretRunning?ConnectionMode.Combined:ConnectionMode.VpnOnly);
        var previous=(store.Settings.Connection,store.Settings.SharedQuickRoutes,store.Settings.FallbackRoute,store.Settings.RoutingPreset);var profiles=store.Profiles.ToArray();
        string? selectedId=selected?.Id;
        var backup=Path.Combine(store.Folder,"backups","before-qr-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(backup);
        foreach(var name in new[]{"profiles.json","settings.json"})if(File.Exists(Path.Combine(store.Folder,name)))File.Copy(Path.Combine(store.Folder,name),Path.Combine(backup,name));
        try
        {
            if(share.Settings!=null){store.Settings.Connection=settings.Connection;store.Settings.SharedQuickRoutes=settings.SharedQuickRoutes;store.Settings.FallbackRoute=settings.FallbackRoute;store.Settings.RoutingPreset=settings.RoutingPreset;store.Profiles.Clear();store.Profiles.AddRange(profiles.Select(p=>{var copy=SharedRouting.Copy(p);copy.QuickRoutes=settings.SharedQuickRoutes!.ToList();return copy;}));if(profile!=null)profile.QuickRoutes=settings.SharedQuickRoutes!.ToList();}
            if(profile!=null)store.Profiles.Add(profile);store.Save();
        }
        catch{store.Profiles.Clear();store.Profiles.AddRange(profiles);store.Settings.Connection=previous.Connection;store.Settings.SharedQuickRoutes=previous.SharedQuickRoutes;store.Settings.FallbackRoute=previous.FallbackRoute;store.Settings.RoutingPreset=previous.RoutingPreset;foreach(var name in new[]{"profiles.json","settings.json"})if(File.Exists(Path.Combine(backup,name)))File.Copy(Path.Combine(backup,name),Path.Combine(store.Folder,name),true);throw;}
        selected=store.Profiles.FirstOrDefault(p=>p.Id==selectedId)??profile;loading=true;RefreshProfiles();ProfileList.SelectedItem=selected;loading=false;if(selected!=null)DisplayProfile();
        Write("QR-пакет сохранён. Работающее подключение не менялось.");
    }
}
