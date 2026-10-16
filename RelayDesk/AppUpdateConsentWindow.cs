using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace RelayDesk;

public sealed class AppUpdateConsentWindow : ThemedWindow
{
    public AppUpdateConsentWindow(AppRelease release)
    {
        Title = "Обновление RelayDesk"; Width = 620; Height = 490; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
        var heading = new TextBlock { Text = "RelayDesk " + release.Version, FontSize = 25, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var warning = new TextBlock { Text = "Установщик загрузится с GitHub и пройдёт проверку SHA-256. Затем RelayDesk закроется: VPN и zapret будут остановлены. После установки приложение откроется снова.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18) };
        DockPanel.SetDock(warning, Dock.Top); panel.Children.Add(warning);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var later = new Button { Content = "Позже", IsCancel = true, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetAutomationId(later, "DeclineAppUpdate"); later.Click += (_, _) => DialogResult = false; actions.Children.Add(later);
        var install = new Button { Content = "Скачать и установить", Style = (Style)FindResource("Primary") };
        AutomationProperties.SetAutomationId(install, "ApproveAppUpdate"); install.Click += (_, _) => DialogResult = true; actions.Children.Add(install);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = release.Notes.Length > 0 ? release.Notes : "Описание изменений не опубликовано.", TextWrapping = TextWrapping.Wrap } });
    }
}
