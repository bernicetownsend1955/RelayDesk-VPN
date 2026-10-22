using System.Windows;
using System.Windows.Controls;
namespace RelayDesk;

public sealed class BatchImportWindow : ThemedWindow
{
    public List<Profile> Chosen { get; } = new();
    public BatchImportWindow(ImportBatch batch)
    {
        Title = "Импорт настроек приложения"; Width = 700; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var title = new TextBlock { Text = $"Профилей: {batch.Profiles.Count}. Пропущено: {batch.Warnings.Count}.", FontSize = 21, Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var cores = new TextBlock { Text = "Ядра: " + string.Join(", ",batch.Profiles.GroupBy(p => p.Core).Select(g => g.Key + " — " + g.Count())) + ". Пути к ядрам настраиваются во вкладке «Ядра». Профили Xray используют HTTP-прокси; TUN sing-box к ним не применяется.", Style = (Style)FindResource("Caption"), Margin = new Thickness(0,0,0,12) }; DockPanel.SetDock(cores,Dock.Top); root.Children.Add(cores);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }; actions.Children.Add(cancel);
        var add = new Button { Content = "Импортировать отмеченные", Style = (Style)FindResource("Primary") }; actions.Children.Add(add);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll); var panel = new StackPanel(); scroll.Content = panel;
        var rows = new List<(CheckBox Check, Profile Profile)>();
        foreach (var p in batch.Profiles) { var check = new CheckBox { Content = p.Name + " / " + p.Core + " / " + p.Protocol + " / " + p.Host + ":" + p.Port, IsChecked = true }; panel.Children.Add(check); rows.Add((check, p)); }
        foreach (var warning in batch.Warnings) panel.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        add.Click += (_, _) => { Chosen.Clear(); Chosen.AddRange(rows.Where(x => x.Check.IsChecked == true).Select(x => x.Profile)); if (Chosen.Count == 0) { MessageBox.Show(this, "Отметь хотя бы один профиль."); return; } DialogResult = true; };
    }
}
