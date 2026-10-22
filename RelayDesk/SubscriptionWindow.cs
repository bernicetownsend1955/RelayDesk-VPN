using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public sealed class SubscriptionWindow : ThemedWindow
{
    public SubscriptionSource? Source { get; private set; }
    public SubscriptionWindow(IEnumerable<SubscriptionSource> sources, string initialUrl, string initialGroup)
    {
        var existing = sources.ToArray();
        Title = "Подписки"; Width = 640; Height = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var actions = new WrapPanel { Margin = new Thickness(0,18,0,0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var load = new Button { Content = "Загрузить предпросмотр", Style = (Style)FindResource("Primary") }; actions.Children.Add(load);
        actions.Children.Add(new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8,0,0,0) });
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = "Подписки", FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Выбери сохранённую подписку для обновления или вставь новую ссылку. Перед импортом можно выбрать серверы.", Style = (Style)FindResource("Caption"), Margin = new Thickness(0,8,0,16) });
        var saved = new ComboBox { ItemsSource = existing, Margin = new Thickness(0,0,0,14) }; panel.Children.Add(saved);
        panel.Children.Add(new TextBlock { Text = "HTTPS или happ://add/…", Style = (Style)FindResource("Label") });
        var address = new TextBox { Text = initialUrl, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(address);
        panel.Children.Add(new TextBlock { Text = "Группа для новых серверов", Style = (Style)FindResource("Label") });
        var name = new TextBox { Text = initialGroup }; panel.Children.Add(name);
        var device = new CheckBox { Content = "Отправлять постоянный идентификатор устройства (HWID)", IsChecked = true }; panel.Children.Add(device);
        panel.Children.Add(new TextBlock { Text = "Нужен некоторым провайдерам. Добавление подписки может занять место в лимите устройств. RelayDesk сохраняет один идентификатор и не меняет его при обновлениях.", Style = (Style)FindResource("Caption"), Margin = new Thickness(0,8,0,0) });
        panel.Children.Add(new TextBlock { Text = "Ссылка сохраняется локально. Обновление не переподключает работающий VPN. Отсутствующие в ответе серверы автоматически не удаляются.", Style = (Style)FindResource("Caption"), Margin = new Thickness(0,14,0,0) });
        saved.SelectionChanged += (_,_) => { if (saved.SelectedItem is SubscriptionSource s) { address.Text = s.Url; name.Text = s.Group; device.IsChecked = s.SendDeviceId; } };
        if (initialUrl.Length == 0 && existing.Length > 0) saved.SelectedItem = existing.FirstOrDefault(s => s.Group == initialGroup) ?? existing[0];
        load.Click += (_,_) =>
        {
            try
            {
                var url = Subscriptions.Address(address.Text).AbsoluteUri;
                var group = name.Text.Trim();
                if (group.Length > 80 || group.Contains('\n') || group.Contains('\r') || group is "Все профили" or "Без группы") throw new FormatException("Выбери другое название группы, до 80 символов.");
                var old = existing.FirstOrDefault(s => s.Url == url);
                Source = new SubscriptionSource { Id = old?.Id ?? Guid.NewGuid().ToString("N"), Url = url, Group = group, SendDeviceId = device.IsChecked == true, UpdatedAt = old?.UpdatedAt };
                DialogResult = true;
            }
            catch (FormatException ex) { MessageBox.Show(this, ex.Message, "Подписки"); }
        };
    }
}
