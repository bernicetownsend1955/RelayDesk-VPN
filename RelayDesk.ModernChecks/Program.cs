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
            Call(window,"EnterQuietMode");Pump();Check(((UIElement)window.FindName("Shell")).Opacity<.1,"Production quiet mode fades actual HUD");
            var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};window.RaiseEvent(click);Check(click.Handled&&((UIElement)window.FindName("QuietOverlay")).Visibility==Visibility.Collapsed,"First click restores production HUD without clicking through");
            Pump();Call(window,"SelectPage",0);Pump();
            window.UpdateLayout();var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bmp.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine("RelayDesk.ModernChecks","modern.png")))png.Save(stream);
            Call(window,"ClosePreview",window,new RoutedEventArgs());Pump(350);Check(!app.Windows.OfType<MainWindow>().Any(),"Caption close button uses existing core shutdown lifecycle");
            var reopened=new Store(folder);Check(reopened.Profiles.Single().Config==profile.Config&&reopened.Settings.LastConnectedProfileId==profile.Id,"Settings and profile survive complete modern-window lifecycle");
            Console.WriteLine($"COMPLETE: {count} production shell and data-preservation checks passed.");return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally{window?.Close();Pump();app.Shutdown();}
    }
}
