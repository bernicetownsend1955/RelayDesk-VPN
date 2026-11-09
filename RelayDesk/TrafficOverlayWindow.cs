using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace RelayDesk;
public sealed class TrafficOverlayWindow:Window
{
    readonly TextBlock name=new(){FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap};
    readonly TextBlock download=new(),upload=new();
    readonly Action<double,double> moved;
    public TrafficOverlayWindow(Action<double,double> moved,Action settings,Action hide)
    {
        this.moved=moved;Title="RelayDesk · Скорость VPN";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;SizeToContent=SizeToContent.Height;Width=244;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/ModernTheme.xaml")});
        FontFamily=new FontFamily("Segoe UI");Foreground=new SolidColorBrush(Color.FromRgb(236,238,242));UseLayoutRounding=true;
        var panel=new StackPanel();name.Margin=new Thickness(0,0,0,8);panel.Children.Add(name);panel.Children.Add(download);panel.Children.Add(upload);
        Content=new Border{Background=new SolidColorBrush(Color.FromRgb(16,19,24)),BorderBrush=new SolidColorBrush(Color.FromRgb(65,71,81)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(14,12,14,12),Child=panel};
        MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2){settings();return;}DragMove();EnsureOnScreen();moved(Left,Top);};
        var menu=new ContextMenu();var configure=new MenuItem{Header="Настроить оверлей"};configure.Click+=(_,_)=>settings();var close=new MenuItem{Header="Скрыть оверлей"};close.Click+=(_,_)=>hide();menu.Items.Add(configure);menu.Items.Add(close);ContextMenu=menu;
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;var style=GetWindowLong(handle,-20);SetWindowLong(handle,-20,style|0x08000000|0x80);};
        Update("VPN выключен",null,"Включи VPN в RelayDesk.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern int GetWindowLong(IntPtr handle,int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern int SetWindowLong(IntPtr handle,int index,int value);
    public void Configure(Preferences prefs)
    {
        Opacity=Math.Clamp(double.IsFinite(prefs.OverlayOpacity)?prefs.OverlayOpacity:.8,.3,1);
        double size=Math.Clamp(double.IsFinite(prefs.OverlayTextSize)?prefs.OverlayTextSize:16,12,24);
        name.FontSize=Math.Max(12,size-2);download.FontSize=upload.FontSize=size;Width=Math.Max(220,size*14);
        if(IsLoaded)UpdateLayout();
    }
    Rect WorkArea()
    {
        var screen=Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        return new Rect(transform.Transform(new Point(screen.Left,screen.Top)),transform.Transform(new Point(screen.Right,screen.Bottom)));
    }
    public void Place(Preferences prefs)
    {
        UpdateLayout();var area=WorkArea();var height=ActualHeight>0?ActualHeight:110;
        if(prefs.OverlayCorner=="custom"&&prefs.OverlayLeft is double x&&prefs.OverlayTop is double y&&double.IsFinite(x)&&double.IsFinite(y)){Left=x;Top=y;EnsureOnScreen();return;}
        Left=prefs.OverlayCorner.EndsWith("left",StringComparison.Ordinal)?area.Left+16:area.Right-Width-16;
        Top=prefs.OverlayCorner.StartsWith("top",StringComparison.Ordinal)?area.Top+16:area.Bottom-height-16;
        EnsureOnScreen();
    }
    public void EnsureOnScreen()
    {
        var area=WorkArea();Left=Math.Clamp(Left,area.Left,Math.Max(area.Left,area.Right-ActualWidth));Top=Math.Clamp(Top,area.Top,Math.Max(area.Top,area.Bottom-ActualHeight));
    }
    public void Update(string connection,TrafficRate? rate,string status)
    {
        name.Text=connection;download.Text="↓ "+(rate is TrafficRate current?current.Download.ToString("0.00",CultureInfo.CurrentCulture):"—")+" Мбит/с";
        upload.Text="↑ "+(rate is TrafficRate measured?measured.Upload.ToString("0.00",CultureInfo.CurrentCulture):"—")+" Мбит/с";
        ToolTip=status;System.Windows.Automation.AutomationProperties.SetName(this,connection+". "+download.Text+". "+upload.Text+". "+status);
    }
}
