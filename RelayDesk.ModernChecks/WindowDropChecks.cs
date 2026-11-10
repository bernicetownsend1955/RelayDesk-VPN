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
        RealWindowDrop(window,backend);
        RealWindowDrop(window,backend,true);
        backend.ModernSaveRoutes(saved);backend.ModernStore.Settings.ZapretArgs=savedStrategy;backend.ModernStore.Save();Call(window,"LoadProfileRules");
        Check(!backend.ModernSession.ProxyRunning&&!backend.ModernSession.ZapretRunning,"Drop workflows never start a stopped VPN or zapret");
    }
    static void RealWindowDrop(MainWindow window,Workspace backend,bool file=false)
    {
        string? bridgeError=null;bool received=false,dialogSeen=false;Exception? failure=null;
        var bridge=new OverlayBridge((_,_)=>{},()=>{},()=>{},payload=>{received=file?payload.Files.SequenceEqual(new[]{Environment.ProcessPath!}):payload.Text=="https://ole-drop.example.net/path";Call(window,"HandleWindowDrop",payload);},s=>bridgeError=s,()=>{},new WindowInteropHelper(window).Handle.ToInt64());
        var source=new Window{Title="RelayDesk QA drag source",Width=150,Height=120,Left=8,Top=8,Topmost=true,ShowInTaskbar=false,Content=new TextBlock{Text="QA link",Margin=new Thickness(20)}};
        var closer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        closer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<DropRouteWindow>().FirstOrDefault();if(dialog==null)return;dialogSeen=true;dialog.Close();};
        GetCursorPos(out var original);Thread? driver=null;
        try
        {
            window.WindowState=WindowState.Normal;window.Left=180;window.Top=160;window.Topmost=true;window.Activate();
            bridge.Show();Wait(()=>bridge.DisplayedName=="drop-target"||bridgeError!=null);
            Check(bridge.DisplayedName=="drop-target"&&bridgeError==null,"Main drop receiver starts in separate process without enabling traffic overlay: "+bridgeError);
            source.Show();source.Activate();Pump(100);var destination=window.PointToScreen(new Point(400,220));
            bool started=false;
            source.MouseMove+=(_,e)=>
            {
                if(started||e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed)return;started=true;
                try{var payload=file?new DataObject(DataFormats.FileDrop,new[]{Environment.ProcessPath!}):new DataObject(DataFormats.UnicodeText,"https://ole-drop.example.net/path");DragDrop.DoDragDrop(source,payload,DragDropEffects.Copy);}catch(Exception ex){failure=ex;}
            };
            closer.Start();
            driver=new Thread(()=>
            {
                try{SetCursorPos(60,70);Thread.Sleep(100);mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(120);SetCursorPos(90,85);Thread.Sleep(250);SetCursorPos((int)destination.X,(int)destination.Y);Thread.Sleep(300);GetCursorPos(out var current);var title=new System.Text.StringBuilder(200);GetWindowText(GetAncestor(WindowFromPoint(current),2),title,200);Console.WriteLine("QA OLE target: "+title+" at "+current.X+","+current.Y);SetCursorPos((int)destination.X+12,(int)destination.Y+8);Thread.Sleep(550);mouse_event(4,0,0,0,UIntPtr.Zero);}
                catch(Exception ex){failure=ex;}
            }){IsBackground=true};driver.Start();
            Wait(()=>dialogSeen||failure!=null);if(failure!=null)throw failure;
            Check(received&&dialogSeen,"Actual OLE "+(file?"EXE file":"browser link")+" drag lands on helper and opens route menu in main process (started="+started+", received="+received+", dialog="+dialogSeen+", state="+bridge.DisplayedName+")");
        }
        finally{driver?.Join(3000);mouse_event(4,0,0,0,UIntPtr.Zero);SetCursorPos(original.X,original.Y);closer.Stop();source.Close();bridge.Close();window.Topmost=false;Pump(250);}
    }
    [StructLayout(LayoutKind.Sequential)]struct CursorPoint{public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(CursorPoint point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flag);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int count);
}
