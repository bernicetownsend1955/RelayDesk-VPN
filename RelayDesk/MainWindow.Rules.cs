using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RelayDesk;
public partial class MainWindow
{
    RouteRule? deletedRule;
    int deletedIndex;
    bool rulesDialogOpen;
    void RefreshRoutes()
    {
        if(AppRoutes==null)return;
        refreshingRoutes=true;HomeRoutes.Children.Clear();AppRoutes.Children.Clear();
        foreach(var rule in state.Rules.Where(r=>r.Kind!="default").Take(2).Concat(state.Rules.Where(r=>r.Kind=="default")))HomeRoutes.Children.Add(RuleRow(rule,true));
        if(HomeRoutes.Children.Count==0)HomeRoutes.Children.Add(new TextBlock{Text="Добавь правило в разделе «Приложения».",Foreground=(Brush)FindResource("Muted")});
        string query=RuleSearch.Text.Trim();int filter=RulesFilter.SelectedIndex;
        var rules=state.Rules.Where(r=>r.Kind!="default").Where(r=>filter<=0||filter==1&&(r.Kind.StartsWith("process_")||r.Kind=="browser_default")||filter==2&&r.Kind.StartsWith("domain")||filter==3&&r.Kind=="ip_cidr").Where(r=>query.Length==0||r.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)||r.Value.Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();
        foreach(var rule in rules)AppRoutes.Children.Add(RuleRow(rule,false));
        if(rules.Count==0)AppRoutes.Children.Add(new TextBlock{Text="Правил пока нет или ничего не найдено. Добавь приложение, сайт или IP.",Margin=new Thickness(12),Foreground=(Brush)FindResource("Muted")});
        DefaultRouteHost.Children.Clear();DefaultRouteHost.Children.Add(RuleRow(state.Rules.First(r=>r.Kind=="default"),true));
        refreshingRoutes=false;
        DiscoveryNotice.Text="Профиль: "+(backend.ModernProfile?.Name??"не выбран")+". Изменения сохраняются здесь; применение переподключит ядро.";
        RulesCount.Text="Правил: "+state.Rules.Count(r=>r.Kind!="default")+". Проверяются сверху вниз; первое совпадение определяет маршрут.";
        RouteHint.Text="Правила выбранного профиля. Сохранённые изменения применяются при подключении.";
        RefreshConnections();RefreshTrayStatus();
    }
    Grid RuleRow(RouteRule rule,bool compact)
    {
        var grid=new Grid{Margin=new Thickness(0,0,0,1),Background=new SolidColorBrush(Color.FromArgb(7,220,226,239))};
        grid.ColumnDefinitions.Add(new(){Width=new GridLength(42)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(compact?220:180)});
        if(!compact){grid.ColumnDefinitions.Add(new(){Width=new GridLength(105)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(94)});}
        string shape=rule.QuickKey=="discord"?"M3,13 L3,9 C3,1 21,1 21,9 L21,13 M3,10 L6,10 L6,18 L3,18 Z M18,10 L21,10 L21,18 L18,18 Z M20,18 C20,22 17,22 13,22":rule.Kind.StartsWith("domain")||rule.QuickKey=="browser"?"M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M2,12 L22,12 M12,2 C5,8 5,16 12,22 C19,16 19,8 12,2":"M3,3 L9,3 L9,9 L3,9 Z M15,3 L21,3 L21,9 L15,9 Z M3,15 L9,15 L9,21 L3,21 Z M15,15 L21,15 L21,21 L15,21 Z";
        grid.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(shape),Stroke=(Brush)FindResource("Muted"),StrokeThickness=1.4,Width=22,Height=22,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center});
        var label=new StackPanel{Margin=new Thickness(0,11,8,11),VerticalAlignment=VerticalAlignment.Center,ToolTip=rule.Value};
        label.Children.Add(new TextBlock{Text=rule.Name,FontSize=compact?16:15});
        if(!compact)label.Children.Add(new TextBlock{Text=RouteRules.Label(rule.Kind)+" · "+(rule.Value.Length==0?"EXE ещё не найден":rule.Value),FontSize=11,Foreground=(Brush)FindResource("Muted"),TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,Margin=new Thickness(0,4,0,0)});
        Grid.SetColumn(label,1);grid.Children.Add(label);
        var choice=new ComboBox{ItemsSource=RouteRules.Routes,SelectedItem=rule.Route,Margin=new Thickness(0,6,6,6),VerticalAlignment=VerticalAlignment.Center,FontSize=14};
        string accessible=rule.QuickKey=="discord"?"Discord":rule.Name;
        System.Windows.Automation.AutomationProperties.SetName(choice,"Маршрут: "+accessible);
        choice.SelectionChanged+=(_,_)=>{if(refreshingRoutes)return;rule.Route=choice.SelectedItem?.ToString()??"Через VPN";Dispatcher.BeginInvoke(()=>{if(PersistRules())Toast("Правило сохранено. Применится при подключении или по кнопке «Применить правила».");});};
        if(rule.Kind=="default"){choice.ItemsSource=new[]{"Исходный профиль"};choice.SelectedIndex=0;choice.IsEnabled=false;choice.ToolTip="Маршрут задаётся исходным JSON/YAML профиля";}
        Grid.SetColumn(choice,2);grid.Children.Add(choice);
        if(!compact)
        {
            var edit=new Button{Content="Изменить",Padding=new Thickness(8),Margin=new Thickness(0,6,6,6),FontSize=12,VerticalAlignment=VerticalAlignment.Center};edit.Click+=(_,_)=>OpenRuleEditor(rule,rule.Kind);Grid.SetColumn(edit,3);grid.Children.Add(edit);
            var remove=new Button{Content="Удалить",Padding=new Thickness(8),Margin=new Thickness(0,6,6,6),FontSize=12,VerticalAlignment=VerticalAlignment.Center};
            System.Windows.Automation.AutomationProperties.SetName(remove,"Удалить правило: "+rule.Name);remove.Click+=(_,_)=>DeleteRule(rule);Grid.SetColumn(remove,4);grid.Children.Add(remove);
            var menu=new ContextMenu();var up=new MenuItem{Header="Выше"};var down=new MenuItem{Header="Ниже"};up.Click+=(_,_)=>MoveRule(rule,-1);down.Click+=(_,_)=>MoveRule(rule,1);menu.Items.Add(up);menu.Items.Add(down);grid.ContextMenu=menu;
        }
        return grid;
    }
    void MoveRule(RouteRule rule,int delta)
    {
        int index=state.Rules.IndexOf(rule),next=index+delta;
        if(next<0||next>=state.Rules.Count||state.Rules[next].Kind=="default")return;
        state.Rules.RemoveAt(index);state.Rules.Insert(next,rule);if(PersistRules())RefreshRoutes();
    }
    void DeleteRule(RouteRule rule)
    {
        if(rule.Kind=="default")return;
        deletedIndex=state.Rules.IndexOf(rule);deletedRule=rule;state.Rules.Remove(rule);UndoRuleButton.Visibility=Visibility.Visible;
        if(PersistRules()){RefreshRoutes();Toast("Удалено: "+rule.Name+". Можно вернуть кнопкой «Отменить удаление».");}
    }
    void UndoRule(object sender,RoutedEventArgs e)
    {
        if(deletedRule==null)return;
        state.Rules.Insert(Math.Clamp(deletedIndex,0,state.Rules.Count),deletedRule);deletedRule=null;UndoRuleButton.Visibility=Visibility.Collapsed;if(PersistRules())RefreshRoutes();
    }
    void RulesFilterChanged(object sender,EventArgs e){if(initialized)RefreshRoutes();}
    void AddSite(object sender,RoutedEventArgs e)=>OpenRuleEditor(null,"domain_suffix");
    void OpenRuleEditor(RouteRule? existing,string kind,RouteRule? draft=null,bool first=false)
    {
        WakeQuietMode();rulesDialogOpen=true;
        try
        {
            while(true)
            {
                var dialog=new RuleEditorWindow(existing,kind,r=>RuleMatching.Overlaps(r,state.Rules),draft){Owner=this};
                bool? accepted=dialog.ShowDialog();
                if(dialog.RequestedExistingRule!=null){existing=dialog.RequestedExistingRule;kind=existing.Kind;draft=null;first=false;continue;}
                if(accepted!=true||dialog.Result==null)return;
                var result=dialog.Result;
                if(state.Rules.Any(r=>r.Id!=result.Id&&RuleMatching.SameTarget(r,result))){Toast("Такое правило уже существует.");return;}
                CommitRule(result,existing,first);Toast("Правило сохранено: "+result.Name+". Нажми «Применить правила», чтобы переподключить работающий профиль.");return;
            }
        }
        catch(Exception ex){Toast(ex.Message);}
        finally{rulesDialogOpen=false;RegisterActivity();}
    }
}
