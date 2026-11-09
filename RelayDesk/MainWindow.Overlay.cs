using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace RelayDesk;
public partial class MainWindow
{
    TrafficOverlayWindow? trafficOverlay;
    readonly VpnTrafficReader trafficReader=new();
    readonly TrafficMeter trafficMeter=new();
    readonly DispatcherTimer trafficTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly DispatcherTimer overlaySaveTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    bool loadingOverlay,readingTraffic;
    int trafficPid;
    static readonly string[] OverlayCorners=["bottom-right","bottom-left","top-right","top-left","custom"];
    void InitializeOverlay()
    {
        loadingOverlay=true;var p=backend.ModernStore.Settings;
        OverlayCorner.ItemsSource=new[]{"Справа снизу","Слева снизу","Справа сверху","Слева сверху","Свободное положение"};OverlayCorner.SelectedIndex=Math.Max(0,Array.IndexOf(OverlayCorners,p.OverlayCorner));
        OverlayChoice.IsChecked=p.TrafficOverlay;OverlayOpacity.Value=Math.Clamp(p.OverlayOpacity*100,30,100);OverlaySize.Value=Math.Clamp(p.OverlayTextSize,12,24);UpdateOverlayLabels();loadingOverlay=false;
        trafficTimer.Tick+=async(_,_)=>await RefreshTraffic();overlaySaveTimer.Tick+=(_,_)=>{overlaySaveTimer.Stop();Save();};
        Loaded+=(_,_)=>SyncOverlay();Closed+=(_,_)=>{trafficTimer.Stop();if(overlaySaveTimer.IsEnabled)Save();overlaySaveTimer.Stop();trafficOverlay?.Close();trafficOverlay=null;trafficReader.Dispose();};
    }
    void UpdateOverlayLabels(){OverlayOpacityLabel.Text=OverlayOpacity.Value.ToString("0")+"%";OverlaySizeLabel.Text=OverlaySize.Value.ToString("0");}
    void OverlayChanged(object sender,RoutedEventArgs e)
    {
        if(!initialized||loadingOverlay)return;var p=backend.ModernStore.Settings;
        p.TrafficOverlay=OverlayChoice.IsChecked==true;p.OverlayOpacity=OverlayOpacity.Value/100;p.OverlayTextSize=OverlaySize.Value;
        p.OverlayCorner=OverlayCorners[Math.Clamp(OverlayCorner.SelectedIndex,0,OverlayCorners.Length-1)];
        UpdateOverlayLabels();SyncOverlay();overlaySaveTimer.Stop();overlaySaveTimer.Start();
    }
    void SyncOverlay()
    {
        if(!IsLoaded)return;
        if(!backend.ModernStore.Settings.TrafficOverlay){trafficTimer.Stop();trafficOverlay?.Close();trafficOverlay=null;trafficPid=0;trafficMeter.Reset();return;}
        if(trafficOverlay==null)
        {
            trafficOverlay=new TrafficOverlayWindow((x,y)=>
            {
                var p=backend.ModernStore.Settings;p.OverlayLeft=x;p.OverlayTop=y;p.OverlayCorner="custom";
                loadingOverlay=true;OverlayCorner.SelectedIndex=4;loadingOverlay=false;Save();
            },()=>{ShowFromTray();SelectPage(3);OverlayChoice.BringIntoView();},()=>OverlayChoice.IsChecked=false);
            trafficOverlay.Configure(backend.ModernStore.Settings);trafficOverlay.Show();
        }
        trafficOverlay.Configure(backend.ModernStore.Settings);trafficOverlay.Place(backend.ModernStore.Settings);trafficTimer.Start();
    }
    async Task RefreshTraffic()
    {
        if(readingTraffic||trafficOverlay==null||leaving)return;
        var session=backend.ModernSession;
        if(!session.ProxyRunning||session.ActiveProfile==null||session.Busy)
        {
            trafficPid=0;trafficMeter.Reset();trafficOverlay.Update(session.Busy?"Переподключение…":"VPN выключен",null,"Только VPN RelayDesk. Трафик напрямую и zapret не учитывается.");return;
        }
        var pid=session.Proxy!.Id;var config=session.ActiveRuntimeConfig;var profile=session.ActiveProfile;
        if(pid!=trafficPid){trafficPid=pid;trafficMeter.Reset();trafficOverlay.Update(profile.Name,null,"Первый замер…");}
        readingTraffic=true;
        try
        {
            var counters=await trafficReader.Read(profile.Core,config,lifetime.Token);
            if(leaving||trafficOverlay==null||!session.ProxyRunning||session.Proxy?.Id!=pid||session.Busy||trafficPid!=pid)return;
            var rate=trafficMeter.Sample(counters,(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
            trafficOverlay.Update(profile.Name,rate,profile.Core=="Xray"?"Счётчики VPN-выходов Xray. Трафик напрямую исключён.":"Скорость активных VPN-соединений. Короткие соединения между замерами могут быть пропущены. Трафик напрямую исключён.");
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception ex){trafficMeter.Reset();if(!leaving&&trafficOverlay!=null&&session.Proxy?.Id==pid)trafficOverlay.Update(profile.Name+" · нет данных",null,ProgramJournal.Clean(ex.Message));}
        finally{readingTraffic=false;}
    }
}
