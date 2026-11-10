using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RelayDesk;

static partial class Program
{
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
    static object? Call(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    static void Pump(int ms=80){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    static void Wait(Func<bool> ready){var clock=System.Diagnostics.Stopwatch.StartNew();while(!ready()&&clock.Elapsed<TimeSpan.FromSeconds(12))Pump(100);}
    static IEnumerable<T> Visual<T>(DependencyObject node) where T:DependencyObject
    {
        if(node is T item)yield return item;
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Visual<T>(System.Windows.Media.VisualTreeHelper.GetChild(node,i)))yield return child;
    }
    static Button Click(MainWindow window,string label)
    {
        var button=Visual<Button>(window).First(b=>b.IsVisible&&b.Content?.ToString()==label);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();return button;
    }
    static void Dialog(MainWindow window,string button,Type type)
    {
        bool seen=false;Exception? renderError=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>
        {
            var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w!=window&&type.IsInstanceOfType(w));if(dialog==null)return;
            seen=dialog.Owner==window;
            try
            {
                if(dialog is QrShareWindow)
                {
                    dialog.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(dialog);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine("RelayDesk.ModernChecks","qr-share.png"));encoder.Save(stream);
                    var qrImage=Visual<Image>(dialog).Single();var point=qrImage.TranslatePoint(new Point(),dialog);var crop=new System.Windows.Media.Imaging.CroppedBitmap(bitmap,new Int32Rect((int)point.X,(int)point.Y,(int)qrImage.ActualWidth,(int)qrImage.ActualHeight));
                    var next=Visual<Button>(dialog).First(b=>b.Content?.ToString()=="Назад").TranslatePoint(new Point(),dialog);
                    Check(point.Y+qrImage.ActualHeight<=next.Y&&QrImages.Decode(crop).Length>0,"QR preview remains completely visible and readable inside share dialog");
                }
            }
            catch(Exception ex){renderError=ex;}
            finally{dialog.Close();timer.Stop();}
        };
        timer.Start();try{Click(window,button);}finally{timer.Stop();}
        if(renderError!=null)throw new Exception("QR dialog render failed",renderError);
        Check(seen,button+" opens owned dialog and cancellation preserves modern shell");
    }
    static void QrChecks(MainWindow window,Workspace backend,string folder)
    {
        const string link="vless://00000000-0000-4000-8000-000000000001@vpn.example.org:443?security=none&type=tcp#QR";
        var qr=QrImages.Encode(link);Check(QrImages.Decode(qr)==link,"QR encoder and decoder retain an ordinary VLESS link");
        var qrFile=Path.Combine(folder,"fixture-qr.png");QrImages.Save(qr,qrFile);Check(QrImages.Decode(QrImages.Load(qrFile))==link,"Saved PNG imports through actual image decoder");
        var mutable=new System.Windows.Media.Imaging.WriteableBitmap(qr);var prepared=QrImages.PrepareForDecode(mutable);Check(prepared.IsFrozen&&!mutable.IsFrozen,"Clipboard-like bitmap is detached without changing source ownership");
        Check(Task.Run(()=>QrImages.Decode(prepared)).GetAwaiter().GetResult()==link,"Detached clipboard image decodes on worker thread without WPF affinity errors");
        var profile=SharedRouting.Copy(backend.ModernProfile!);profile.Executable="C:\\private\\core.exe";profile.SubscriptionId="private-subscription-id";
        var share=QrShare.Create(profile,backend.ModernStore.Settings,true);var decoded=QrShare.Decode(share.Encode());
        Check(decoded.Profile!.Config==profile.Config&&decoded.Profile.Executable.Length==0&&decoded.Profile.SubscriptionId.Length==0&&decoded.Profile.Id!=profile.Id&&decoded.Settings!=null,"QR share retains profile config and common settings while omitting engine path and source subscription identity");
        Check(QrShare.Decode(QrShare.Create(profile,backend.ModernStore.Settings,false).Encode()).Settings==null,"Profile sharing does not include common rules unless selected");
        var large=Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4500));var parts=QrParts.Split(large);var collector=new QrParts();string? collected=null;
        foreach(var part in parts.Reverse()){var read=collector.Accept(QrImages.Decode(QrImages.Encode(part)));collected=read.Text;if(read.Read<read.Total)Check(read.Text==null,"Incomplete QR parts cannot produce an import payload");}
        Check(collected==large,"Multiple QR images assemble in reverse order with checksum verification");
        var bad=parts.ToArray();bad[0]=bad[0][..^1]+(bad[0][^1]=='A'?'B':'A');var invalid=new QrParts();bool rejected=false;try{foreach(var part in bad)invalid.Accept(part);}catch(FormatException){rejected=true;}Check(rejected,"Tampered QR packet is rejected before import");
        var exe=Environment.ProcessPath!;var appRule=OverlayDrops.Rule(exe);Check(appRule.Kind=="process_path"&&appRule.Value==exe,"Dropping an executable resolves a process rule without launching it");
        var siteRule=OverlayDrops.Rule("https://example.org/path?test=1");Check(siteRule.Kind=="domain_suffix"&&siteRule.Value=="example.org","Dropped browser link becomes domain rule rather than a path or query rule");
        var shortcutPath=Path.Combine(folder,"fixture.lnk");object? shell=null,shortcut=null;
        try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);shortcut=shell!.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,[shortcutPath]);shortcut!.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,[exe]);shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);}
        finally{if(shortcut!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);if(shell!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        Check(OverlayDrops.Rule(shortcutPath).Value==exe,"Dropped Windows shortcut resolves its EXE without running shortcut arguments");
        var urlShortcut=Path.Combine(folder,"fixture.url");File.WriteAllText(urlShortcut,"[InternetShortcut]\nURL=https://example.org/path\n");Check(OverlayDrops.Rule(urlShortcut).Value=="example.org","Dropped Internet shortcut resolves site domain");
        var profilesBefore=File.ReadAllText(Path.Combine(folder,"profiles.json"));var settingsBefore=File.ReadAllText(Path.Combine(folder,"settings.json"));
        Dialog(window,"Поделиться QR",typeof(QrShareWindow));Check(File.ReadAllText(Path.Combine(folder,"profiles.json"))==profilesBefore&&File.ReadAllText(Path.Combine(folder,"settings.json"))==settingsBefore,"Viewing and closing QR share never modifies profiles or settings");
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};bool preview=false;
        timer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<ThemedWindow>().FirstOrDefault(w=>w.Title=="Предпросмотр QR-пакета");if(dialog!=null){preview=true;dialog.Close();timer.Stop();}};timer.Start();
        try{var task=(Task)Call(window,"AcceptQrText",share.Encode())!;Wait(()=>task.IsCompleted);}finally{timer.Stop();}
        Check(preview&&File.ReadAllText(Path.Combine(folder,"profiles.json"))==profilesBefore&&File.ReadAllText(Path.Combine(folder,"settings.json"))==settingsBefore,"Cancelling QR import preview preserves all profile and settings files");
        var screenImage=new Image{Source=qr,Width=420,Height=420,Margin=new Thickness(20)};
        var qrWindow=new Window{Title="QR screen fixture",Content=screenImage,Width=480,Height=500,Background=System.Windows.Media.Brushes.White,Topmost=true,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        try
        {
            qrWindow.Show();qrWindow.UpdateLayout();Pump(300);var capture=new QrScreenWindow();
            try{var screenshot=(System.Windows.Media.Imaging.BitmapSource)typeof(QrScreenWindow).GetField("screenshot",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(capture)!;var start=screenImage.PointToScreen(new Point());var end=screenImage.PointToScreen(new Point(screenImage.ActualWidth,screenImage.ActualHeight));var bounds=System.Windows.Forms.SystemInformation.VirtualScreen;var crop=new System.Windows.Media.Imaging.CroppedBitmap(screenshot,new Int32Rect((int)start.X-bounds.Left,(int)start.Y-bounds.Top,(int)(end.X-start.X),(int)(end.Y-start.Y)));Check(QrImages.Decode(crop)==link,"QR reader decodes pixels captured from an actual on-screen window");}finally{capture.Close();}
        }
        finally{qrWindow.Close();}
        string? bridgeError=null;var bridge=new OverlayBridge((_,_)=>{},()=>{},()=>{},_=>{},s=>bridgeError=s,()=>{});bridge.Configure(new Preferences());bridge.Place(new Preferences());bridge.Update("IPC QA connection",new TrafficRate(2.5,1.25),"Fixture");bridge.Show();
        int pid=0;bool connected=false;
        try
        {
            Wait(()=>{var child=(System.Diagnostics.Process?)typeof(OverlayBridge).GetField("child",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(bridge);if(child==null)return false;pid=child.Id;connected=bridge.DisplayedName=="IPC QA connection";return connected||bridgeError!=null;});
            Check(connected&&pid>0&&pid!=Environment.ProcessId&&bridgeError==null,"Separate overlay host connects over private local IPC without starting main app: "+bridgeError);
            Pump(400);Check(File.ReadAllText(Path.Combine(folder,"profiles.json"))==profilesBefore&&File.ReadAllText(Path.Combine(folder,"settings.json"))==settingsBefore,"Overlay worker never loads or saves VPN profiles");
        }
        finally{bridge.Close();}
        bool Alive(){try{using var p=System.Diagnostics.Process.GetProcessById(pid);return !p.HasExited;}catch(ArgumentException){return false;}}
        Wait(()=>!Alive());Check(!Alive(),"Closing overlay bridge shuts down only its own worker process");
        var savedConnection=backend.ModernStore.Settings.Connection;var savedRules=backend.ModernStore.Settings.SharedQuickRoutes;var savedFallback=backend.ModernStore.Settings.FallbackRoute;var savedPreset=backend.ModernStore.Settings.RoutingPreset;var oldProfileIds=backend.ModernStore.Profiles.Select(p=>p.Id).ToHashSet();
        backend.ModernAcceptShare(decoded);var added=backend.ModernStore.Profiles.Single(p=>!oldProfileIds.Contains(p.Id));
        Check(added.Config==profile.Config&&added.Executable.Length==0&&!backend.ModernSession.ProxyRunning,"Confirmed QR import creates a new profile without starting its engine");
        Check(Directory.GetDirectories(Path.Combine(folder,"backups"),"before-qr-*").Length>0,"Confirmed QR import preserves a backup before writing settings");
        backend.ModernStore.Profiles.Remove(added);backend.ModernStore.Settings.Connection=savedConnection;backend.ModernStore.Settings.SharedQuickRoutes=savedRules;backend.ModernStore.Settings.FallbackRoute=savedFallback;backend.ModernStore.Settings.RoutingPreset=savedPreset;backend.ModernStore.Save();
    }
    [STAThread]static int Main()
    {
        if(Environment.GetCommandLineArgs().Contains("--ole-source")){try{return OleSource();}catch(Exception ex){File.WriteAllText(Environment.GetCommandLineArgs()[^1],ex.ToString());return 1;}}
        string folder=Path.Combine(Path.GetTempPath(),"RelayDesk-production-QA-"+Guid.NewGuid().ToString("N"));Environment.SetEnvironmentVariable("RELAYDESK_DATA_DIR",folder);Environment.SetEnvironmentVariable("RELAYDESK_INLINE_OVERLAY","1");
        if(Environment.GetCommandLineArgs().Contains("--drops-only"))Environment.SetEnvironmentVariable("RELAYDESK_DROP_QA_ONLY","1");
        if(Environment.GetCommandLineArgs().Contains("--elevated-log")){var output=new StreamWriter(Path.Combine(AppContext.BaseDirectory,"elevated-drop.log")){AutoFlush=true};Console.SetOut(output);Console.SetError(output);}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"qa-start.txt"),"Admin="+EngineSession.IsAdmin()+"; folder="+folder);
        var fixture=new Store(folder);
        var profile=new Profile{Name="Preserved profile",Core="sing-box",Config="{\"log\":{\"level\":\"error\"},\"inbounds\":[{\"type\":\"mixed\",\"listen\":\"127.0.0.1\",\"listen_port\":32199}],\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"},{\"type\":\"socks\",\"tag\":\"proxy\",\"server\":\"127.0.0.1\",\"server_port\":32200}],\"route\":{\"final\":\"proxy\"}}",SubscriptionGroup="Private group",QuickRoutes=[new("domain_suffix","example.org","proxy")]};
        fixture.Profiles.Add(profile);fixture.Settings.LastConnectedProfileId=profile.Id;fixture.Settings.Subscriptions.Add(new(){Url="https://example.com/private-subscription",Group="Private group"});
        fixture.Settings.CheckComponentUpdates=false;fixture.Settings.InterfaceMotion=false;fixture.Settings.AsciiBackground=false;fixture.Settings.QuietMode=false;fixture.Settings.TrayEnabled=false;
        fixture.Save();string original=File.ReadAllText(Path.Combine(folder,"profiles.json"));string settings=File.ReadAllText(Path.Combine(folder,"settings.json"));
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/Theme.xaml")});
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        MainWindow? window=null;
        try
        {
            window=new MainWindow();window.Show();Pump(250);
            var backend=(Workspace)typeof(MainWindow).GetField("backend",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(Environment.GetEnvironmentVariable("RELAYDESK_DROP_QA_ONLY")=="1"){WindowDropChecks(window,backend,folder);window.Close();Console.WriteLine($"COMPLETE: {count} window drop checks passed.");return 0;}
            Check(backend.ModernProfile?.Id==profile.Id,"Modern shell restores the existing selected profile");
            Check(backend.ModernStore.Profiles.Single().Config==profile.Config&&backend.ModernStore.Settings.Subscriptions.Single().Url==fixture.Settings.Subscriptions.Single().Url,"Production profiles and private subscriptions survive UI migration");
            var backups=Directory.GetDirectories(Path.Combine(folder,"backups"));Check(backups.Length==1&&File.ReadAllText(Path.Combine(backups[0],"profiles.json"))==original&&File.ReadAllText(Path.Combine(backups[0],"settings.json"))==settings,"Migration backs up original files byte-for-byte before saving");
            Check(!backend.ModernSession.ProxyRunning&&!backend.ModernSession.ZapretRunning,"Opening modern shell does not fabricate or start a connection");
            Call(window,"Maximize",window,new RoutedEventArgs());Pump();Check(window.WindowState==WindowState.Maximized,"Caption maximize button expands owned window");Call(window,"Maximize",window,new RoutedEventArgs());Pump();Check(window.WindowState==WindowState.Normal,"Caption restore button restores owned window");
            Call(window,"Minimize",window,new RoutedEventArgs());Pump();Check(window.WindowState==WindowState.Minimized,"Caption minimize button minimizes when tray is disabled");window.WindowState=WindowState.Normal;window.Activate();Pump();
            Call(window,"SelectPage",4);Pump();Check(((Panel)window.FindName("ConnectionRows")).Children.OfType<Button>().Count()==0,"Connections screen contains no demo rows");
            var rule=new RouteRule{Kind="domain",Value="api.example.org",Name="My API",Route="Напрямую"};
            var previousProfile=backend.ModernProfile!;int previousRuleCount=previousProfile.QuickRoutes.Count;
            Call(window,"CommitRule",rule,null,false);
            Check(!ReferenceEquals(previousProfile,backend.ModernProfile)&&previousProfile.QuickRoutes.Count==previousRuleCount,"Saving routes creates a new selected profile and preserves the active-session snapshot");
            var persisted=new Store(folder).Profiles.Single();Check(persisted.QuickRoutes.Any(r=>r.Value=="api.example.org"&&r.Direct&&r.Name=="My API")&&persisted.Config==profile.Config,"Modern rule saves to the actual profile while preserving original JSON");
            Check(RuleMatching.Overlaps(rule,[new(){Kind="domain_suffix",Value="example.org",Route="Через VPN"}]).Count==1,"Modern rule conflict analyzer detects suffix overlap");
            var protectedConfig=persisted.Config;
            try{backend.ModernSaveRoutes([new("unknown","Discord.exe","proxy")]);throw new Exception("Invalid rule accepted");}catch(FormatException){}
            Check(new Store(folder).Profiles.Single().Config==protectedConfig&&new Store(folder).Profiles.Single().QuickRoutes.Count==persisted.QuickRoutes.Count,"Invalid shared rule leaves saved profile and routes unchanged");
            Call(window,"ShowAdvanced",2);Pump();Check(((UIElement)window.FindName("AdvancedHost")).Visibility==Visibility.Visible,"Original JSON editor and advanced controls remain available");
            Call(window,"BackToPanel",window,new RoutedEventArgs());
            Call(window,"SelectPage",1);Pump();Click(window,"Настроить");Check(((UIElement)window.FindName("AdvancedHost")).Visibility==Visibility.Visible&&((UIElement)window.FindName("StandardContent")).Visibility==Visibility.Visible,"Profile Configure button preserves modern navigation");
            Click(window,"Вернуться к панели");Call(window,"SelectPage",1);Pump();Dialog(window,"Управлять группами",typeof(SubscriptionGroupsWindow));
            Click(window,"Добавить профиль / подписку");
            var menu=PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>().Where(s=>s.RootVisual!=null).SelectMany(s=>Visual<ContextMenu>(s.RootVisual)).FirstOrDefault(m=>m.IsOpen);
            Check(menu!=null&&menu.Items.OfType<MenuItem>().Select(i=>i.Header?.ToString()).SequenceEqual(new[]{"Вставить из буфера","Импортировать файл","Добавить подписку","Создать пустой профиль"}),"Add profile button opens all four real import actions");menu!.IsOpen=false;
            var latency=Click(window,"Проверить задержку");Wait(()=>latency.IsEnabled);Check(latency.IsEnabled&&backend.ModernProfile!=null,"Latency action completes against loopback fixture without changing selected profile");
            ((TextBox)window.FindName("GroupName")).Text="QA group";Click(window,"Добавить группу");Check(new Store(folder).Settings.SubscriptionGroups.Contains("QA group"),"Add group button persists a real group");
            Call(window,"SelectPage",2);Pump();Dialog(window,"Добавить приложение…",typeof(RuleEditorWindow));Dialog(window,"Добавить сайт / IP…",typeof(RuleEditorWindow));Dialog(window,"Изменить",typeof(RuleEditorWindow));
            int beforeDelete=backend.ModernProfile!.QuickRoutes.Count;Click(window,"Удалить");Check(backend.ModernProfile.QuickRoutes.Count==beforeDelete-1,"Delete rule button persists deletion");Click(window,"Отменить удаление");Check(backend.ModernProfile.QuickRoutes.Count==beforeDelete,"Undo rule button restores persisted rule");
            Click(window,"Применить правила");Check(((TextBlock)window.FindName("Notice")).Text.Contains("не запущен"),"Apply rules on inactive profile reports the actual state");
            Call(window,"SelectPage",0);Pump();Click(window,"Журнал");Check(((UIElement)window.FindName("JournalPanel")).Visibility==Visibility.Visible,"Journal button opens actual log");Click(window,"Журнал");Check(((UIElement)window.FindName("JournalPanel")).Visibility==Visibility.Collapsed,"Journal button closes log");
            Check(window.FindName("VpnActionButton")==null&&window.FindName("ZapretActionButton")==null,"Dashboard exposes only one power action per component");
            foreach(string name in new[]{"VpnPower","ZapretPower"})
            {
                ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(300);
                Check(!backend.ModernSession.ProxyRunning&&!backend.ModernSession.ZapretRunning&&backend.ModernMessage.Length>0,name+" validates missing engine without starting network processes");
            }
            Click(window,"Проверить TCP");Wait(()=>backend.ModernTcp!="Проверка…");Check(backend.ModernTcp!="Проверка…","TCP button completes using loopback fixture");
            Click(window,"Проверить HTTPS");Pump();Check(backend.ModernMessage.Contains("запусти"),"HTTPS button requires actually running profile");
            Click(window,"Настройки");Click(window,"Расширенные настройки  ›");Check(((UIElement)window.FindName("AdvancedPanel")).Visibility==Visibility.Visible,"Advanced settings expander opens");Click(window,"TUN, DNS, JSON, ядра и автоматика…");Check(((TabControl)backend.FindName("Tabs")).SelectedIndex==1,"Advanced settings button opens profile section inside modern shell");Click(window,"Вернуться к панели");
            Call(window,"SelectPage",0);Call(window,"ToggleJournal",window,new RoutedEventArgs());
            var journal=(TextBox)window.FindName("Journal");journal.Text=string.Join("\n",Enumerable.Repeat(new string('x',300),10000));window.UpdateLayout();
            var journalScroll=(ScrollViewer)journal.Parent;
            Check(journalScroll.ActualHeight<=261&&journalScroll.ScrollableHeight>0,"Large journal is height-bounded and scrollable without shrinking dashboard");
            Call(window,"ToggleJournal",window,new RoutedEventArgs());
            backend.ModernStore.Settings.StrategyList="QA strategy\n--dpi-desync=multisplit";
            Call(window,"LoadZapretStrategies");
            var strategyChoice=(ComboBox)window.FindName("ZapretStrategyChoice");
            strategyChoice.SelectedItem=strategyChoice.Items.Cast<Strategy>().Single(s=>s.Name=="QA strategy");
            Call(window,"SaveZapretStrategy",window,new RoutedEventArgs());
            var strategySaved=new Store(folder).Settings;
            Check(strategySaved.ZapretArgs=="--dpi-desync=multisplit"&&!strategySaved.AutoZapret,"Manual strategy selector persists arguments and disables automatic replacement");
            Check(!backend.ModernSession.ProxyRunning&&!backend.ModernSession.ZapretRunning,"Choosing strategy never starts or interrupts cores");
            Call(window,"SelectPage",3);Pump();
            var settingsTexts=Visual<TextBlock>(window).Where(t=>t.IsVisible).Select(t=>t.Text).ToArray();
            Check(settingsTexts.Contains("Стратегия zapret")&&!settingsTexts.Any(t=>t.Contains("???")||t.Contains('\uFFFD')),"Rendered settings keep Russian labels without encoding loss: "+string.Join(" | ",settingsTexts.Where(t=>t.Contains("???")||t.Contains('\uFFFD')||t.Contains("zapret"))));
            Check(Visual<Button>(window).Any(b=>b.Content?.ToString()=="Применить стратегию"),"Strategy action has readable Russian caption");
            Check(((TextBlock)window.FindName("ZapretStrategyMessage")).Text.StartsWith("Сохранена: QA strategy"),"Saved strategy status keeps Russian text");
            Call(window,"OpenStartup",window,new RoutedEventArgs());Pump();
            Check(((UIElement)window.FindName("StartupPanel")).Visibility==Visibility.Visible&&((UIElement)window.FindName("AdvancedHost")).Visibility==Visibility.Collapsed&&((UIElement)window.FindName("StandardContent")).Visibility==Visibility.Visible,"Windows startup opens inside modern settings without legacy navigation");
            ((CheckBox)window.FindName("StartupEnabled")).IsChecked=false;((CheckBox)window.FindName("StartupVpn")).IsChecked=true;((CheckBox)window.FindName("StartupZapret")).IsChecked=true;
            Call(window,"SaveModernStartup",window,new RoutedEventArgs());
            var startupSaved=new Store(folder).Settings;
            Check(!startupSaved.WindowsStartup&&startupSaved.StartupVpn&&startupSaved.StartupZapret&&startupSaved.StartupProfileId==profile.Id,"Modern startup saves independent component choices and profile without changing real Windows task in QA");
            ((CheckBox)window.FindName("StartupEnabled")).IsChecked=true;Call(window,"SaveModernStartup",window,new RoutedEventArgs());
            Check(!new Store(folder).Settings.WindowsStartup&&((TextBlock)window.FindName("StartupMessage")).Text.Contains("ядро"),"Startup validation reports missing engine and leaves persisted settings unchanged");
            foreach(int tab in new[]{1,2,3,4,5,6,8})
            {
                Call(window,"ShowAdvanced",tab);Pump();
                Check(((UIElement)window.FindName("StandardContent")).Visibility==Visibility.Visible&&!((FrameworkElement)backend.FindName("Navigation")).IsVisible&&((TabControl)backend.FindName("Tabs")).SelectedIndex==tab,"Parameter section "+tab+" keeps modern shell and hides legacy sidebar");
                Call(window,"SelectPage",0);Pump();Check(((UIElement)window.FindName("AdvancedHost")).Visibility==Visibility.Collapsed,"Main navigation exits parameter section "+tab);
            }
            Call(window,"OpenUpdates",window,new RoutedEventArgs());Pump();Check(((TabControl)backend.FindName("Tabs")).SelectedIndex==6&&((UIElement)window.FindName("StandardContent")).Visibility==Visibility.Visible,"Updates button opens modern parameter host");
            ((TabControl)backend.FindName("Tabs")).SelectedIndex=2;Pump();Check(((ComboBox)window.FindName("AdvancedSection")).SelectedIndex==1,"Internal parameter navigation synchronizes modern section selector");
            ((TabControl)backend.FindName("Tabs")).SelectedIndex=0;Pump();Check(((UIElement)window.FindName("AdvancedHost")).Visibility==Visibility.Collapsed&&((UIElement)window.FindName("HomePage")).Visibility==Visibility.Visible,"Legacy return-to-dashboard action resolves to modern home page");
            Call(window,"BackToPanel",window,new RoutedEventArgs());
            ((DispatcherTimer)typeof(Workspace).GetField("connectionTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(backend)!).Stop();
            File.WriteAllText(Path.Combine(folder,"Discord.exe"),"routing test fixture - never executed");File.WriteAllText(Path.Combine(folder,"Browser.exe"),"routing test fixture - never executed");
            var connectionFixture=new List<ConnectionRow>
            {
                new("voice","Discord.exe","voice.example.org:19326","UDP","Через VPN / прокси","proxy","voice-rule","",""){ProcessPath=Path.Combine(folder,"Discord.exe"),Host="voice.example.org",Address="192.0.2.1"},
                new("api","Discord.exe","api.example.org:443","TCP","Напрямую","direct","api-rule","",""){ProcessPath=Path.Combine(folder,"Discord.exe"),Host="api.example.org",Address="192.0.2.2"},
                new("browser","Browser.exe","www.example.net:443","TCP","Через VPN / прокси","proxy","final","",""){ProcessPath=Path.Combine(folder,"Browser.exe"),Host="www.example.net",Address="192.0.2.3"}
            };
            typeof(Workspace).GetField("connectionRows",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(backend,connectionFixture);
            Call(window,"SelectPage",4);Pump();
            int RowCount()=>((Panel)window.FindName("ConnectionRows")).Children.OfType<Button>().Count();
            void Filter(string name,string value){((TextBox)window.FindName(name)).Text=value;}
            Check(RowCount()==3,"Connection table renders all actual fixture rows");
            Check(!((FrameworkElement)window.FindName("ConnectionAppFilter")).IsVisible&&((UIElement)window.FindName("ConnectionBatchToolbar")).Visibility==Visibility.Collapsed,"Compact table hides filters and batch toolbar by default");
            var filterHeader=((Grid)window.FindName("ConnectionColumnHeaders")).Children.OfType<Button>().First(b=>b.Tag.ToString()=="0");
            filterHeader.ContextMenu!.IsOpen=true;Pump();
            var filterInput=(TextBox)((StackPanel)((MenuItem)filterHeader.ContextMenu.Items[0]).Header).Children[1];
            filterInput.SetCurrentValue(TextBox.TextProperty,"Discord");Pump();Check(RowCount()==2,"Header context filter edits actual column filter through two-way binding");
            filterHeader.ContextMenu.IsOpen=false;Pump();Check(((UIElement)window.FindName("ConnectionFilterToolbar")).Visibility==Visibility.Visible,"Active column filter is summarized after context menu closes");Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());
            foreach(var test in new[]{("ConnectionAppFilter","Discord",2),("ConnectionDestinationFilter","voice",1),("ConnectionTypeFilter","UDP",1),("ConnectionRouteFilter","Напрямую",1),("ConnectionRuleFilter","api-rule",1)})
            {
                Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());Filter(test.Item1,test.Item2);Check(RowCount()==test.Item3,"Independent column filter: "+test.Item1);
            }
            Filter("ConnectionAppFilter","Browser");Check(RowCount()==0,"Column filters combine using AND");
            Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());Filter("ConnectionSearch","voice-rule");Check(RowCount()==1,"Global search also matches reported rule");
            Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());
            var sort=((Grid)window.FindName("ConnectionColumnHeaders")).Children.OfType<Button>().First(b=>b.Tag.ToString()=="0");
            Call(window,"SelectAllConnections");Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 3","Select all marks every active visible connection by stable ID");
            Filter("ConnectionAppFilter","Discord");Call(window,"SelectAllConnections");Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 2","Select all respects current column filters");
            Call(window,"ClearConnectionSelection",window,new RoutedEventArgs());Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());
            sort.Focus();ApplicationCommands.SelectAll.Execute(null,sort);Pump();
            Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 3","Routed SelectAll command reaches table when a row or header has focus");
            Call(window,"ClearConnectionSelection",window,new RoutedEventArgs());
            var searchInput=(TextBox)window.FindName("ConnectionSearch");searchInput.Text="Discord";searchInput.Focus();searchInput.CaretIndex=2;ApplicationCommands.SelectAll.Execute(null,searchInput);Pump();
            Check(searchInput.SelectedText=="Discord"&&((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 0","SelectAll in search selects text without selecting connections");
            Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());
            sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Call(window,"RefreshConnections");
            string FirstRow()=>((Panel)window.FindName("ConnectionRows")).Children.OfType<Button>().First().Tag.ToString()!;
            Check(FirstRow()=="browser","Ascending column sorting survives refresh");sort.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Call(window,"RefreshConnections");Check(FirstRow()!="browser","Descending column sorting survives refresh");
            var connections=((IEnumerable<DemoConnection>)Call(window,"CoreConnections")!).ToArray();
            Call(window,"SelectConnection",connections[0],true);Call(window,"SelectConnection",connections[1],true);Call(window,"RefreshConnections");
            Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 2","Ctrl selection adds multiple independent connection IDs");
            Check(((UIElement)window.FindName("ConnectionBatchToolbar")).Visibility==Visibility.Visible,"Batch actions appear only after selection");
            ((CheckBox)window.FindName("GlassChoice")).IsChecked=false;window.UpdateLayout();
            var connectionImage=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);connectionImage.Render(window);var connectionPng=new System.Windows.Media.Imaging.PngBitmapEncoder();connectionPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(connectionImage));using(var imageStream=File.Create(Path.Combine(folder,"connections.png")))connectionPng.Save(imageStream);Console.WriteLine("Connection screenshot: "+Path.Combine(folder,"connections.png"));
            connectionFixture.Reverse();Call(window,"RefreshConnections");Filter("ConnectionTypeFilter","UDP");
            Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 2","Selection survives reordering, refresh and filtering");
            Dialog(window,"Настроить выбранные…",typeof(ConnectionBatchWindow));
            Call(window,"SelectConnection",connections[0],true);Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 1","Ctrl selection toggles only clicked row");
            Call(window,"ClearConnectionSelection",window,new RoutedEventArgs());Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 0","Clear selection resets selected IDs");
            var batch=ConnectionBatch.Create(connections,1,"Напрямую",[]);Check(batch.Count==2,"Batch deduplicates repeated application targets");
            var domainBatch=ConnectionBatch.Create(connections,0,"Напрямую",[]);Check(domainBatch.Count==3,"Batch creates individual domain rules");
            var replacement=ConnectionBatch.Create([connections[1]],0,"Через VPN",[domainBatch[1]]);Check(replacement[0].Id==domainBatch[1].Id,"Batch updates matching rule without changing identity");
            try{ConnectionBatch.Create([connections[0] with {CanApplication=false}],1,"Напрямую",[]);throw new Exception("Unsupported application batch accepted");}catch(InvalidOperationException){}
            Check(true,"Unsupported application target rejects complete batch");
            var beforeBatch=File.ReadAllText(Path.Combine(folder,"profiles.json"));Call(window,"SaveConnectionBatch",(object)batch.Select(r=>new RouteRule{Kind="unknown",Value=r.Value,Route=r.Route}).ToArray());Check(File.ReadAllText(Path.Combine(folder,"profiles.json"))==beforeBatch,"Invalid batch leaves all saved profile data unchanged");
            Call(window,"SaveConnectionBatch",domainBatch);var afterBatch=new Store(folder).Profiles.Single();Check(domainBatch.All(r=>afterBatch.QuickRoutes.Any(q=>q.Value==r.Value&&q.Direct)),"Domain batch saves all rules in a single profile update");
            Check(afterBatch.Config==profile.Config,"Batch routing preserves original full JSON");
            Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());Call(window,"SelectConnection",connections[0],true);Call(window,"RefreshConnections");
            connectionFixture.RemoveAll(r=>r.Id==connections[0].Id);Call(window,"RefreshConnections");Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 0","Expired connection removes only its stale selection");
            var trafficConfig=JsonTools.Object("{\"outbounds\":[{\"tag\":\"proxy\",\"type\":\"vless\"},{\"tag\":\"direct\",\"type\":\"direct\"}]}");
            var trafficData=JsonTools.Object("{\"connections\":[{\"id\":\"vpn\",\"chains\":[\"proxy\"],\"download\":1000000,\"upload\":250000},{\"id\":\"direct\",\"chains\":[\"direct\"],\"download\":9000000,\"upload\":9000000},{\"id\":\"unknown\",\"chains\":[\"missing\"],\"download\":9000000}]}" );
            var trafficCounters=VpnTrafficReader.ParseConnections(trafficData,trafficConfig);
            Check(trafficCounters.Count==1&&trafficCounters["vpn"]==new TrafficBytes(1000000,250000),"Overlay excludes direct and unclassified connections and retains both directions");
            var meter=new TrafficMeter();Check(meter.Sample(trafficCounters,1)==null,"First overlay sample establishes baseline without historical speed spike");
            var rate=meter.Sample(new(){["vpn"]=new(2000000,500000)},2)!.Value;
            Check(rate.Download==8&&rate.Upload==2,"Overlay converts bytes per second into decimal megabits per second in correct direction");
            Check(meter.Sample(new(){["vpn"]=new(100,100)},3)==new TrafficRate(0,0),"Counter reset does not produce negative traffic");
            meter.Reset();Check(meter.Sample(new(){["new-core"]=new(90000000,1000)},4)==null,"Core restart resets overlay baseline");
            var xrayMetrics=JsonTools.Object(VpnTrafficReader.ConfigureXray("{\"outbounds\":[{\"tag\":\"proxy\",\"protocol\":\"vless\"},{\"tag\":\"direct\",\"protocol\":\"freedom\"}]}",54321));
            var xrayCounters=VpnTrafficReader.ParseXray(JsonTools.Object("{\"stats\":{\"outbound\":{\"proxy\":{\"downlink\":1000,\"uplink\":2000},\"direct\":{\"downlink\":900000}}}}"),xrayMetrics);
            Check(xrayCounters.Count==1&&xrayCounters["proxy"]==new TrafficBytes(1000,2000)&&JsonTools.Text(xrayMetrics["metrics"]?["listen"])=="127.0.0.1:54321","Xray loopback metrics retain only VPN outbound counters");
            Call(window,"SelectPage",3);var quietChoice=(CheckBox)window.FindName("QuietChoice");quietChoice.ApplyTemplate();
            Check(quietChoice.Template.FindName("Track",quietChoice) is Border&&quietChoice.Template.FindName("Thumb",quietChoice) is System.Windows.Shapes.Ellipse,"Quiet mode uses a toggle track and thumb instead of checkbox glyph");
            var overlayChoice=(CheckBox)window.FindName("OverlayChoice");overlayChoice.IsChecked=true;Pump(450);
            var overlay=app.Windows.OfType<TrafficOverlayWindow>().Single();
            Check(overlay.IsVisible&&overlay.Topmost&&!overlay.ShowInTaskbar&&!overlay.ShowActivated&&overlay.Owner==null,"Overlay appears above windows without taskbar entry or coupling to main minimization");
            ((Slider)window.FindName("OverlayOpacity")).Value=65;((Slider)window.FindName("OverlaySize")).Value=18;((ComboBox)window.FindName("OverlayCorner")).SelectedIndex=2;Pump(500);
            var overlayPrefs=new Store(folder).Settings;
            Check(overlay.Opacity==.65&&overlayPrefs.TrafficOverlay&&overlayPrefs.OverlayTextSize==18&&overlayPrefs.OverlayCorner=="top-right","Overlay transparency, size, position and enabled state persist from settings");
            window.WindowState=WindowState.Minimized;Pump();Check(overlay.IsVisible,"Overlay remains visible while main window is minimized");window.WindowState=WindowState.Normal;window.Activate();Pump();
            overlay.Update("QA connection",new TrafficRate(12.34,1.25),"Synthetic UI fixture");overlay.UpdateLayout();
            var overlayImage=new System.Windows.Media.Imaging.RenderTargetBitmap((int)overlay.ActualWidth,(int)overlay.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);overlayImage.Render(overlay);var overlayPng=new System.Windows.Media.Imaging.PngBitmapEncoder();overlayPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(overlayImage));using(var overlayStream=File.Create(Path.Combine("RelayDesk.ModernChecks","overlay.png")))overlayPng.Save(overlayStream);
            byte[] Pixels(System.Windows.Media.Imaging.RenderTargetBitmap image){var bytes=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(bytes,image.PixelWidth*4,0);return bytes;}
            var plainPixels=Pixels(overlayImage);
            Check(plainPixels[3]==0&&Enumerable.Range(0,plainPixels.Length/4).Count(i=>plainPixels[i*4+3]==0)>plainPixels.Length/8,"Overlay has no painted rectangle or border; empty pixels remain transparent");
            ((CheckBox)window.FindName("OverlaySharks")).IsChecked=true;((Slider)window.FindName("OverlaySharkOpacity")).Value=35;((CheckBox)window.FindName("OverlaySharkMotion")).IsChecked=false;Pump(450);
            var sharkLayer=Visual<AsciiSharksBackdrop>(overlay).Single();
            var sharkPrefs=new Store(folder).Settings;
            Check(sharkPrefs.OverlaySharks&&sharkPrefs.OverlaySharkOpacity==.35&&!sharkPrefs.OverlaySharkMotion&&sharkLayer.TransparentBackground&&!sharkLayer.AnimationEnabled,"ASCII overlay appearance and paused motion persist independently of main background");
            overlay.Update("QA connection",new TrafficRate(12.34,1.25),"Synthetic UI fixture");overlay.UpdateLayout();
            var sharkImage=new System.Windows.Media.Imaging.RenderTargetBitmap((int)overlay.ActualWidth,(int)overlay.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);sharkImage.Render(overlay);var sharkPixels=Pixels(sharkImage);
            Check(sharkPixels[3]==0&&Enumerable.Range(0,sharkPixels.Length/4).Count(i=>sharkPixels[i*4+3]>0)>Enumerable.Range(0,plainPixels.Length/4).Count(i=>plainPixels[i*4+3]>0),"ASCII sharks add visible glyphs without an opaque background");
            var sharkPng=new System.Windows.Media.Imaging.PngBitmapEncoder();sharkPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(sharkImage));using(var sharkStream=File.Create(Path.Combine("RelayDesk.ModernChecks","overlay-sharks.png")))sharkPng.Save(sharkStream);
            overlayChoice.IsChecked=false;Pump(450);Check(!app.Windows.OfType<TrafficOverlayWindow>().Any()&&!new Store(folder).Settings.TrafficOverlay,"Disabling overlay closes its window and persists off state");
            var quietSlider=(Slider)window.FindName("QuietDelay");quietSlider.Value=60;Pump(450);
            Check(new Store(folder).Settings.QuietDelaySeconds==60&&((TextBlock)window.FindName("QuietDelayLabel")).Text=="60 с","Quiet delay slider persists the chosen timeout and displays seconds");
            quietChoice.IsChecked=true;
            void IdleSince(int seconds){foreach(var field in new[]{"lastActivity","awaySince"})typeof(MainWindow).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,DateTime.UtcNow.AddSeconds(-seconds));}
            IdleSince(35);Pump(600);Check(((UIElement)window.FindName("QuietOverlay")).Visibility==Visibility.Collapsed,"Quiet timer waits for selected delay beyond former hardcoded 30 seconds");
            IdleSince(65);Pump(600);Check(((UIElement)window.FindName("QuietOverlay")).Visibility==Visibility.Visible,"Quiet timer fades UI after the selected timeout");Call(window,"WakeQuietMode");quietChoice.IsChecked=false;
            Call(window,"EnterQuietMode");Pump();Check(((UIElement)window.FindName("Shell")).Opacity<.1,"Production quiet mode fades actual HUD");
            var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};window.RaiseEvent(click);Check(click.Handled&&((UIElement)window.FindName("QuietOverlay")).Visibility==Visibility.Collapsed,"First click restores production HUD without clicking through");
            Pump();Call(window,"SelectPage",0);Pump();
            window.UpdateLayout();var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bmp.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine("RelayDesk.ModernChecks","modern.png")))png.Save(stream);
            var imported=ImportParser.Parse("vless://00000000-0000-4000-8000-000000000001@server.example.org:443?security=none&type=tcp#QA").Profile;
            Check(imported.Managed&&imported.SystemProxy&&imported.Dns=="system","New link profiles enable application proxy and system DNS by default");
            var built=JsonTools.Object(JsonTools.Build(imported));
            Check(built["dns"]!["servers"]![0]!["type"]!.ToString()=="local","System DNS emits supported sing-box local resolver");
            imported.SubscriptionId="qa-subscription";imported.SystemProxy=false;imported.Dns="1.1.1.1";
            backend.ModernStore.Profiles.Add(imported);backend.ModernSelect(imported);backend.ModernEnableSubscriptionTraffic();
            Check(backend.ModernProfile!.SystemProxy&&backend.ModernProfile.Dns=="system"&&!imported.SystemProxy,"Existing subscription repair uses new profile snapshot and corrects default DNS");
            backend.ModernProfile.Dns="9.9.9.9";backend.ModernSelect(backend.ModernProfile);backend.ModernEnableSubscriptionTraffic();
            Check(backend.ModernProfile!.Dns=="9.9.9.9","Subscription repair preserves custom DNS");
            var activeSnapshot=backend.ModernProfile;
            backend.ModernSaveRoutes([new("domain_suffix","shared.example.org","proxy"),new("process_name","Example.exe","@direct",true)]);
            var sharedCopy=backend.ModernStore.Profiles.Single(p=>p.Id==profile.Id);
            Check(sharedCopy.QuickRoutes.Count==2&&backend.ModernProfile!.QuickRoutes.Count==2&&activeSnapshot!.QuickRoutes.Count!=2,"Editing subscription updates common rules and all profile mirrors without mutating snapshots");
            var sharedRuntime=JsonTools.Object(RuntimePolicy.Config(sharedCopy,backend.ModernStore.Settings,ConnectionMode.VpnOnly));
            Check(sharedRuntime["inbounds"]!.AsArray().Any(i=>JsonTools.Text(i?["type"])=="tun"&&JsonTools.Text(i?["auto_route"])=="true"),"Shared application rule enables TUN in raw sing-box runtime without editing source JSON");
            var subscriptionRuntime=JsonTools.Object(RuntimePolicy.Config(backend.ModernProfile!,backend.ModernStore.Settings,ConnectionMode.VpnOnly));
            Check(subscriptionRuntime["route"]!["rules"]!.ToJsonString().Contains("shared.example.org"),"Managed subscription receives the same common domain rules");
            var sharedReload=new Store(folder);
            Check(sharedReload.Settings.SharedQuickRoutes!.Count==2&&sharedReload.Profiles.All(p=>p.QuickRoutes.Count==2),"Common rules persist across restart for every group");
            var migration=new Store(Path.Combine(folder,"migration"));
            migration.Profiles.Add(new Profile{Name="Мой VPN",Config=profile.Config,QuickRoutes=[new("domain","same.example.org","old-proxy",true)]});
            migration.Profiles.Add(new Profile{Name="Other group",SubscriptionGroup="Other",Config=profile.Config,QuickRoutes=[new("domain","same.example.org","proxy"),new("domain_suffix","extra.example.org","proxy")]});
            SharedRouting.Migrate(migration);
            Check(migration.Settings.SharedQuickRoutes!.Count==2&&migration.Settings.SharedQuickRoutes[0].Direct,"Migration preserves My VPN precedence and merges unique rules from other groups");
            var alternate=SharedRouting.Copy(sharedCopy);alternate.Config=alternate.Config.Replace("\"proxy\"","\"other-proxy\"");
            var remapped=SharedRouting.ForProfile(alternate,backend.ModernStore.Settings);
            Check(remapped.First(r=>!r.Direct).Outbound=="other-proxy","Common VPN route resolves to the destination profile outbound tag");
            var originalCommon=backend.ModernStore.Settings.Connection!;
            backend.ModernSaveConnection(true,true,37555);
            var commonReload=new Store(folder).Settings.Connection!;
            Check(commonReload.Tun&&commonReload.SystemProxy&&commonReload.LocalPort==37555,"Shared network mode persists independently from individual server profiles");
            foreach(var target in backend.ModernStore.Profiles)
            {
                var effectiveProfile=SharedConnection.Effective(target,backend.ModernStore.Settings);
                var effectiveConfig=JsonTools.Object(RuntimePolicy.Config(target,backend.ModernStore.Settings,ConnectionMode.VpnOnly));
                Check(effectiveProfile.SystemProxy&&JsonTools.LocalProxy(effectiveProfile).Port==37555&&effectiveConfig["inbounds"]!.AsArray().Any(i=>JsonTools.Text(i?["type"])=="tun"),"Raw and subscription profiles share TUN, system proxy and listener port: "+target.Core);
            }
            backend.ModernSelect(backend.ModernStore.Profiles.Single(p=>p.Id==profile.Id));
            ((CheckBox)backend.FindName("SystemProxy")).IsChecked=false;((TextBox)backend.FindName("ProxyAddress")).Text="http://127.0.0.1:37556";Call(backend,"SaveFields");
            var fromOther=SharedConnection.Effective(backend.ModernStore.Profiles.Single(p=>p.Id==imported.Id),backend.ModernStore.Settings);
            Check(!fromOther.SystemProxy&&JsonTools.LocalProxy(fromOther).Port==37556,"Editing connection fields in another profile changes the common mode for subscriptions");
            backend.ModernSaveConnection(originalCommon.Tun,originalCommon.SystemProxy,originalCommon.LocalPort);
            var networkMigration=new Store(Path.Combine(folder,"network-migration"));
            networkMigration.Profiles.Add(new(){Name="Мой VPN",ProxyUri="http://127.0.0.1:37418",Config="{\"inbounds\":[{\"type\":\"mixed\",\"listen_port\":37418,\"set_system_proxy\":true},{\"type\":\"tun\",\"auto_route\":true}],\"outbounds\":[{\"type\":\"direct\"}]}"});
            SharedConnection.Migrate(networkMigration);
            Check(networkMigration.Settings.Connection==new ConnectionSettings(true,true,37418),"My VPN JSON seeds both TUN and embedded system proxy plus port 37418");
            var address=TunRuntime.SelectAddress(["172.19.0.1/30","172.30.0.0/16","172.29.240.0/24"]);
            Check(!address.StartsWith("172.30.")&&!address.StartsWith("172.29.240."),"TUN allocator avoids occupied interface and routed subnets");
            var tunSource="{\"inbounds\":[{\"type\":\"tun\",\"tag\":\"relay-tun\",\"interface_name\":\"happ-xray\",\"address\":[\"172.19.0.1/30\",\"fd00::1/126\"],\"auto_route\":true},{\"type\":\"tun\",\"tag\":\"second\",\"address\":[\"172.19.0.1/30\"]}]}";
            var tuned=JsonTools.Object(TunRuntime.Configure(tunSource,["172.19.0.1/30","172.30.240.0/30"],"RelayDesk-QA"));
            var tunInputs=tuned["inbounds"]!.AsArray();
            Check(tunInputs[0]!["interface_name"]!.ToString()=="RelayDesk-QA-0"&&tunInputs[1]!["interface_name"]!.ToString()=="RelayDesk-QA-1","Runtime TUN interfaces never reuse a foreign application name");
            Check(tunInputs[0]!["address"]![0]!.ToString()!=tunInputs[1]!["address"]![0]!.ToString()&&tunInputs[0]!["address"]![0]!.ToString()!="172.19.0.1/30","Multiple TUN inputs receive distinct free IPv4 subnets");
            Check(tunInputs[0]!["address"]!.AsArray().Any(a=>a!.ToString()=="fd00::1/126")&&tunSource.Contains("happ-xray"),"TUN adaptation preserves IPv6 and leaves source configuration untouched");
            var liveAddress=TunRuntime.SelectAddress(TunRuntime.OccupiedNetworks());
            Check(!TunRuntime.OccupiedNetworks().Any(n=>RuleMatching.NetworkContains(n,liveAddress)||RuleMatching.NetworkContains(liveAddress,n)),"TUN address is free in the current Windows network inventory");
            var legacySubscription=SharedRouting.Copy(imported);legacySubscription.Dns="1.1.1.1";
            var legacyRuntime=JsonTools.Object(RuntimePolicy.Config(legacySubscription,backend.ModernStore.Settings,ConnectionMode.VpnOnly));
            Check(legacyRuntime["dns"]!["servers"]![0]!["type"]!.ToString()=="local"&&legacySubscription.Dns=="1.1.1.1","Legacy subscription default DNS uses system resolver without mutating source profile");
            legacySubscription.Dns="9.9.9.9";
            Check(JsonTools.Object(RuntimePolicy.Config(legacySubscription,backend.ModernStore.Settings,ConnectionMode.VpnOnly))["dns"]!["servers"]![0]!["server"]!.ToString()=="9.9.9.9","Custom DNS survives subscription runtime correction");
            legacySubscription.Tun=false;legacySubscription.SystemProxy=false;
            Check(ConnectionTransport.Planned(legacySubscription,backend.ModernStore.Settings,ConnectionMode.VpnOnly).StartsWith("TUN"),"Auto TUN from shared application rules overrides stored false flags in connection caption");
            Check(ConnectionTransport.Describe(legacySubscription,legacySubscription.Config).StartsWith("Локальный прокси"),"Actual configuration without TUN stays local despite newly saved shared settings");
            legacySubscription.SystemProxy=true;
            Check(ConnectionTransport.Describe(legacySubscription,legacyRuntime.ToJsonString())=="TUN + системный прокси","Connection caption reports combined TUN and system proxy accurately");
            Call(window,"RefreshFromBackend");
            Check(((TextBlock)window.FindName("ServerHint")).Text.Contains("При подключении: TUN"),"Stopped dashboard labels the planned auto TUN mode without claiming active traffic");
            Call(window,"SelectPage",0);
            var preservedRules=System.Text.Json.JsonSerializer.Serialize(backend.ModernStore.Settings.SharedQuickRoutes);
            var preservedProfiles=File.ReadAllText(Path.Combine(folder,"profiles.json"));
            foreach(var preset in new[]{"direct","proxy","rules"})
            {
                Click(window,RoutingPresets.Label(preset));
                Check(new Store(folder).Settings.RoutingPreset==preset&&!backend.ModernSession.ProxyRunning,"Preset selection persists without connecting an inactive VPN: "+preset);
                var forced=JsonTools.Object(RuntimePolicy.Config(backend.ModernProfile!,backend.ModernStore.Settings,ConnectionMode.Combined));
                if(preset!="rules")
                {
                    string tag=forced["route"]!["final"]!.ToString();
                    var outbound=forced["outbounds"]!.AsArray().First(o=>o!["tag"]!.ToString()==tag)!;
                    Check(preset=="direct"?outbound["type"]!.ToString()=="direct":outbound["type"]!.ToString() is not ("direct" or "block" or "dns"),"Forced preset resolves to the intended outbound: "+preset);
                    Check(!forced["route"]!["rules"]!.ToJsonString().Contains("shared.example.org"),"Forced preset overrides saved exceptions without deleting them: "+preset);
                    Check(forced["dns"]!["servers"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().Where(s=>s["type"]!.ToString()!="local").All(s=>s["detour"]!.ToString()==tag),"DNS follows forced transport while bootstrap stays local: "+preset);
                }
                else Check(forced["route"]!["rules"]!.ToJsonString().Contains("shared.example.org"),"Returning to rules restores saved routing order");
            }
            Check(System.Text.Json.JsonSerializer.Serialize(backend.ModernStore.Settings.SharedQuickRoutes)==preservedRules&&File.ReadAllText(Path.Combine(folder,"profiles.json"))==preservedProfiles,"Preset round trip preserves all source configurations and common rules byte-for-byte");
            var xrayPreset="{\"outbounds\":[{\"protocol\":\"freedom\",\"tag\":\"direct\"},{\"protocol\":\"socks\",\"tag\":\"remote\"}],\"routing\":{\"rules\":[{\"type\":\"field\",\"domain\":[\"full:exception.example\"],\"outboundTag\":\"direct\"}]}}";
            foreach(var preset in new[]{"direct","proxy"})Check(JsonTools.Object(RoutingPresets.Apply(xrayPreset,"Xray",preset))["routing"]!["rules"]![0]!["outboundTag"]!.ToString()==(preset=="direct"?"direct":"remote"),"Xray preset overrides source exceptions: "+preset);
            try{RoutingPresets.Apply("{\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}]}","sing-box","proxy");throw new Exception("Missing VPN outbound accepted");}catch(InvalidOperationException){}
            Check(true,"VPN preset rejects a profile containing only direct transport");
            Call(window,"SelectPage",2);
            foreach(var fallback in new[]{"direct","proxy","zapret","source"})
            {
                var fallbackChoice=Visual<ComboBox>((DependencyObject)window.FindName("DefaultRouteHost")).Single();
                Check(fallbackChoice.IsEnabled&&fallbackChoice.Items.Count==4,"Default route selector remains editable: "+fallback);
                fallbackChoice.SelectedItem=RoutingFallback.Label(fallback);Pump();
                Check(new Store(folder).Settings.FallbackRoute==fallback,"Default route persists from modern dropdown for all profiles: "+fallback);
                foreach(var mode in new[]{ConnectionMode.VpnOnly,ConnectionMode.Combined})
                {
                    var effective=JsonTools.Object(RuntimePolicy.Config(backend.ModernProfile!,backend.ModernStore.Settings,mode));
                    Check(effective["route"]!["rules"]!.ToJsonString().Contains("shared.example.org"),"Fallback keeps individual common rules: "+fallback+"/"+mode);
                    var expected=fallback=="direct"||fallback=="zapret"&&mode==ConnectionMode.Combined?"direct":"proxy";
                    var tag=effective["route"]!["final"]!.ToString();
                    Check(effective["outbounds"]!.AsArray().Any(o=>JsonTools.Text(o?["tag"])==tag&&(expected=="direct"?JsonTools.Text(o?["type"])=="direct":JsonTools.Text(o?["type"])=="socks")),"Fallback selects correct effective transport: "+fallback+"/"+mode);
                }
            }
            var xrayFallback=JsonTools.Object(RoutingFallback.Apply(xrayPreset,"Xray","proxy",true));
            Check(xrayFallback["routing"]!["rules"]!.AsArray().Count==2&&xrayFallback["routing"]!["rules"]![0]!["domain"]!=null&&xrayFallback["routing"]!["rules"]![1]!["outboundTag"]!.ToString()=="remote","Xray fallback retains domain exception before final VPN catchall");
            backend.ModernSaveFallback("zapret");
            Check(RuntimePolicy.ZapretRoutes(backend.ModernProfile,backend.ModernStore.Settings,ConnectionMode.Combined).Count(r=>r.Zapret&&r.Kind=="ip_cidr"&&r.Value.EndsWith("/0"))==2,"Zapret fallback captures both IPv4 and IPv6 destinations");
            backend.ModernStore.Settings.RoutingPreset="proxy";
            Check(RuntimePolicy.ZapretRoutes(backend.ModernProfile,backend.ModernStore.Settings,ConnectionMode.Combined).Count==0&&JsonTools.Text(JsonTools.Object(RuntimePolicy.Config(backend.ModernProfile!,backend.ModernStore.Settings,ConnectionMode.Combined))["route"]?["final"])=="proxy","Forced VPN preset temporarily overrides saved zapret fallback");
            backend.ModernStore.Settings.RoutingPreset="rules";backend.ModernSaveFallback("source");Call(window,"LoadProfileRules");
            backend.ModernSelect(backend.ModernStore.Profiles.Single(p=>p.Id==profile.Id));backend.ModernStore.Profiles.RemoveAll(p=>p.Id==imported.Id);backend.ModernStore.Save();
            Call(window,"SelectPage",1);QrChecks(window,backend,folder);
            WindowDropChecks(window,backend,folder);
            Call(window,"SelectPage",3);
            var editor=(TextBox)backend.FindName("ConfigEditor");var profileName=(TextBox)backend.FindName("ProfileName");
            string savedEditor=editor.Text,savedName=profileName.Text;
            editor.Text="{ draft JSON";profileName.Text="Unsaved update draft";
            var resume=backend.CaptureUpdate();
            Check(resume.Page==3&&!resume.Vpn&&!resume.Zapret&&resume.Draft["ConfigEditor"]==editor.Text&&resume.Draft["ProfileName"]==profileName.Text,"Update snapshot preserves disconnected state, page and unsaved draft without validating it as live config");
            Check(UpdateRestart.Take(folder,Guid.NewGuid().ToString("N"))==null&&File.Exists(UpdateRestart.PathIn(folder)),"Wrong restart token cannot consume saved state");
            editor.Text=savedEditor;profileName.Text=savedName;
            backend.ResumeAfterUpdate(resume.Token).GetAwaiter().GetResult();Pump();
            Check(editor.Text=="{ draft JSON"&&profileName.Text=="Unsaved update draft"&&!backend.ModernSession.ProxyRunning,"Resume restores draft fields and never enables previously disabled VPN");
            Check(UpdateRestart.Take(folder,resume.Token)==null,"Restart snapshot is consumed once and cannot reconnect on a later ordinary launch");
            editor.Text=savedEditor;profileName.Text=savedName;
            foreach(var mode in new[]{(false,false),(true,false),(false,true),(true,true)})
            {
                var token=Guid.NewGuid().ToString("N");UpdateRestart.Save(folder,new(){Token=token,Vpn=mode.Item1,Zapret=mode.Item2,ProfileId=profile.Id});
                var taken=UpdateRestart.Take(folder,token)!;Check(taken.Vpn==mode.Item1&&taken.Zapret==mode.Item2&&taken.ProfileId==profile.Id,"Restart snapshot retains component combination "+mode);
            }
            var expired=Guid.NewGuid().ToString("N");UpdateRestart.Save(folder,new(){Token=expired,CreatedUtc=DateTime.UtcNow.AddHours(-1),Vpn=true});
            Check(UpdateRestart.Take(folder,expired)==null,"Expired restart state cannot reconnect unexpectedly");File.Delete(UpdateRestart.PathIn(folder));
            Call(window,"ClosePreview",window,new RoutedEventArgs());Pump(350);Check(!app.Windows.OfType<MainWindow>().Any(),"Caption close button uses existing core shutdown lifecycle");
            var reopened=new Store(folder);Check(reopened.Profiles.Single().Config==profile.Config&&reopened.Settings.LastConnectedProfileId==profile.Id,"Settings and profile survive complete modern-window lifecycle");
            Console.WriteLine($"COMPLETE: {count} production shell and data-preservation checks passed.");return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally{window?.Close();Pump();app.Shutdown();}
    }
}
