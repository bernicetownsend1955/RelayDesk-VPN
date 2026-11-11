using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RelayDesk;

// A medium-integrity OLE receiver appears over the elevated main window only
// during a drag that started outside it. No message filter or UAC policy changes.
public sealed class MainDropWindow : Window,IDisposable
{
    readonly IntPtr target;
    readonly Action<OverlayDrop> dropped;
    readonly Action<string>? report;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    readonly DropGesture gesture=new();
    IntPtr handle;
    bool disposed;
    DateTime releaseAt;
    bool wasDown,addressEntered;
    int addressGesture;
    Task<string?>? address;
    public MainDropWindow(IntPtr target,int parent,Action<OverlayDrop> dropped,Action<string>? report=null)
    {
        GetWindowThreadProcessId(target,out uint process);
        if(target==IntPtr.Zero||process!=parent)throw new ArgumentException("Неверное окно RelayDesk.");
        this.target=target;this.dropped=dropped;this.report=report;
        Title="RelayDesk · Перетащи приложение или ссылку";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        ShowActivated=false;ShowInTaskbar=false;Topmost=true;AllowsTransparency=true;Background=new SolidColorBrush(Color.FromArgb(245,16,19,24));
        FontFamily=new FontFamily("Segoe UI");Foreground=new SolidColorBrush(Color.FromRgb(236,238,242));
        AllowDrop=!(Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR")!=null&&Environment.GetEnvironmentVariable("RELAYDESK_QA_ADDRESS_FALLBACK")=="1");
        var text=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(32)};
        text.Children.Add(new TextBlock{Text="Отпусти приложение или ссылку",FontSize=26,Margin=new Thickness(0,0,0,12)});
        text.Children.Add(new TextBlock{Text="Выбери маршрут: VPN, zapret или напрямую",FontSize=16});
        Content=new Border{BorderBrush=new SolidColorBrush(Color.FromRgb(100,106,116)),BorderThickness=new Thickness(1),Child=text};
        handle=new WindowInteropHelper(this).EnsureHandle();
        SetWindowLongPtr(handle,-20,new IntPtr(GetWindowLongPtr(handle,-20).ToInt64()|0x08000000|0x80));
        PreviewDragEnter+=(_,_)=>report?.Invoke("drag-enter");
        PreviewDragOver+=(_,e)=>{e.Effects=OverlayDrops.Effect(e.AllowedEffects,OverlayDrops.Supports(e.Data));e.Handled=true;};
        PreviewDrop+=(_,e)=>{e.Handled=true;try{var data=OverlayDrops.Read(e.Data);addressGesture++;Hide();gesture.Cancel();Dispatcher.BeginInvoke(()=>dropped(data));}catch(Exception ex){Hide();gesture.Cancel();report?.Invoke("drop-error: "+ProgramJournal.Clean(ex.Message));}};
        timer.Tick+=(_,_)=>Poll();timer.Start();
    }
    void Poll()
    {
        if(disposed)return;
        bool down=(GetAsyncKeyState(1)&0x8000)!=0;
        if(down)releaseAt=DateTime.UtcNow;
        GetCursorPos(out var point);
        var under=GetAncestor(WindowFromPoint(point),2);
        bool over=under==target||under==handle;
        bool available=IsWindowVisible(target)&&!IsIconic(target)&&IsWindowEnabled(target);
        bool escape=(GetAsyncKeyState(27)&0x8000)!=0;
        if(down&&!wasDown){addressGesture++;addressEntered=false;address=!over?Task.Run(()=>BrowserAddressDrag.Read(point.X,point.Y)):null;}
        if(escape||!available){addressGesture++;address=null;addressEntered=false;}
        bool show=gesture.Update(down,over,point.X,point.Y,available,escape);
        if(show)addressEntered=true;
        if(!down&&wasDown&&over&&available&&addressEntered&&address!=null)_=CompleteAddress(address,addressGesture);
        wasDown=down;
        // OLE delivers Drop across processes after the mouse release. Hiding at
        // once unregisters WPF's target before that delivery completes.
        if(!show){if(IsVisible&&(down||!available||(DateTime.UtcNow-releaseAt).TotalMilliseconds>=150))Hide();return;}
        if(!GetClientRect(target,out var rect))return;
        var origin=new PixelPoint();ClientToScreen(target,ref origin);
        if(!IsVisible)Show();
        SetWindowPos(handle,new IntPtr(-1),origin.X,origin.Y,rect.Right-rect.Left,rect.Bottom-rect.Top,0x0010|0x0040);
    }
    async Task CompleteAddress(Task<string?> capture,int generation)
    {
        await Task.Delay(200);
        if(await Task.WhenAny(capture,Task.Delay(600))!=capture)return;
        string? value=await capture;
        if(value==null||disposed||generation!=addressGesture||!IsWindowEnabled(target))return;
        addressGesture++;Hide();gesture.Cancel();report?.Invoke("address-drop");dropped(new([],value));
    }
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();Close();}
    [StructLayout(LayoutKind.Sequential)]struct PixelPoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct PixelRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out PixelPoint point);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flag);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out PixelRect rect);
    [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref PixelPoint point);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
}

public sealed class DropGesture
{
    bool pressed,external,canceled;
    int startX,startY;
    public void Cancel()=>canceled=true;
    public bool Update(bool down,bool over,int x,int y,bool available,bool escape=false)
    {
        if(!down){pressed=external=canceled=false;return false;}
        if(!pressed){pressed=true;external=!over;startX=x;startY=y;}
        if(escape||!available)canceled=true;
        return !canceled&&external&&over&&(Math.Abs(x-startX)>8||Math.Abs(y-startY)>8);
    }
}
