using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace RelayDesk;
public partial class MainWindow
{
    readonly DispatcherTimer quietTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly Stopwatch vpnElapsed=new(),zapretElapsed=new();
    DateTime lastActivity=DateTime.UtcNow,awaySince=DateTime.UtcNow;
    bool quiet,wasVpn,wasZapret;
    void InitializeQuietMode()
    {
        QuietChoice.IsChecked=state.QuietMode;
        PreviewMouseDown+=(_,e)=>{RegisterActivity();if(quiet){WakeQuietMode();e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{RegisterActivity();if(quiet){WakeQuietMode();e.Handled=true;}};
        PreviewMouseWheel+=(_,e)=>{RegisterActivity();if(quiet){WakeQuietMode();e.Handled=true;}};
        PreviewMouseMove+=(_,_)=>{if(!quiet&&IsActive)RegisterActivity();};
        Activated+=(_,_)=>RegisterActivity();Deactivated+=(_,_)=>awaySince=DateTime.UtcNow;
        Loaded+=(_,_)=>{RegisterActivity();quietTimer.Start();};
        Closed+=(_,_)=>quietTimer.Stop();
        quietTimer.Tick+=(_,_)=>
        {
            UpdateConnectionClocks();
            if(!state.QuietMode||rulesDialogOpen||!IsLoaded||WindowState==WindowState.Minimized||vpnBusy||zapretBusy)return;
            bool inactive=IsActive?(DateTime.UtcNow-lastActivity).TotalSeconds>=30:(DateTime.UtcNow-awaySince).TotalSeconds>=1.5;
            if(inactive&&!quiet)EnterQuietMode();
        };
    }
    void RegisterActivity()=>lastActivity=DateTime.UtcNow;
    static string Duration(Stopwatch watch)=>$"{(int)watch.Elapsed.TotalHours:00}:{watch.Elapsed.Minutes:00}:{watch.Elapsed.Seconds:00}";
    void UpdateConnectionClocks()
    {
        if(vpn&&!wasVpn)vpnElapsed.Restart();else if(!vpn&&wasVpn)vpnElapsed.Reset();
        if(zapret&&!wasZapret)zapretElapsed.Restart();else if(!zapret&&wasZapret)zapretElapsed.Reset();
        wasVpn=vpn;wasZapret=zapret;
        QuietVpnRow.Visibility=vpn?Visibility.Visible:Visibility.Collapsed;
        QuietZapretRow.Visibility=zapret?Visibility.Visible:Visibility.Collapsed;
        QuietOff.Visibility=!vpn&&!zapret?Visibility.Visible:Visibility.Collapsed;
        QuietVpnTitle.Text="VPN · "+(backend.ModernSession.ActiveProfile?.Name??"ядро запущено");
        QuietVpnTime.Text=CoreDuration(backend.ModernSession.Proxy);QuietZapretTime.Text=CoreDuration(backend.ModernSession.Zapret);
        RefreshTrayStatus();
    }
    static string CoreDuration(System.Diagnostics.Process? process)
    {
        try{var elapsed=DateTime.UtcNow-process!.StartTime.ToUniversalTime();return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";}catch{return "00:00:00";}
    }
    void EnterQuietMode()
    {
        quiet=true;UpdateConnectionClocks();
        QuietOverlay.Visibility=Visibility.Visible;QuietOverlay.Opacity=0;Shell.IsHitTestVisible=false;
        BrandTitle.AnimationEnabled=false;
        App.Animate(Shell,OpacityProperty,.025,550);App.Animate(QuietOverlay,OpacityProperty,1,550);
    }
    void WakeQuietMode()
    {
        RegisterActivity();if(!quiet)return;
        quiet=false;QuietOverlay.BeginAnimation(OpacityProperty,null);QuietOverlay.Visibility=Visibility.Collapsed;
        Shell.IsHitTestVisible=true;BrandTitle.AnimationEnabled=App.Motion;App.Animate(Shell,OpacityProperty,1,220);
    }
    void QuietModeChanged(object sender,RoutedEventArgs e)
    {
        if(!initialized)return;state.QuietMode=QuietChoice.IsChecked==true;
        if(!state.QuietMode)WakeQuietMode();RegisterActivity();Save();
    }
}
