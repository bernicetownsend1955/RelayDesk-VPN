using System.IO;
using System.Windows;

namespace RelayDesk;

public partial class Workspace
{
    void RefreshSetup()
    {
        SetupBanner.Visibility = File.Exists(store.Settings.Engines.GetValueOrDefault("sing-box", "")) && File.Exists(store.Settings.ZapretExe) ? Visibility.Collapsed : Visibility.Visible;
    }
    void SetupComponentsClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        Tabs.SelectedIndex = 6;
        await CheckUpdates();
    });
    void InstallRequiredComponentsClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        GuardUpdate();
        await CheckUpdates();
        if (!File.Exists(store.Settings.Engines.GetValueOrDefault("sing-box", "")) && singUpdate?.Available == true) await Install(singUpdate);
        if (!File.Exists(store.Settings.ZapretExe) && zapretUpdate?.Available == true) await Install(zapretUpdate);
        RefreshSetup();
    });
}
