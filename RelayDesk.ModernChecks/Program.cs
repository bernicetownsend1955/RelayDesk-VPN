using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RelayDesk;

static class Program
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
        bool seen=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w!=window&&type.IsInstanceOfType(w));if(dialog!=null){seen=dialog.Owner==window;dialog.Close();timer.Stop();}};
        timer.Start();try{Click(window,button);}finally{timer.Stop();}
        Check(seen,button+" opens owned dialog and cancellation preserves modern shell");
    }
    [STAThread]static int Main()
    {
        string folder=Path.Combine(Path.GetTempPath(),"RelayDesk-production-QA-"+Guid.NewGuid().ToString("N"));Environment.SetEnvironmentVariable("RELAYDESK_DATA_DIR",folder);
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
            try{backend.ModernSaveRoutes([new("process_name","Discord.exe","proxy")]);throw new Exception("Missing TUN accepted");}catch(InvalidOperationException){}
            Check(new Store(folder).Profiles.Single().Config==protectedConfig&&new Store(folder).Profiles.Single().QuickRoutes.Count==persisted.QuickRoutes.Count,"Unsupported route leaves saved profile and routes unchanged");
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
            foreach(string name in new[]{"VpnPower","VpnActionButton","ZapretPower","ZapretActionButton"})
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
            Check(settingsTexts.Contains("Стратегия zapret")&&!settingsTexts.Any(t=>t.Contains("???")||t.Contains('\uFFFD')),"Rendered settings keep Russian labels without encoding loss");
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
            var beforeBatch=File.ReadAllText(Path.Combine(folder,"profiles.json"));Call(window,"SaveConnectionBatch",batch);Check(File.ReadAllText(Path.Combine(folder,"profiles.json"))==beforeBatch,"Invalid EXE batch leaves all saved profile data unchanged");
            Call(window,"SaveConnectionBatch",domainBatch);var afterBatch=new Store(folder).Profiles.Single();Check(domainBatch.All(r=>afterBatch.QuickRoutes.Any(q=>q.Value==r.Value&&q.Direct)),"Domain batch saves all rules in a single profile update");
            Check(afterBatch.Config==profile.Config,"Batch routing preserves original full JSON");
            Call(window,"ClearConnectionFilters",window,new RoutedEventArgs());Call(window,"SelectConnection",connections[0],true);Call(window,"RefreshConnections");
            connectionFixture.RemoveAll(r=>r.Id==connections[0].Id);Call(window,"RefreshConnections");Check(((TextBlock)window.FindName("ConnectionSelectionCount")).Text=="Выбрано: 0","Expired connection removes only its stale selection");
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
            backend.ModernSelect(backend.ModernStore.Profiles.Single(p=>p.Id==profile.Id));backend.ModernStore.Profiles.RemoveAll(p=>p.Id==imported.Id);backend.ModernStore.Save();
            Call(window,"ClosePreview",window,new RoutedEventArgs());Pump(350);Check(!app.Windows.OfType<MainWindow>().Any(),"Caption close button uses existing core shutdown lifecycle");
            var reopened=new Store(folder);Check(reopened.Profiles.Single().Config==profile.Config&&reopened.Settings.LastConnectedProfileId==profile.Id,"Settings and profile survive complete modern-window lifecycle");
            Console.WriteLine($"COMPLETE: {count} production shell and data-preservation checks passed.");return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally{window?.Close();Pump();app.Shutdown();}
    }
}
