using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public sealed class ConnectionRouteWindow : ThemedWindow
{
    public QuickRoute? Rule { get; private set; }
    public bool Remove { get; private set; }
    public bool ApplyNow { get; private set; }
    public ConnectionRouteWindow(Profile profile, string kind, string value, string[] proxyTags, bool canApply)
    {
        Title = "Маршрут соединения"; Width = 610; Height = 470; MaxHeight = Math.Max(400,SystemParameters.WorkArea.Height - 40); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(24) }; Content = panel;
        var actions = new WrapPanel { Margin = new Thickness(0,18,0,0) }; DockPanel.SetDock(actions,Dock.Bottom); panel.Children.Add(actions);
        var content = new StackPanel(); panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content });
        content.Children.Add(new TextBlock { Text = kind.StartsWith("process_") ? "Маршрут приложения" : "Маршрут адреса", FontSize = 23, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,8) });
        content.Children.Add(new TextBlock { Text = "Профиль: " + profile.Name, Style = (Style)FindResource("Caption") });
        var existing = profile.QuickRoutes.FirstOrDefault(r => r.Value == value && (r.Kind == kind || kind == "domain" && r.Kind == "domain_suffix"));
        var choices = new[] { "Напрямую" }.Concat(proxyTags.Select(t => "VPN / " + t)).ToArray();
        var choice = new ComboBox { ItemsSource = choices, Margin = new Thickness(0,16,0,0), SelectedIndex = existing != null && !existing.Direct ? Array.IndexOf(proxyTags, existing.Outbound) + 1 : 0 };
        content.Children.Add(choice);
        var suffix = new CheckBox { Content = "Включая поддомены", IsChecked = existing?.Kind == "domain_suffix", Visibility = kind == "domain" ? Visibility.Visible : Visibility.Collapsed };
        content.Children.Add(suffix);
        content.Children.Add(new TextBlock { Text = "Правило сохраняется для этого профиля. «Применить» переподключит VPN и прервёт текущие соединения. Для правил приложений нужен TUN. «Напрямую» не гарантирует обработку zapret: она зависит от его фильтра.", TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Caption"), Margin = new Thickness(0,14,0,0) });
        void Save(bool apply)
        {
            if (choice.SelectedIndex < 0) return;
            Rule = new(kind == "domain" && suffix.IsChecked == true ? "domain_suffix" : kind, value, choice.SelectedIndex == 0 ? "@direct" : proxyTags[choice.SelectedIndex - 1], choice.SelectedIndex == 0);
            ApplyNow = apply; DialogResult = true;
        }
        var save = new Button { Content = "Сохранить правило", Style = (Style)FindResource("Primary") }; save.Click += (_,_) => Save(false); actions.Children.Add(save);
        var apply = new Button { Content = "Сохранить и применить", IsEnabled = canApply, Margin = new Thickness(8,0,0,0) }; apply.Click += (_,_) => Save(true); actions.Children.Add(apply);
        var remove = new Button { Content = "Удалить правило", IsEnabled = existing != null, Margin = new Thickness(0,8,8,0) }; remove.Click += (_,_) => { Remove = true; DialogResult = true; }; actions.Children.Add(remove);
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0,8,0,0) }; actions.Children.Add(cancel);
    }
}
