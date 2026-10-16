using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public sealed class ImportWindow : ThemedWindow
{
    public ImportWindow(ImportResult result)
    {
        Title = "Предпросмотр импорта"; Width = 580; Height = 490; MinWidth = 480; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
        var heading = new TextBlock { Text = "Добавить подключение", FontSize = 24, Margin = new Thickness(0, 0, 0, 18) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }; actions.Children.Add(cancel);
        var add = new Button { Content = "Добавить профиль", IsDefault = true, Style = (Style)FindResource("Primary") }; actions.Children.Add(add);
        var fields = new StackPanel(); root.Children.Add(fields);
        fields.Children.Add(new TextBlock { Text = result.Summary, TextWrapping = TextWrapping.Wrap });
        fields.Children.Add(new TextBlock { Text = "Название профиля", Margin = new Thickness(0, 20, 0, 6) });
        var name = new TextBox { Text = result.Profile.Name }; fields.Children.Add(name);
        ComboBox? choice = null;
        if (result.NeedsCoreChoice)
        {
            fields.Children.Add(new TextBlock { Text = "Выбери ядро для JSON", Margin = new Thickness(0, 16, 0, 6) });
            choice = new ComboBox { ItemsSource = new[] { "sing-box", "Xray", "mihomo" } }; fields.Children.Add(choice);
        }
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(this, "Укажи название профиля."); return; }
            if (choice != null && choice.SelectedItem == null) { MessageBox.Show(this, "Выбери ядро."); return; }
            result.Profile.Name = name.Text.Trim();
            if (choice != null) result.Profile.Core = choice.SelectedItem!.ToString()!;
            DialogResult = true;
        };
    }
}
