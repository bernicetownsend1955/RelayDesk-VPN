using System.IO;
using System.Text.Json;
using System.Windows;

namespace RelayDesk;
public partial class Workspace
{
    public event Action<int>? ModernSectionChanged;
    public void ModernSaveStartup(bool enabled,bool vpn,bool zapret,Profile? profile)
    {
        WindowsStartupToggle.IsChecked=enabled;StartupVpnToggle.IsChecked=vpn;StartupZapretToggle.IsChecked=zapret;StartupProfileChoice.SelectedItem=profile;
        SaveStartupSettings();
    }
    public void ModernSaveConnection(bool tun,bool systemProxy,int port)
    {
        if(ModernBusy||HasChanges())throw new InvalidOperationException("Сохрани изменения профиля и дождись завершения операции.");
        _=JsonTools.Port(port.ToString());
        if(!tun&&store.Settings.SharedQuickRoutes?.Any(r=>r.Kind is "process_name" or "process_path")==true)throw new InvalidOperationException("Для общих правил приложений нужен TUN. Удали эти правила, чтобы отключить TUN.");
        var old=store.Settings.Connection;store.Settings.Connection=new(tun,systemProxy,port);
        try{if(selected?.Core is "sing-box" or "Xray")_=RuntimePolicy.Config(selected,store.Settings,session.ZapretRunning?ConnectionMode.Combined:ConnectionMode.VpnOnly);store.Save();}
        catch{store.Settings.Connection=old;throw;}
        DisplayProfile();
    }
    public void ModernEmbed()
    {
        var root=(System.Windows.Controls.Grid)Content;
        root.ColumnDefinitions[0].Width=new GridLength(0);root.ColumnDefinitions[1].Width=new GridLength(0);
        foreach(var child in root.Children.OfType<FrameworkElement>().Where(e=>System.Windows.Controls.Grid.GetColumn(e)==0))child.Visibility=Visibility.Collapsed;
        ContentGrid.Margin=new Thickness(0);
        foreach(var child in ContentGrid.Children.OfType<FrameworkElement>().Where(e=>System.Windows.Controls.Grid.GetRow(e)==0))child.Visibility=Visibility.Collapsed;
        Background=System.Windows.Media.Brushes.Transparent;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/ModernTheme.xaml")});
        Tabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,Tabs))ModernSectionChanged?.Invoke(Tabs.SelectedIndex);};
    }
    public void ModernRestyle()
    {
        UpdateLayout();
        void Visit(System.Windows.DependencyObject node)
        {
            if(node is System.Windows.Controls.Button button)button.Style=(Style)FindResource(typeof(System.Windows.Controls.Button));
            else if(node is System.Windows.Controls.TextBox field)field.Style=(Style)FindResource(typeof(System.Windows.Controls.TextBox));
            else if(node is System.Windows.Controls.ComboBox combo)combo.Style=(Style)FindResource(typeof(System.Windows.Controls.ComboBox));
            else if(node is System.Windows.Controls.CheckBox check)check.Style=(Style)FindResource(typeof(System.Windows.Controls.CheckBox));
            for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)Visit(System.Windows.Media.VisualTreeHelper.GetChild(node,i));
        }
        Visit(Tabs);
    }
    public Store ModernStore=>store;
    public EngineSession ModernSession=>session;
    public Profile? ModernProfile=>selected;
    public bool ModernBusy=>session.Busy||automationBusy||installingUpdate||closing;
    public bool ModernConnectionsVisible {get;set;}
    public IReadOnlyList<ConnectionRow> ModernConnections=>connectionRows;
    public string ModernConnectionsStatus=>ConnectionsStatus.Text;
    public string ModernMessage=>ActionMessage.Text;
    public string ModernTcp=>TcpResult.Text;
    public string ModernHttps=>HttpsResult.Text;
    public string ModernLog=>Log.Text;
    public bool ModernHasUpdate=>appRelease?.Available==true;
    public string ModernUpdateVersion=>appRelease?.Version??"";
    public string ModernUpdateNotes=>appRelease?.Notes??"";
    public void ModernPrepareData()
    {
        if(store.Settings.ModernUiVersion>=1&&store.Settings.SharedQuickRoutes!=null&&store.Settings.Connection!=null)return;
        var backup=Path.Combine(store.Folder,"backups","before-shared-routes-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        foreach(string name in new[]{"profiles.json","settings.json"}){var file=Path.Combine(store.Folder,name);if(File.Exists(file))File.Copy(file,Path.Combine(backup,name),true);}
        store.Settings.ModernUiVersion=1;SharedRouting.Migrate(store);SharedConnection.Migrate(store);store.Save();DisplayProfile();
    }
    public bool ModernSelect(Profile profile)
    {
        if(ModernBusy)return false;
        if(selected?.Id!=profile.Id&&!CanSwitch())return false;
        loading=true;SubscriptionGroupFilter.SelectedIndex=0;ProfileSearch.Text="";RefreshGroupedProfiles();ProfileList.SelectedItem=profile;selected=profile;loading=false;DisplayProfile();
        store.Settings.LastConnectedProfileId=profile.Id;store.Save();return true;
    }
    public void ModernEnableSubscriptionTraffic()
    {
        var profile=RequireProfile();
        if(!profile.Managed||profile.SubscriptionId.Length==0)throw new InvalidOperationException("Выбери профиль из подписки.");
        if(ModernBusy||HasChanges()||ReferenceEquals(session.ActiveProfile,profile))throw new InvalidOperationException("Отключи выбранный профиль и сохрани его изменения. Другое подключение можно оставить включённым.");
        var copy=JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile))!;
        copy.SystemProxy=true;
        if(copy.Dns=="1.1.1.1")copy.Dns="system";
        _=RuntimePolicy.Config(copy,store.Settings,session.ZapretRunning?ConnectionMode.Combined:ConnectionMode.VpnOnly);
        store.Profiles[store.Profiles.IndexOf(profile)]=copy;selected=copy;
        if(store.Settings.Connection!=null)store.Settings.Connection=store.Settings.Connection with {SystemProxy=true};
        loading=true;RefreshProfiles();ProfileList.SelectedItem=copy;loading=false;DisplayProfile();store.Save();
    }
    public void ModernToggleVpn()=>ToggleVpnClick(this,new RoutedEventArgs());
    public void ModernToggleZapret()=>ToggleZapretClick(this,new RoutedEventArgs());
    public void ModernTcpCheck()=>TcpClick(this,new RoutedEventArgs());
    public void ModernHttpsCheck()=>HttpsClick(this,new RoutedEventArgs());
    public void ModernOpen(int tab){Tabs.SelectedIndex=tab;}
    public void ModernImport(System.Windows.Controls.Button target)=>OpenProfileAddMenu(target,new RoutedEventArgs());
    public void ModernGroups()=>EditSubscriptionGroups(this,new RoutedEventArgs());
    public void ModernAuto(bool server,bool zapret)
    {
        if(ModernBusy)throw new InvalidOperationException("Дождись завершения операции.");
        AutoServer.IsChecked=server;AutoZapret.IsChecked=zapret;store.Settings.AutoServer=server;store.Settings.AutoZapret=zapret;store.Save();
    }
    public void ModernSaveZapretStrategy(Strategy strategy)
    {
        if(ModernBusy||session.ZapretRunning)throw new InvalidOperationException("Останови zapret перед сменой стратегии. VPN можно оставить включённым.");
        _=DiscordScope.Arguments(new Preferences{ZapretDiscordOnly=true},strategy.Arguments);
        store.Settings.ZapretArgs=strategy.Arguments;ZapretArgs.Text=strategy.Arguments;
        store.Settings.AutoZapret=false;AutoZapret.IsChecked=false;store.Save();
    }
    public void ModernScope(bool discordOnly)
    {
        if(ModernBusy||session.ZapretRunning)throw new InvalidOperationException("Изменяй область zapret после его отключения.");
        DiscordOnly.IsChecked=discordOnly;SaveAutomation(false);
    }
    public void ModernSaveRoutes(IReadOnlyList<QuickRoute> routes)
    {
        if(ModernBusy||HasChanges())throw new InvalidOperationException("Сохрани изменения профиля и дождись завершения операции.");
        var profile=RequireProfile();
        foreach(var route in routes)QuickRouting.Validate(route);
        var previous=store.Settings.SharedQuickRoutes;
        var normalized=SharedRouting.Normalize(routes);
        var settings=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(store.Settings))!;
        settings.SharedQuickRoutes=normalized;
        if(settings.Connection!=null&&normalized.Any(r=>r.Kind is "process_name" or "process_path"))settings.Connection=settings.Connection with {Tun=true};
        _=RuntimePolicy.Config(profile,settings,session.ZapretRunning?ConnectionMode.Combined:ConnectionMode.VpnOnly);
        if(routes.Any(r=>r.Zapret))_=ZapretQuickRoutes.Apply(RuntimePolicy.ZapretArguments(store.Settings,ConnectionMode.Combined),routes,store.Settings.ZapretExe,store.Settings.VoiceUdpDesync);
        // Keep the active session's profile snapshot intact until Reconfigure.
        // Otherwise Transition sees the same object and skips the restart.
        var previousProfiles=store.Profiles.ToArray();
        var copies=previousProfiles.Select(p=>{var copy=SharedRouting.Copy(p);copy.QuickRoutes=normalized.ToList();return copy;}).ToArray();
        var previousConnection=store.Settings.Connection;
        store.Profiles.Clear();store.Profiles.AddRange(copies);store.Settings.SharedQuickRoutes=normalized;store.Settings.Connection=settings.Connection;
        try{store.Save();}catch{store.Profiles.Clear();store.Profiles.AddRange(previousProfiles);store.Settings.SharedQuickRoutes=previous;store.Settings.Connection=previousConnection;throw;}
        selected=copies.Single(p=>p.Id==profile.Id);loading=true;RefreshProfiles();ProfileList.SelectedItem=selected;loading=false;DisplayProfile();
    }
    public async Task ModernApplyRoutes()
    {
        if(ModernBusy||HasChanges())throw new InvalidOperationException("Сохрани изменения и дождись завершения операции.");
        var profile=session.ProxyRunning?store.Profiles.Single(p=>p.Id==session.ActiveProfile!.Id):RequireProfile();
        await session.Reconfigure(profile);
    }
}
