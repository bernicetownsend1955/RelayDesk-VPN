using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RelayDesk;

static class Program
{
    static int count;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);count++;}
    static void Pump(int ms=100){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    static object? Call(object target,string method,params object?[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    static Workspace Backend(MainWindow w)=>(Workspace)typeof(MainWindow).GetField("backend",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!;
    static IEnumerable<T> Visual<T>(DependencyObject node)where T:DependencyObject{if(node is T t)yield return t;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Visual<T>(VisualTreeHelper.GetChild(node,i)))yield return child;}
    static void Capture(MainWindow window,string name){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine("RelayDesk.OrbitChecks",name));encoder.Save(file);}
    static void Wait(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(15);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException();Pump();}}
    [STAThread]static int Main()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/Theme.xaml")});SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        MainWindow? window=null;
        try
        {
            foreach(string mode in new[]{"orbit","classic"})
            {
                string folder=Path.Combine(Path.GetTempPath(),"RelayDesk-Orbit-QA-"+Guid.NewGuid().ToString("N"));Environment.SetEnvironmentVariable("RELAYDESK_DATA_DIR",folder);Environment.SetEnvironmentVariable("RELAYDESK_INLINE_OVERLAY","1");
                var store=new Store(folder);var profile=new Profile{Name="Sweden · QA",Core="sing-box",Config="{\"inbounds\":[{\"type\":\"mixed\",\"listen\":\"127.0.0.1\",\"listen_port\":32199}],\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}],\"route\":{\"final\":\"direct\"}}",SubscriptionGroup="Подписка · QA",SubscriptionId="qa"};store.Profiles.Add(profile);store.Settings.LastConnectedProfileId=profile.Id;store.Settings.CheckComponentUpdates=false;store.Settings.QuietMode=false;store.Settings.TrayEnabled=false;store.Settings.InterfaceMotion=false;store.Settings.AsciiBackground=false;store.Settings.UiMode=mode;store.Settings.Subscriptions.Add(new(){Id="qa",Url="https://example.org/subscription",Group="Подписка · QA"});store.Save();
                window=new MainWindow();window.Show();window.Activate();Pump(500);var backend=Backend(window);
                Check(window.UsesClassicUi==(mode=="classic"),mode+" UI preference loads the correct layout");Check(backend.ModernStore.Profiles.Single().Config==profile.Config&&backend.ModernStore.Settings.Subscriptions.Single().Url==store.Settings.Subscriptions.Single().Url,mode+" layout preserves original JSON and subscription");
                Capture(window,mode+"-home.png");
                Call(window,"SelectPage",3);Pump(500);Check(Visual<ComboBox>(window).Any(c=>c.IsVisible&&c.Items.Count==2&&c.Items[0]?.ToString()=="Новый · Orbit"),mode+" settings expose the restart-based UI selector");Capture(window,mode+"-settings.png");
                foreach(int page in new[]{0,1,2,4}){Call(window,"SelectPage",page);Pump(100);Check(Visual<Button>(window).Any(b=>b.IsVisible&&b.Tag?.ToString()==page.ToString()),mode+" navigation retains page "+page);}
                if(mode=="orbit")
                {
                    Call(window,"SelectPage",0);Pump(400);Check(!((FrameworkElement)window.FindName("VpnPower")).IsVisible&&((FrameworkElement)window.FindName("OrbitVpnModes")).IsVisible,"Orbit uses only the segmented VPN controls");
                    Check(!((FrameworkElement)window.FindName("OrbitIndicator")).GetValue(UIElement.VisibilityProperty).Equals(Visibility.Collapsed),"Orbit status indicator is present");
                    Call(window,"OrbitOpenConnection",window,new RoutedEventArgs());Pump(400);Check(((FrameworkElement)window.FindName("SharedTun")).IsVisible&&((FrameworkElement)window.FindName("ZapretStrategyChoice")).IsVisible,"VPN and zapret settings share the connection view");Capture(window,"orbit-connection.png");
                    Call(window,"SelectPage",0);Call(window,"ToggleJournal",window,new RoutedEventArgs());Pump(150);var journal=(FrameworkElement)window.FindName("JournalPanel");Check(journal.IsVisible&&journal.ActualWidth>500&&journal.ActualHeight>=300,"Journal opens at usable size outside the old Viewbox");Capture(window,"orbit-journal.png");Call(window,"ToggleJournal",window,new RoutedEventArgs());
                    window.Width=1040;window.Height=700;Pump(250);Capture(window,"orbit-small.png");
                    string mihomo=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"RelayDesk","bundled-cores","mihomo","mihomo-windows-amd64-compatible.exe");
                    if(File.Exists(mihomo))
                    {
                        var socket=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);socket.Start();int port=((System.Net.IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
                        var raw=new Profile{Name="mihomo ? isolated QA",Core="mihomo",Executable=mihomo,LocalPort=port,ProxyUri="http://127.0.0.1:"+port,Config="mixed-port: "+port+"\nallow-lan: false\nmode: direct\nlog-level: warning\nrules:\n  - MATCH,DIRECT\n"};
                        var library=backend.ModernStore;library.Profiles.Add(raw);backend.ModernSelect(raw);((TextBox)backend.FindName("MihomoExe")).Text=mihomo;
                        var previousConnection=library.Settings.Connection;var task=backend.ModernSetVpnMode("proxy");Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();Pump(200);
                        Check(backend.ModernSession.ProxyRunning&&backend.ModernSession.ActiveProfile?.Id==raw.Id,"Orbit starts the original mihomo configuration with a real core");
                        task=Diagnostics.Tcp("127.0.0.1",port,CancellationToken.None);Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();
                        Check(library.Settings.Connection==previousConnection&&raw.Config.Contains("mode: direct"),"mihomo launch retains shared VPN settings and original YAML");
                        task=backend.ModernSetVpnMode("off");Wait(()=>task.IsCompleted);task.GetAwaiter().GetResult();library.Profiles.Remove(raw);backend.ModernSelect(library.Profiles.Single());
                    }

                }
                window.Close();Wait(()=>!window.IsVisible);var saved=new Store(folder);Check(saved.Settings.UiMode==mode&&saved.Profiles.Single().Config==profile.Config,mode+" shutdown preserves UI choice and profile JSON");window=null;
            }
            Console.WriteLine("COMPLETE: "+count+" working Orbit/classic checks passed.");return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
        finally{window?.Close();app.Shutdown();}
    }
}
