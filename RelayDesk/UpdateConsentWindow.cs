using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace RelayDesk;

public sealed class UpdateConsentWindow : ThemedWindow
{
    public UpdateConsentWindow(string component, string version)
    {
        Title = "Разрешение на обновление"; Width = 560; Height = 320; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
        var heading = new TextBlock { Text = "Обновить " + component + "?", FontSize = 23, Margin = new Thickness(0, 0, 0, 18) }; DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var decline = new Button { Content = "Отказаться", IsCancel = true, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) }; AutomationProperties.SetAutomationId(decline, "DeclineUpdate"); actions.Children.Add(decline);
        decline.Click += (_, _) => DialogResult = false;
        var allow = new Button { Content = "Разрешить обновление", Style = (Style)FindResource("Primary") }; AutomationProperties.SetAutomationId(allow, "ApproveUpdate"); actions.Children.Add(allow);
        allow.Click += (_, _) => DialogResult = true;
        panel.Children.Add(new TextBlock { Text = $"Версия / сборка: {version}\n\nПакет будет загружен из официального источника в отдельную папку. Прежние файлы сохранятся. VPN и zapret после обновления не запускаются.", TextWrapping = TextWrapping.Wrap });
    }
}
