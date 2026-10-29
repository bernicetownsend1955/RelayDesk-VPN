using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public partial class MainWindow
{
    void EnableSubscriptionTraffic(object sender,RoutedEventArgs e)
    {
        try{backend.ModernEnableSubscriptionTraffic();RefreshFromBackend();Toast("Системный прокси настроен для выбранного сервера. Включи VPN, затем проверь HTTPS.");}
        catch(Exception ex){Toast(ex.Message);}
    }
    void LoadZapretStrategies()
    {
        try
        {
            var p=backend.ModernStore.Settings;
            var strategies=FlowsealStrategies.Resolve(p.ZapretExe,p.StrategyList);
            if(!string.IsNullOrWhiteSpace(p.ZapretArgs)&&!strategies.Any(s=>s.Arguments==p.ZapretArgs))strategies.Insert(0,new Strategy("Текущая сохранённая стратегия",p.ZapretArgs));
            ZapretStrategyChoice.ItemsSource=strategies;
            ZapretStrategyChoice.SelectedItem=strategies.FirstOrDefault(s=>s.Arguments==p.ZapretArgs)??strategies.FirstOrDefault();
        }
        catch(Exception ex){ZapretStrategyMessage.Text=ex.Message;}
    }
    void ReloadZapretStrategies(object sender,RoutedEventArgs e)=>LoadZapretStrategies();
    void SaveZapretStrategy(object sender,RoutedEventArgs e)
    {
        try
        {
            if(ZapretStrategyChoice.SelectedItem is not Strategy strategy)throw new InvalidOperationException("Выбери стратегию из списка.");
            backend.ModernSaveZapretStrategy(strategy);RefreshFromBackend();
            ZapretStrategyMessage.Text="Сохранена: "+strategy.Name+". Автоподбор отключён. Стратегия будет использована при включении zapret.";
        }
        catch(Exception ex){ZapretStrategyMessage.Text=ex.Message;}
    }
    static readonly int[] AdvancedTabs=[1,2,3,4,5,6,8];
    bool syncingSection;
    void SyncModernSection(int tab)
    {
        if(AdvancedHost.Visibility!=Visibility.Visible)return;
        if(tab==0||tab==7){SelectPage(tab==0?0:4);return;}
        syncingSection=true;try{AdvancedSection.SelectedIndex=Array.IndexOf(AdvancedTabs,tab);}finally{syncingSection=false;}
        Dispatcher.BeginInvoke(()=>{if(AdvancedHost.Visibility==Visibility.Visible)backend.ModernRestyle();});
    }
    void AdvancedSectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!initialized||syncingSection||AdvancedSection.SelectedIndex<0)return;
        backend.ModernOpen(AdvancedTabs[AdvancedSection.SelectedIndex]);backend.ModernRestyle();
    }
    void LoadModernStartup()
    {
        var p=backend.ModernStore.Settings;
        StartupEnabled.IsChecked=p.WindowsStartup;StartupVpn.IsChecked=p.StartupVpn;StartupZapret.IsChecked=p.StartupZapret;
        var profiles=backend.ModernStore.Profiles.Where(p=>p.Core!="zapret").ToArray();
        StartupProfile.ItemsSource=profiles;
        StartupProfile.SelectedItem=profiles.FirstOrDefault(profile=>profile.Id==p.StartupProfileId)??backend.ModernProfile??profiles.FirstOrDefault();
        StartupMessage.Text=backend.StartupStatus.Text;
    }
    void SaveModernStartup(object sender,RoutedEventArgs e)
    {
        try
        {
            backend.ModernSaveStartup(StartupEnabled.IsChecked==true,StartupVpn.IsChecked==true,StartupZapret.IsChecked==true,StartupProfile.SelectedItem as Profile);
            StartupMessage.Text=backend.StartupStatus.Text;Toast("Параметры автозапуска сохранены.");
        }
        catch(Exception ex){StartupMessage.Text=ex.Message;Toast(ex.Message);}
    }
}
