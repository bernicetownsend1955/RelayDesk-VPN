using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RelayDesk;
public sealed class ModernServer
{
    public string Name {get;set;}="";
    public string ProfileId {get;set;}="";
}
public sealed class ModernUiState
{
    public bool Motion,Glass,CityMotion,QuietMode,TrayEnabled,AutoServer,AutoStrategy;
    public string Server="";
    public List<ModernServer> Servers=[];
    public List<RouteRule> Rules=[];
}
public partial class MainWindow : Window
{
    readonly Workspace backend;
    readonly ModernUiState state=new();
    readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly HashSet<string> pendingRules=[];
    readonly Dictionary<string,string> latencyResults=[];
    readonly CancellationTokenSource lifetime=new();
    bool initialized,loadingUi,refreshingRoutes,leaving,vpn,zapret,vpnBusy,zapretBusy;
    int currentPage,lastProcessId;
    string profileFingerprint="",ruleFingerprint="",selectedId="",lastMessage="";
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr h,int a,ref int v,int size);
    public MainWindow()
    {
        InitializeComponent();Title="RelayDesk "+AppUpdates.CurrentVersion;
        Width=Math.Min(1320,SystemParameters.WorkArea.Width-32);Height=Math.Min(920,SystemParameters.WorkArea.Height-32);
        MinWidth=Math.Min(1040,Width);MinHeight=Math.Min(740,Height);
        backend=new Workspace();backend.ModernPrepareData();BackendHost.Content=backend;
        backend.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/Theme.xaml")});
        foreach(var pair in new[]{("Page","#101318"),("Surface","#1B1E25"),("Ink","#ECEEF2"),("Muted","#9DA3AF"),("Line","#3D4350"),("Accent","#D2DAE6"),("Selected","#303641"),("Action","#444C59")})backend.Resources[pair.Item1]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Item2));
        if(backend.ThemeChoice.Parent is FrameworkElement themePanel)themePanel.Visibility=Visibility.Collapsed;
        var p=backend.ModernStore.Settings;
        state.Motion=p.InterfaceMotion;state.Glass=p.GraphiteGlass;state.CityMotion=p.AsciiBackground;state.QuietMode=p.QuietMode;state.TrayEnabled=p.TrayEnabled;
        state.AutoServer=p.AutoServer;state.AutoStrategy=p.AutoZapret;
        AutoServer.IsChecked=p.AutoServer;AutoStrategy.IsChecked=p.AutoZapret;MotionChoice.IsChecked=state.Motion;GlassChoice.IsChecked=state.Glass;CityMotionChoice.IsChecked=state.CityMotion;
        ScopeChoice.ItemsSource=new[]{"Discord и правила zapret","Все подходящие подключения"};ScopeChoice.SelectedIndex=p.ZapretDiscordOnly?0:1;
        RulesFilter.ItemsSource=new[]{"Все правила","Приложения","Домены","IP / подсети"};RulesFilter.SelectedIndex=0;
        SizeChanged+=(_,_)=>WindowClip.Rect=new Rect(0,0,ActualWidth,ActualHeight);
        InitializeQuietMode();App.Motion=state.Motion&&SystemParameters.ClientAreaAnimation;
        initialized=true;RefreshFromBackend();SelectPage(0);
        refresh.Tick+=(_,_)=>RefreshFromBackend();
        Closed+=(_,_)=>{leaving=true;refresh.Stop();lifetime.Cancel();};
    }
    void Ready(object sender,RoutedEventArgs e)
    {
        ApplyGlass();UpdateCityMotion();BrandTitle.AnimationEnabled=App.Motion;InitializeTray();refresh.Start();FadeIn(Shell,0,180);
    }
    void RefreshFromBackend()
    {
        if(leaving)return;
        var store=backend.ModernStore;var session=backend.ModernSession;
        vpn=session.ProxyRunning;zapret=session.ZapretRunning;vpnBusy=zapretBusy=backend.ModernBusy;
        VpnPower.IsEnabled=VpnActionButton.IsEnabled=!vpnBusy;ZapretPower.IsEnabled=ZapretActionButton.IsEnabled=!zapretBusy;
        VpnAction.Text=vpn?"Выключить":"Включить";ZapretAction.Text=zapret?"Выключить":"Включить";
        VpnStatus.Text=vpnBusy?"Выполняется операция…":vpn?"Ядро "+session.ActiveProfile?.Core+" запущено":"Выключен";
        ZapretStatus.Text=zapretBusy?"Выполняется операция…":zapret?"winws запущен":"Выключен";
        VpnRing.Stroke=new SolidColorBrush(vpn?Color.FromRgb(220,230,244):Color.FromRgb(125,135,151));ZapretRing.Stroke=new SolidColorBrush(zapret?Color.FromRgb(220,230,244):Color.FromRgb(125,135,151));
        HttpsStatus.Text="HTTPS: "+backend.ModernHttps;Latency.Text=backend.ModernTcp;
        Strategy.Text=string.IsNullOrWhiteSpace(store.Settings.ZapretArgs)?"Стратегия не выбрана":store.Settings.AutoZapret?"Автоподбор включён":"Стратегия из настроек";
        var fingerprint=string.Join('|',store.Profiles.Select(p=>p.Id+":"+p.Name+":"+p.SubscriptionGroup));
        bool profilesChanged=fingerprint!=profileFingerprint;
        if(profilesChanged)
        {
            profileFingerprint=fingerprint;state.Servers=store.Profiles.Where(p=>p.Core!="zapret").Select(p=>new ModernServer{ProfileId=p.Id,Name=p.Name+(store.Profiles.Count(s=>s.Name==p.Name)>1?" ["+p.Id[..6]+"]":"")}).ToList();
            loadingUi=true;ServerChoice.ItemsSource=new[]{"Автоматически"}.Concat(state.Servers.Select(s=>s.Name)).ToArray();RefreshGroups();loadingUi=false;RefreshProfiles();
        }
        var selected=backend.ModernProfile;string id=selected?.Id??"";
        string rules=selected==null?"":id+"|"+System.Text.Json.JsonSerializer.Serialize(selected.QuickRoutes);
        if(!rulesDialogOpen&&rules!=ruleFingerprint){selectedId=id;LoadProfileRules();}
        loadingUi=true;
        state.AutoServer=store.Settings.AutoServer;state.AutoStrategy=store.Settings.AutoZapret;
        AutoServer.IsChecked=state.AutoServer;AutoStrategy.IsChecked=state.AutoStrategy;
        state.Server=state.AutoServer?"Автоматически":state.Servers.FirstOrDefault(s=>s.ProfileId==id)?.Name??"";
        if(ServerChoice.SelectedItem?.ToString()!=state.Server)ServerChoice.SelectedItem=state.Server;
        loadingUi=false;
        ServerHint.Text=vpn?"Работает: "+session.ActiveProfile?.Name:state.AutoServer?"Автовыбор при подключении":selected?.Core??"Импортируй профиль";
        int pid=session.ProxyRunning?session.Proxy!.Id:0;if(pid!=lastProcessId){lastProcessId=pid;if(session.ActiveProfile!=null)pendingRules.Remove(session.ActiveProfile.Id);}
        if(backend.ModernMessage.Length>0&&backend.ModernMessage!=lastMessage){lastMessage=backend.ModernMessage;Toast(ProgramJournal.Clean(lastMessage));}
        if(backend.ModernHasUpdate){Notice.Text="Доступна версия "+backend.ModernUpdateVersion+". Обновление готовится автоматически; подробности в расширенных настройках.";}
        if(JournalPanel.Visibility==Visibility.Visible)Journal.Text=backend.ModernLog;
        if(currentPage==4)RefreshConnections();
        UpdateConnectionClocks();RefreshTrayStatus();
    }
    void LoadProfileRules()
    {
        var profile=backend.ModernProfile;state.Rules.Clear();deletedRule=null;UndoRuleButton.Visibility=Visibility.Collapsed;
        if(profile!=null)
        {
            int index=0;
            foreach(var q in profile.QuickRoutes)
            {
                string id=q.Id.Length>0?q.Id:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(q.Kind+"|"+q.Value+"|"+index++)))[..24];
                state.Rules.Add(new(){Id=id,Kind=q.Kind,Value=q.Value,Name=q.Name.Length>0?q.Name:q.Kind=="process_path"?Path.GetFileName(q.Value):q.Value,Route=q.Zapret?"Через zapret":q.Direct?"Напрямую":"Через VPN",OutboundTag=q.Outbound});
            }
            ruleFingerprint=profile.Id+"|"+System.Text.Json.JsonSerializer.Serialize(profile.QuickRoutes);
        }
        else ruleFingerprint="";
        state.Rules.Add(new(){Id="source-default",Kind="default",Name="Исходный маршрут профиля",Route="Исходный профиль"});RefreshRoutes();
    }
    bool PersistRules()
    {
        try
        {
            var profile=backend.ModernProfile??throw new InvalidOperationException("Выбери профиль.");
            string proxy=ProxyTag(profile);
            var routes=state.Rules.Where(r=>r.Kind!="default").Select(r=>new QuickRoute(r.Kind,r.Value,r.Route=="Через zapret"?"@zapret":r.Route=="Напрямую"?"@direct":r.OutboundTag.Length>0&&!r.OutboundTag.StartsWith('@')?r.OutboundTag:proxy,r.Route=="Напрямую",r.Route=="Через zapret",r.Name,r.Id)).ToArray();
            backend.ModernSaveRoutes(routes);ruleFingerprint=profile.Id+"|"+System.Text.Json.JsonSerializer.Serialize(backend.ModernProfile!.QuickRoutes);pendingRules.Add(profile.Id);RefreshRoutes();return true;
        }
        catch(Exception ex){LoadProfileRules();Toast(ProgramJournal.Clean(ex.Message));return false;}
    }
    static string ProxyTag(Profile profile)
    {
        if(profile.Core is not ("sing-box" or "Xray"))return "proxy";
        var root=JsonTools.Object(profile.Config);var outbounds=root["outbounds"] as System.Text.Json.Nodes.JsonArray??[];
        return outbounds.FirstOrDefault(o=>JsonTools.Text(o?[profile.Core=="Xray"?"protocol":"type"]) is not ("direct" or "freedom" or "block" or "blackhole" or "dns")&&JsonTools.Text(o?["tag"]).Length>0)?["tag"]?.ToString()??"proxy";
    }
    void Navigate(object sender,RoutedEventArgs e){if(sender is Button b&&int.TryParse(b.Tag?.ToString(),out int i))SelectPage(i);}
    void SelectPage(int index)
    {
        currentPage=index;var pages=new[]{HomePage,ProfilesPage,AppsPage,SettingsPage,ConnectionsPage};for(int i=0;i<pages.Length;i++)pages[i].Visibility=i==index?Visibility.Visible:Visibility.Collapsed;
        foreach(var b in Navigation.Children.OfType<Button>()){bool active=b.Tag?.ToString()==index.ToString();b.Background=new SolidColorBrush(active?Color.FromArgb(22,230,235,245):Colors.Transparent);b.BorderBrush=new SolidColorBrush(active?Color.FromArgb(35,230,235,245):Colors.Transparent);}
        backend.ModernConnectionsVisible=index==4;Scroller.ScrollToTop();FadeIn(PageHost);if(index==1)RefreshProfiles();if(index==4)RefreshConnections();
    }
    void ServerChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized||loadingUi)return;
        try
        {
            string value=ServerChoice.SelectedItem?.ToString()??"";
            if(value=="Автоматически")backend.ModernAuto(true,backend.ModernStore.Settings.AutoZapret);
            else{var item=state.Servers.FirstOrDefault(s=>s.Name==value);if(item!=null){backend.ModernAuto(false,backend.ModernStore.Settings.AutoZapret);backend.ModernSelect(backend.ModernStore.Profiles.Single(p=>p.Id==item.ProfileId));}}
            RefreshFromBackend();RefreshProfiles();
        }
        catch(Exception ex){Toast(ex.Message);}
    }
    void ScopeChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized||loadingUi)return;try{backend.ModernScope(ScopeChoice.SelectedIndex==0);}catch(Exception ex){loadingUi=true;ScopeChoice.SelectedIndex=backend.ModernStore.Settings.ZapretDiscordOnly?0:1;loadingUi=false;Toast(ex.Message);}
    }
    void ToggleVpn(object sender,RoutedEventArgs e){if(!backend.ModernBusy)backend.ModernToggleVpn();}
    void ToggleZapret(object sender,RoutedEventArgs e){if(!backend.ModernBusy)backend.ModernToggleZapret();}
    void CheckTcp(object sender,RoutedEventArgs e)=>backend.ModernTcpCheck();
    void CheckHttps(object sender,RoutedEventArgs e)=>backend.ModernHttpsCheck();
    void RefreshGroups()
    {
        string current=GroupChoice.SelectedItem?.ToString()??"Все группы";GroupChoice.ItemsSource=new[]{"Все группы","Без группы"}.Concat(backend.ModernStore.Settings.SubscriptionGroups.Concat(backend.ModernStore.Profiles.Select(p=>p.SubscriptionGroup)).Where(s=>s.Length>0).Distinct().Order()).ToArray();GroupChoice.SelectedItem=GroupChoice.Items.Contains(current)?current:"Все группы";
    }
    void GroupChanged(object sender,SelectionChangedEventArgs e){if(initialized&&!loadingUi)RefreshProfiles();}
    void RefreshProfiles()
    {
        if(ProfileRows==null)return;ProfileRows.Children.Clear();string group=GroupChoice.SelectedItem?.ToString()??"Все группы";
        foreach(var profile in backend.ModernStore.Profiles.Where(p=>group=="Все группы"||group=="Без группы"&&p.SubscriptionGroup.Length==0||p.SubscriptionGroup==group))
        {
            var row=new Grid{Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(160)});row.ColumnDefinitions.Add(new(){Width=new GridLength(110)});
            var text=new StackPanel{Margin=new Thickness(0,0,12,0)};text.Children.Add(new TextBlock{Text=profile.Name,FontSize=16});text.Children.Add(new TextBlock{Text=profile.Core+" · "+(profile.SubscriptionGroup.Length>0?profile.SubscriptionGroup:"Без группы")+" · TCP: "+latencyResults.GetValueOrDefault(profile.Id,"не проверен"),FontSize=12,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,5,0,0)});row.Children.Add(text);
            var change=new Button{Content="Настроить",Margin=new Thickness(0,0,10,0)};change.Click+=(_,_)=>{if(backend.ModernSelect(profile))ShowAdvanced(1);};Grid.SetColumn(change,1);row.Children.Add(change);
            var pick=new Button{Content=backend.ModernProfile?.Id==profile.Id?"Выбран":"Выбрать"};pick.Click+=(_,_)=>{try{backend.ModernAuto(false,backend.ModernStore.Settings.AutoZapret);backend.ModernSelect(profile);RefreshFromBackend();RefreshProfiles();}catch(Exception ex){Toast(ex.Message);}};Grid.SetColumn(pick,2);row.Children.Add(pick);ProfileRows.Children.Add(row);
        }
    }
    async void ProbeServers(object sender,RoutedEventArgs e)
    {
        var button=(Button)sender;button.IsEnabled=false;
        try{latencyResults.Clear();await ServerLatencyProbe.Run(backend.ModernStore.Profiles.Where(p=>p.Core!="zapret"),r=>Dispatcher.Invoke(()=>{foreach(var p in backend.ModernStore.Profiles.Where(p=>p.Name==r.Profile))latencyResults[p.Id]=r.Result;RefreshProfiles();}),lifetime.Token);Toast("TCP проверен по текущим маршрутам Windows. HTTPS проверяется отдельно.");}catch(Exception ex){Toast(ex.Message);}finally{button.IsEnabled=true;}
    }
    void AddGroup(object sender,RoutedEventArgs e)
    {
        string name=GroupName.Text.Trim();if(name.Length is <1 or >40||name is "Все группы" or "Без группы"){Toast("Укажи название группы от 1 до 40 символов.");return;}
        if(!backend.ModernStore.Settings.SubscriptionGroups.Contains(name,StringComparer.OrdinalIgnoreCase))backend.ModernStore.Settings.SubscriptionGroups.Add(name);Save();RefreshGroups();GroupChoice.SelectedItem=name;GroupName.Clear();
    }
    void ImportProfiles(object sender,RoutedEventArgs e)=>backend.ModernImport((Button)sender);
    void EditGroups(object sender,RoutedEventArgs e)=>backend.ModernGroups();
    void AddApplication(object sender,RoutedEventArgs e)=>OpenRuleEditor(null,"process_path");
    void SettingsChanged(object sender,RoutedEventArgs e){if(!initialized||loadingUi)return;try{backend.ModernAuto(AutoServer.IsChecked==true,AutoStrategy.IsChecked==true);}catch(Exception ex){Toast(ex.Message);}}
    void MotionChanged(object sender,RoutedEventArgs e){if(!initialized)return;state.Motion=MotionChoice.IsChecked==true;App.Motion=state.Motion&&SystemParameters.ClientAreaAnimation;BrandTitle.AnimationEnabled=App.Motion;Save();}
    void CityMotionChanged(object sender,RoutedEventArgs e){if(!initialized)return;state.CityMotion=CityMotionChoice.IsChecked==true;UpdateCityMotion();Save();}
    void UpdateCityMotion()=>CityBackdrop.AnimationEnabled=state.CityMotion&&SystemParameters.ClientAreaAnimation;
    void GlassChanged(object sender,RoutedEventArgs e){if(!initialized)return;state.Glass=GlassChoice.IsChecked==true;ApplyGlass();Save();}
    void ApplyGlass()
    {
        int dark=1,corner=2;var handle=new WindowInteropHelper(this).Handle;DwmSetWindowAttribute(handle,20,ref dark,4);DwmSetWindowAttribute(handle,33,ref corner,4);
        Background=Brushes.Transparent;bool glass=state.Glass&&!SystemParameters.HighContrast;CityBackdrop.Visibility=glass?Visibility.Visible:Visibility.Collapsed;BackdropShade.Visibility=glass?Visibility.Visible:Visibility.Collapsed;Shell.Background=new SolidColorBrush(glass?Color.FromArgb(18,13,15,20):Color.FromRgb(15,17,23));MaterialLabel.Text=glass?"Белый ASCII · чёрный фон":"Плотный фон";
    }
    static void FadeIn(FrameworkElement element,double offset=0,int duration=180){if(App.Motion)element.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(duration)));}
    void ToggleAdvanced(object sender,RoutedEventArgs e){AdvancedPanel.Visibility=AdvancedPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;}
    void ToggleJournal(object sender,RoutedEventArgs e){JournalPanel.Visibility=JournalPanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;Journal.Text=backend.ModernLog;}
    void OpenSettings(object sender,RoutedEventArgs e)=>SelectPage(3);
    void ShowAdvanced(int tab){WakeQuietMode();StandardContent.Visibility=Visibility.Collapsed;AdvancedHost.Visibility=Visibility.Visible;backend.ModernOpen(tab);}
    void OpenAdvanced(object sender,RoutedEventArgs e)=>ShowAdvanced(1);
    void OpenStartup(object sender,RoutedEventArgs e)=>ShowAdvanced(1);
    void OpenUpdates(object sender,RoutedEventArgs e)=>ShowAdvanced(6);
    void BackToPanel(object sender,RoutedEventArgs e){AdvancedHost.Visibility=Visibility.Collapsed;StandardContent.Visibility=Visibility.Visible;RefreshFromBackend();}
    async void ApplyRules(object sender,RoutedEventArgs e){try{await backend.ModernApplyRoutes();if(backend.ModernProfile!=null)pendingRules.Remove(backend.ModernProfile.Id);Toast("Правила применены. Соединения переподключены.");}catch(Exception ex){Toast(ex.Message);}}
    void Toast(string text){Notice.Text=ProgramJournal.Clean(text);}
    void Save()
    {
        var p=backend.ModernStore.Settings;p.InterfaceMotion=state.Motion;p.AsciiBackground=state.CityMotion;p.GraphiteGlass=state.Glass;p.QuietMode=state.QuietMode;p.TrayEnabled=state.TrayEnabled;
        try{backend.ModernStore.Save();}catch(Exception ex){Toast(ex.Message);}
    }
    void Help(object sender,RoutedEventArgs e)=>MessageBox.Show(this,"RelayDesk "+AppUpdates.CurrentVersion+"\n\nVPN и zapret управляются отдельно. Запуск ядра не доказывает доступность сети: используй проверку HTTPS.\n\nПравила относятся к выбранному профилю. Кнопка применения переподключает его.\n\nРасширенные настройки содержат JSON/YAML, подписки, ядра, диагностику и автозапуск.\n\nДанные: "+backend.ModernStore.Folder,"RelayDesk");
    void Minimize(object sender,RoutedEventArgs e)=>HideToTray();
    void Maximize(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    void ClosePreview(object sender,RoutedEventArgs e)=>Close();
}
