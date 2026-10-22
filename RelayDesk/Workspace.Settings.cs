using System.Windows;

namespace RelayDesk;

public partial class Workspace
{
    void InitializeExperimental()
    {
        ExperimentalFeatures.IsChecked = store.Settings.ExperimentalDiagnostics;
        ExperimentalDiagnostics.Visibility = store.Settings.ExperimentalDiagnostics ? Visibility.Visible : Visibility.Collapsed;
    }
    void ExperimentalFeaturesClick(object sender, RoutedEventArgs e)
    {
        if (store == null) return;
        Safe(() => {
        store.Settings.ExperimentalDiagnostics = ExperimentalFeatures.IsChecked == true;
        store.Save();
        ExperimentalDiagnostics.Visibility = store.Settings.ExperimentalDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        if (!store.Settings.ExperimentalDiagnostics) ExperimentalDiagnostics.IsExpanded = false;
        return Task.CompletedTask;
        });
    }
    void TunModeChanged(object sender, RoutedEventArgs e) => UpdateTunHint();
    void UpdateTunHint()
    {
        bool enabled = Tun.IsChecked == true;
        TunModeHint.Text = selected?.Managed == true
            ? enabled ? "Включён в профиле. Применится после сохранения и следующего подключения." : "Выключен. Для подключения приложения нужен HTTP / SOCKS или системный прокси."
            : selected?.Core == "sing-box"
                ? enabled ? "TUN включён в исходном JSON. Его параметры меняются в конфигурации." : "В исходном JSON нет TUN. При разделении Discord RelayDesk добавит его в рабочую копию."
                : "Этот переключатель доступен для профилей sing-box, импортированных из ссылки.";
        if (store.Settings.ConditionalRouting || store.Settings.DiscordSplit)
            TunModeHint.Text += " Для разделения Discord TUN включается в рабочей копии; исходный JSON сохраняется.";
    }
    void EditTunJsonClick(object sender, RoutedEventArgs e)
    {
        Tabs.SelectedIndex = 2;
        ConfigEditor.Focus();
        var index = ConfigEditor.Text.IndexOf("\"tun\"", StringComparison.Ordinal);
        if (index >= 0) { ConfigEditor.Select(index, 5); ConfigEditor.ScrollToLine(ConfigEditor.GetLineIndexFromCharacterIndex(index)); }
    }
}
