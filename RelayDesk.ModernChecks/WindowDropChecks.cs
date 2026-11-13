using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using RelayDesk;

static partial class Program
{
    static void WindowDropChecks(MainWindow window,Workspace backend,string folder)
    {
        var saved=backend.ModernStore.Settings.SharedQuickRoutes!.ToList();
        Check(OverlayDrops.Effect(DragDropEffects.Link,true)==DragDropEffects.Link&&OverlayDrops.Effect(DragDropEffects.Move,true)==DragDropEffects.None,"URL link-only drags are accepted without asking the source to move data");
        Check(BrowserAddressDrag.Normalize("example.org")=="https://example.org"&&BrowserAddressDrag.Normalize("https://example.org/path")=="https://example.org/path","Browser address accepts displayed host and full HTTP URL");
        Check(BrowserAddressDrag.Normalize("chrome://settings")==null&&BrowserAddressDrag.Normalize("javascript:alert(1)")==null&&BrowserAddressDrag.Normalize("two words")==null,"Browser address rejects internal commands and ordinary text");
        Check(!BrowserAddressDrag.IsAddressControl("HTMLInputElement","search","Search")&&BrowserAddressDrag.IsAddressControl("OmniboxViewViews","",""),"Address fallback distinguishes browser chrome from webpage input");
        string savedStrategy=backend.ModernStore.Settings.ZapretArgs;
        backend.ModernStore.Settings.ZapretArgs="--dpi-desync=multisplit";backend.ModernStore.Save();
        string profilesBefore=File.ReadAllText(Path.Combine(folder,"profiles.json")),settingsBefore=File.ReadAllText(Path.Combine(folder,"settings.json"));
        var unicodeUrl=new DataObject();unicodeUrl.SetData("UniformResourceLocatorW",new MemoryStream(System.Text.Encoding.Unicode.GetBytes("https://drop.example.net/path\0")));
        Check(OverlayDrops.Supports(unicodeUrl)&&OverlayDrops.Rule(OverlayDrops.Read(unicodeUrl).Text).Value=="drop.example.net","Browser URL stream format resolves a domain without fetching the link");
        var gesture=new DropGesture();
        Check(!gesture.Update(true,true,100,100,true)&&!gesture.Update(true,true,200,200,true),"Dragging or selecting within RelayDesk never opens external drop surface");
        gesture.Update(false,false,0,0,true);gesture.Update(true,false,10,10,true);
        Check(gesture.Update(true,true,100,100,true),"External drag entering main window activates receiver");
        Check(!gesture.Update(true,true,100,100,true,true)&&!gesture.Update(true,true,110,110,true),"Escape suppresses receiver until the mouse is released");
        gesture.Update(false,false,0,0,true);gesture.Update(true,false,10,10,true);
        Check(!gesture.Update(true,true,100,100,false),"Disabled or hidden main window cannot be covered by drop receiver");
        void Drop(OverlayDrop payload,string? route)
        {
            bool seen=false;Exception? error=null;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(60)};
            timer.Tick+=(_,_)=>
            {
                var dialog=Application.Current.Windows.OfType<DropRouteWindow>().SingleOrDefault();if(dialog==null)return;
                timer.Stop();seen=true;
                try
                {
                    Check(dialog.Owner==window&&Visual<RadioButton>(dialog).Count()==3,"Main window drop opens owned dialog with exactly VPN, zapret and direct choices");
                    Check(!Visual<Button>(dialog).Any(b=>b.Content?.ToString()=="Сохранить и применить"),"Disconnected VPN never offers immediate reconnect action");
                    if(route==null){dialog.UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(dialog);QrImages.Save(image,Path.Combine("RelayDesk.ModernChecks","window-drop.png"));}
                    if(route!=null){Visual<RadioButton>(dialog).Single(b=>b.Tag?.ToString()==route).IsChecked=true;Visual<Button>(dialog).Single(b=>b.Content?.ToString()=="Сохранить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                    else dialog.Close();
                }
                catch(Exception ex){error=ex;dialog.Close();}
            };
            timer.Start();try{Call(window,"HandleWindowDrop",payload);Pump();}finally{timer.Stop();}
            if(error!=null)throw error;Check(seen,"Drop payload reached quick route settings");
        }
        Check(window.AllowDrop,"Main window accepts drops on every page including over child controls");
        Drop(new([],"https://drop.example.net/path"),null);
        Check(File.ReadAllText(Path.Combine(folder,"profiles.json"))==profilesBefore&&File.ReadAllText(Path.Combine(folder,"settings.json"))==settingsBefore,"Cancelling dropped target preserves profile and settings files byte-for-byte");
        foreach(var route in RouteRules.Routes)
        {
            Call(window,"SelectPage",route=="Через VPN"?0:route=="Через zapret"?3:4);
            Drop(new([],"https://drop.example.net/path"),route);
            var matches=new Store(folder).Settings.SharedQuickRoutes!.Where(r=>r.Value=="drop.example.net").ToArray();
            Check(matches.Length==1&&matches[0].Direct==(route=="Напрямую")&&matches[0].Zapret==(route=="Через zapret"),"Drop edits one shared rule without duplicates: "+route+" / "+((TextBlock)window.FindName("Notice")).Text);
            Check(backend.ModernStore.Profiles.All(p=>p.QuickRoutes.Any(r=>r.Value=="drop.example.net")),"Dropped target rule is mirrored to every profile");
        }
        Drop(new([Environment.ProcessPath!],""),"Через VPN");
        Check(backend.ModernStore.Settings.SharedQuickRoutes!.Any(r=>r.Kind=="process_path"&&r.Value==Environment.ProcessPath),"EXE drop saves a full process path without launching the executable");
        // Exercise the real Windows OLE drag loop and the separate helper process.
        if(!Environment.GetCommandLineArgs().Contains("--skip-physical-drag"))
        {RealWindowDrop(window,backend);RealWindowDrop(window,backend,true);}
        else Console.WriteLine("SKIP physical OLE source: injected mouse driver did not start a drag in this desktop session; route/drop payload checks still run.");
        backend.ModernSaveRoutes(saved);backend.ModernStore.Settings.ZapretArgs=savedStrategy;backend.ModernStore.Save();Call(window,"LoadProfileRules");
        Check(!backend.ModernSession.ProxyRunning&&!backend.ModernSession.ZapretRunning,"Drop workflows never start a stopped VPN or zapret");
    }
    static void RealWindowDrop(MainWindow window,Workspace backend,bool file=false)
    {
        string? bridgeError=null;bool received=false,dialogSeen=false;Exception? failure=null;
        var automatic=(OverlayBridge?)typeof(MainWindow).GetField("windowDropBridge",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window);
        var bridge=automatic??new OverlayBridge((_,_)=>{},()=>{},()=>{},payload=>{received=file?payload.Files.SequenceEqual(new[]{Environment.ProcessPath!}):payload.Text=="https://ole-drop.example.net/path";Call(window,"HandleWindowDrop",payload);},s=>bridgeError=s,()=>{},new WindowInteropHelper(window).Handle.ToInt64());
        var closer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        closer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<DropRouteWindow>().FirstOrDefault();if(dialog==null)return;dialogSeen=true;if(automatic!=null)received=Visual<TextBlock>(dialog).Any(t=>t.Text==(file?Environment.ProcessPath!:"ole-drop.example.net"));dialog.Close();};
        GetCursorPos(out var original);System.Diagnostics.Process? source=null;
        string result=Path.Combine(Path.GetTempPath(),"RelayDesk-ole-source-"+Guid.NewGuid().ToString("N")+".txt");
        try
        {
            window.WindowState=WindowState.Normal;window.Left=180;window.Top=160;window.Topmost=true;window.Activate();
            if(automatic==null)bridge.Show();Wait(()=>bridge.DisplayedName is "drop-target" or "drag-enter"||bridgeError!=null);
            Check(bridge.DisplayedName is "drop-target" or "drag-enter"&&bridgeError==null,"Main drop receiver starts in separate process without enabling traffic overlay: "+bridgeError);
            Check(bridge.HostElevated==false,"Drop receiver reports standard Windows rights even when main window is elevated");
            var destination=window.PointToScreen(new Point(400,220));
            closer.Start();
            source=OverlayBridge.StartDesktopProcess($"--ole-source {(int)destination.X} {(int)destination.Y} {(file?"file":"link")} \"{result}\"",Environment.ProcessPath!);
            Console.WriteLine("QA source executable: "+Environment.ProcessPath+"; pid="+source.Id);
            Wait(()=>{source.Refresh();return source.MainWindowHandle!=IntPtr.Zero||source.HasExited;});
            AllowSetForegroundWindow((uint)source.Id);SetForegroundWindow(source.MainWindowHandle);
            Wait(()=>dialogSeen||failure!=null||source.HasExited);if(failure!=null)throw failure;
            Wait(()=>source.HasExited);string report=(File.Exists(result)?File.ReadAllText(result):"no source result")+"; source exited="+source.HasExited+(source.HasExited?"; exit="+source.ExitCode:"");
            Check(received&&dialogSeen&&report.Contains("Admin=False")&&report.Contains("Started=True")&&bridge.DisplayedName=="drag-enter","Standard-user OLE "+(file?"EXE file":"browser link")+" enters helper and opens main route menu: "+report+"; receiver="+bridge.DisplayedName);
        }
        finally{if(source!=null){if(!source.HasExited){source.CloseMainWindow();if(!source.WaitForExit(1500))source.Kill();}source.Dispose();}mouse_event(4,0,0,0,UIntPtr.Zero);SetCursorPos(original.X,original.Y);closer.Stop();if(automatic==null)bridge.Close();window.Topmost=false;Pump(250);}
    }
    [STAThread]static int OleSource()
    {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--ole-source");int x=int.Parse(args[index+1]),y=int.Parse(args[index+2]);bool file=args[index+3]=="file";string result=args[index+4];
        File.WriteAllText(result,"Source started; Admin="+EngineSession.IsAdmin());
        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var source=new Window{Title="RelayDesk QA drag source",Width=150,Height=140,Left=8,Top=8,Topmost=true,ShowInTaskbar=true,Content=new TextBlock{Text="QA link",Margin=new Thickness(20)}};
        bool started=false;Thread? driver=null;
        source.MouseMove+=(_,e)=>{if(started||e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed)return;started=true;try{DragDrop.DoDragDrop(source,file?new DataObject(DataFormats.FileDrop,new[]{Environment.ProcessPath!}):new DataObject(DataFormats.UnicodeText,"https://ole-drop.example.net/path"),DragDropEffects.Copy);}finally{source.Close();}};
        source.Loaded+=(_,_)=>
        {
            File.AppendAllText(result,"; Window loaded");
            var origin=source.PointToScreen(new Point(40,40));
            driver=new Thread(()=>{Thread.Sleep(1000);SetCursorPos((int)origin.X,(int)origin.Y);Thread.Sleep(200);mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(200);SetCursorPos((int)origin.X+20,(int)origin.Y+5);Thread.Sleep(400);SetCursorPos(x,y);Thread.Sleep(300);SetCursorPos(x+12,y+8);Thread.Sleep(550);mouse_event(4,0,0,0,UIntPtr.Zero);}){IsBackground=true};driver.Start();
        };
        var timeout=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};timeout.Tick+=(_,_)=>{timeout.Stop();source.Close();};timeout.Start();
        app.Run(source);driver?.Join(3000);File.WriteAllText(result,"Admin="+EngineSession.IsAdmin()+"; Started="+started);return started?0:1;
    }
    [StructLayout(LayoutKind.Sequential)]struct CursorPoint{public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flag);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int count);
    [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
}
