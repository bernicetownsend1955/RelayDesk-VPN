using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RelayDesk;

public sealed class DropRouteWindow : ThemedWindow
{
    public string SelectedRoute {get;private set;}="Через VPN";
    public bool ApplyNow {get;private set;}
    public DropRouteWindow(RouteRule target,IReadOnlyList<RuleOverlap> overlaps,bool canApply)
    {
        Title="Маршрут подключения";Width=560;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text=target.Name,FontSize=24,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        panel.Children.Add(new TextBlock{Text=target.Value,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)Resources["Muted"],Margin=new Thickness(0,0,0,20)});
        var routes=new StackPanel();panel.Children.Add(routes);
        SelectedRoute=target.Route;
        foreach(var route in RouteRules.Routes)
        {
            string description=route switch{"Через VPN"=>"Через выбранный VPN-сервер.","Через zapret"=>"Напрямую с обработкой zapret, когда он включён.",_=>"Прямое подключение."};
            var option=new RadioButton{Content=new TextBlock{Text=route+" — "+description,TextWrapping=TextWrapping.Wrap,MaxWidth=460,FontSize=14,Foreground=(Brush)Resources["Ink"]},Tag=route,GroupName="drop-route",IsChecked=route==target.Route,Foreground=(Brush)Resources["Ink"],Margin=new Thickness(0,0,0,14)};
            System.Windows.Automation.AutomationProperties.SetName(option,route);
            option.Checked+=(_,_)=>SelectedRoute=route;routes.Children.Add(option);
        }
        string message=overlaps.Count>0?"Пересекается с: "+string.Join(", ",overlaps.Take(3).Select(o=>o.Existing.Value))+". Сохранённое правило будет проверяться первым.":"Правило сохранится для всех профилей и групп.";
        if(target.Kind=="domain_suffix")message+=" Действует для домена и его поддоменов.";
        panel.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)Resources["Muted"],Margin=new Thickness(0,4,0,14)});
        panel.Children.Add(new TextBlock{Text=(target.Kind.StartsWith("process_")?"EXE требует sing-box с TUN. ":"")+"Обработка zapret зависит от его фильтра.",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=(Brush)Resources["Muted"]});
        var actions=new WrapPanel{Margin=new Thickness(0,22,0,0)};panel.Children.Add(actions);
        void Button(string label,bool apply){var button=new Button{Content=label,Margin=new Thickness(0,0,8,8),IsDefault=!apply};button.Click+=(_,_)=>{ApplyNow=apply;DialogResult=true;};actions.Children.Add(button);}
        Button("Сохранить",false);if(canApply)Button("Сохранить и применить",true);
        actions.Children.Add(new Button{Content="Отмена",IsCancel=true,Margin=new Thickness(0,0,0,8)});
    }
}
