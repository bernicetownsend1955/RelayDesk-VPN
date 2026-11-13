using System.Windows;
using System.Windows.Controls;
namespace RelayDesk;

public sealed class BatchImportWindow : ThemedWindow
{
    public List<Profile> Chosen { get; } = new();
    public string Group {get;private set;}="";
    public BatchImportWindow(ImportBatch batch,string? group=null)
    {
        Title = "Импорт настроек приложения"; Width = 700; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var title = new TextBlock { Text = $"Профилей: {batch.Profiles.Count}. Пропущено: {batch.Warnings.Count}.", FontSize = 21, Margin = new Thickness(0, 0, 0, 16) }; DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var cores = new TextBlock { Text = "Ядра: " + string.Join(", ",batch.Profiles.GroupBy(p => p.Core).Select(g => g.Key + " — " + g.Count())) + ". Ядро выбирается по формату профиля. Недостающие компоненты доступны в обновлениях. Xray использует системный прокси.", Style = (Style)FindResource("Caption"), Margin = new Thickness(0,0,0,12) }; DockPanel.SetDock(cores,Dock.Top); root.Children.Add(cores);
        TextBox? groupField=null;
        if(group!=null)
        {var form=new StackPanel{Margin=new Thickness(0,0,0,12)};form.Children.Add(new TextBlock{Text="Группа подписки",Margin=new Thickness(0,0,0,6)});groupField=new TextBox{Text=group};form.Children.Add(groupField);DockPanel.SetDock(form,Dock.Top);root.Children.Add(form);}
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }; actions.Children.Add(cancel);
        var add = new Button { Content = "Импортировать отмеченные", Style = (Style)FindResource("Primary") }; actions.Children.Add(add);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll); var panel = new StackPanel(); scroll.Content = panel;
        var rows = new List<(CheckBox Check, Profile Profile)>();
        foreach (var p in batch.Profiles) { var check = new CheckBox { Content = p.Name + " / " + p.Core + " / " + p.Protocol + " / " + p.Host + ":" + p.Port, IsChecked = true }; panel.Children.Add(check); rows.Add((check, p)); }
        foreach (var warning in batch.Warnings) panel.Children.Add(new TextBlock { Text = warning, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        add.Click += (_, _) => { Chosen.Clear(); Chosen.AddRange(rows.Where(x => x.Check.IsChecked == true).Select(x => x.Profile)); if (Chosen.Count == 0) { MessageBox.Show(this, "Отметь хотя бы один профиль."); return; } Group=groupField?.Text.Trim()??"";if(groupField!=null&&Group.Length is <1 or >100){MessageBox.Show(this,"Укажи название группы от 1 до 100 символов.");return;} DialogResult = true; };
    }
}
