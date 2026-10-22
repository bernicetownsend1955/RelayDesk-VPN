using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public sealed class SubscriptionGroupsWindow : ThemedWindow
{
    public List<string> Groups { get; }
    public Dictionary<string, string> Assignments { get; }
    public Dictionary<string, string> GroupChanges { get; }
    readonly List<Profile> profiles;
    readonly ComboBox group = new();
    readonly TextBox name = new();
    readonly StackPanel rows = new();
    string current = "";
    bool loading;

    public SubscriptionGroupsWindow(List<Profile> profiles, List<string> groups, string selected)
    {
        this.profiles = profiles;
        Groups = groups.Concat(profiles.Select(p => p.SubscriptionGroup)).Where(g => g.Length > 0).Distinct().OrderBy(g => g).ToList();
        Assignments = profiles.ToDictionary(p => p.Id, p => p.SubscriptionGroup);
        GroupChanges = Groups.ToDictionary(g => g,g => g);
        Title = "Группы подписок"; Width = 660; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var actions = new WrapPanel { Margin = new Thickness(0,16,0,0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var save = new Button { Content = "Сохранить", Style = (Style)FindResource("Primary") }; save.Click += (_,_) => { Capture(); DialogResult = true; }; actions.Children.Add(save);
        actions.Children.Add(new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8,0,0,0) });
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Группы подписок", FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Выбери группу и отметь её профили. Перенос между группами не меняет подключение или JSON.", Style = (Style)FindResource("Caption"), Margin = new Thickness(0,8,0,16) });
        header.Children.Add(group); group.SelectionChanged += (_,_) => { if (loading) return; Capture(); current = group.SelectedItem?.ToString() ?? ""; name.Text = current; Render(); };
        header.Children.Add(new TextBlock { Text = "Название группы", Margin = new Thickness(0,12,0,6) }); header.Children.Add(name);
        var management = new WrapPanel { Margin = new Thickness(0,10,0,14) }; header.Children.Add(management);
        Add("Создать", () => { var value = GroupName(); if (Groups.Contains(value)) throw new InvalidOperationException("Группа с таким названием уже есть."); Capture(); Groups.Add(value); Select(value); });
        Add("Переименовать", () => { RequireGroup(); var value = GroupName(); if (value != current && Groups.Contains(value)) throw new InvalidOperationException("Группа с таким названием уже есть."); Capture(); foreach (var id in Assignments.Keys.ToArray()) if (Assignments[id] == current) Assignments[id] = value; foreach (var key in GroupChanges.Keys.ToArray()) if (GroupChanges[key] == current) GroupChanges[key] = value; Groups[Groups.IndexOf(current)] = value; Select(value); });
        Add("Удалить группу", () => { RequireGroup(); Capture(); Groups.Remove(current); foreach (var key in GroupChanges.Keys.ToArray()) if (GroupChanges[key] == current) GroupChanges[key] = ""; foreach (var id in Assignments.Keys.ToArray()) if (Assignments[id] == current) Assignments[id] = ""; Select(Groups.FirstOrDefault() ?? ""); });
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Select(Groups.Contains(selected) ? selected : Groups.FirstOrDefault() ?? "");

        void Add(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0,0,8,0) };
            button.Click += (_,_) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Группы подписок"); } }; management.Children.Add(button);
        }
    }
    string GroupName() { var value = name.Text.Trim(); if (value.Length is < 1 or > 80 || value.Contains('\n') || value.Contains('\r')) throw new FormatException("Укажи название группы от 1 до 80 символов."); if (value is "Все профили" or "Без группы") throw new FormatException("Выбери другое название группы."); return value; }
    void RequireGroup() { if (current.Length == 0) throw new InvalidOperationException("Выбери группу."); }
    void Capture()
    {
        if (current.Length == 0) return;
        foreach (var check in rows.Children.OfType<CheckBox>())
        {
            var id = (string)check.Tag;
            if (check.IsChecked == true) Assignments[id] = current;
            else if (Assignments[id] == current) Assignments[id] = "";
        }
    }
    void Select(string value)
    {
        loading = true; group.ItemsSource = Groups.OrderBy(g => g).ToArray(); group.SelectedItem = value; current = value; name.Text = value; loading = false; Render();
    }
    void Render()
    {
        rows.Children.Clear();
        foreach (var p in profiles) rows.Children.Add(new CheckBox { Content = p.Name + (Assignments[p.Id].Length > 0 ? " / " + Assignments[p.Id] : ""), Tag = p.Id, IsChecked = current.Length > 0 && Assignments[p.Id] == current, IsEnabled = current.Length > 0, Margin = new Thickness(0,5,0,5) });
    }
}
