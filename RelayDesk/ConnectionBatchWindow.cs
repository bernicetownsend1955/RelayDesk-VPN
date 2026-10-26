using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public sealed class ConnectionBatchWindow:ThemedWindow
{
    public IReadOnlyList<RouteRule> Result {get;private set;}=[];
    readonly ComboBox scope=new(){ItemsSource=new[]{"Точные домены / IP","Приложения","Домены и поддомены / IP"},SelectedIndex=0};
    readonly ComboBox route=new(){ItemsSource=RouteRules.Routes,SelectedIndex=0};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0)};
    readonly TextBox preview=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,14,0,0)};
    readonly Button save=new(){Content="Сохранить правила"};
    readonly DemoConnection[] connections;
    readonly IReadOnlyList<RouteRule> existing;
    public ConnectionBatchWindow(DemoConnection[] connections,IReadOnlyList<RouteRule> existing)
    {
        this.connections=connections;this.existing=existing;
        Title="Маршрут выбранных соединений";Width=720;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(24)};Content=root;
        var buttons=new WrapPanel{Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        save.Click+=(_,_)=>{if(Result.Count>0)DialogResult=true;};buttons.Children.Add(save);
        var cancel=new Button{Content="Отмена",IsCancel=true,Margin=new Thickness(10,0,0,0)};buttons.Children.Add(cancel);
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text=$"Выбрано соединений: {connections.Length}",FontSize=22,FontWeight=FontWeights.SemiBold});
        header.Children.Add(new TextBlock{Text="Создать правила для",Margin=new Thickness(0,16,0,6)});header.Children.Add(scope);
        header.Children.Add(new TextBlock{Text="Маршрут",Margin=new Thickness(0,16,0,6)});header.Children.Add(route);header.Children.Add(status);
        header.Children.Add(new TextBlock{Text="Точные совпадения обновляются. Новые правила ставятся выше существующих. После сохранения работающий профиль нужно переподключить кнопкой «Применить правила».",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        root.Children.Add(preview);
        scope.SelectionChanged+=(_,_)=>Update();route.SelectionChanged+=(_,_)=>Update();Update();
    }
    void Update()
    {
        try
        {
            Result=ConnectionBatch.Create(connections,scope.SelectedIndex,route.SelectedItem?.ToString()??RouteRules.Routes[0],existing);
            int overlaps=Result.Count(r=>RuleMatching.Overlaps(r,existing).Count>0);
            status.Text=$"Будет сохранено правил: {Result.Count}. Пересекаются с существующими: {overlaps}.";
            preview.Text=string.Join(Environment.NewLine,Result.Select(r=>$"{RouteRules.Label(r.Kind)}: {r.Value} → {r.Route}"));save.IsEnabled=Result.Count>0;
        }
        catch(Exception ex){Result=[];status.Text=ex.Message;preview.Clear();save.IsEnabled=false;}
    }
}
