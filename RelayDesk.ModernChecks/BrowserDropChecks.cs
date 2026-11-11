using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using RelayDesk;

static partial class Program
{
    static void BrowserDropChecks(MainWindow window)
    {
        window.Left=600;window.Top=50;window.Width=1200;window.Height=850;window.Topmost=true;window.Activate();Pump(200);
        var automatic=(OverlayBridge?)typeof(MainWindow).GetField("windowDropBridge",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window);
        string? error=null;bool accepted=false;int dialogs=0;
        var backend=(Workspace)typeof(MainWindow).GetField("backend",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        string saved=File.ReadAllText(Path.Combine(backend.ModernStore.Folder,"settings.json"));
        var bridge=automatic??new OverlayBridge((_,_)=>{},()=>{},()=>{},drop=>{Console.WriteLine("Browser formats delivered: files="+drop.Files.Length+"; text="+drop.Text);Call(window,"HandleWindowDrop",drop);},s=>error=s,()=>{},new WindowInteropHelper(window).Handle.ToInt64());
        if(automatic==null)bridge.Show();Wait(()=>bridge.DisplayedName=="drop-target"||error!=null);
        Console.WriteLine("Browser target admin="+EngineSession.IsAdmin()+"; worker="+bridge.DisplayedName+"; error="+error);
        var closer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};closer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<DropRouteWindow>().FirstOrDefault();if(dialog==null)return;accepted=true;dialogs++;Console.WriteLine("Browser route dialog: "+string.Join(" | ",Visual<TextBlock>(dialog).Select(t=>t.Text)));dialog.Close();};closer.Start();
        GetCursorPos(out var before);var config=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"browser-source.json"))).RootElement;
        int x=config.GetProperty("x").GetInt32(),y=config.GetProperty("y").GetInt32();var target=window.PointToScreen(new Point(400,300));
        bool focus=config.TryGetProperty("focusAddress",out var focusValue)&&focusValue.GetBoolean();
        var driver=new Thread(()=>{Thread.Sleep(2000);SetCursorPos(x,y);if(focus){mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);Thread.Sleep(150);keybd_event(17,0,0,UIntPtr.Zero);keybd_event(76,0,0,UIntPtr.Zero);keybd_event(76,0,2,UIntPtr.Zero);keybd_event(17,0,2,UIntPtr.Zero);}Thread.Sleep(900);using(var image=new System.Drawing.Bitmap(500,100)){using var graphics=System.Drawing.Graphics.FromImage(image);graphics.CopyFromScreen(18,10,0,0,image.Size);image.Save(Path.Combine(AppContext.BaseDirectory,"browser-address.png"));}mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(200);SetCursorPos(x+25,y+5);Thread.Sleep(400);for(int step=1;step<=20;step++){SetCursorPos(x+(int)((target.X-x)*step/20),y+(int)((target.Y-y)*step/20));Thread.Sleep(30);}Thread.Sleep(500);mouse_event(4,0,0,0,UIntPtr.Zero);}){IsBackground=true};
        try{driver.Start();Wait(()=>accepted);Pump(800);Console.WriteLine("Browser drop accepted="+accepted+"; receiver="+bridge.DisplayedName);Check(accepted,"Actual Yandex Browser link opens route menu");Check(dialogs==1&&File.ReadAllText(Path.Combine(backend.ModernStore.Folder,"settings.json"))==saved,"Browser drop opens exactly one menu and cancelling it preserves saved settings");if(Environment.GetCommandLineArgs().Contains("--browser-no-ole"))Check(bridge.DisplayedName=="address-drop","Address gesture still opens menu when browser does not deliver OLE payload");}
        finally{driver.Join(3000);mouse_event(4,0,0,0,UIntPtr.Zero);SetCursorPos(before.X,before.Y);closer.Stop();if(automatic==null)bridge.Close();window.Topmost=false;}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
