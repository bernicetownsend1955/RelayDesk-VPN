using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RelayDesk;
public partial class MainWindow
{
    bool connectionMenuOpen;
    IEnumerable<DemoConnection> CoreConnections()
    {
        foreach(var r in backend.ModernConnections)
        {
            int port=0;var end=r.Destination.LastIndexOf(':');if(end>=0)int.TryParse(r.Destination[(end+1)..],out port);
            yield return new(r.Application,r.Application,r.ProcessPath,r.Host,r.Address,r.Network,port,r.CanEditApplication,r.CanEdit,r.Route,r.Rule,r.Destination);
        }
    }
    void RefreshConnections()
    {
        if(ConnectionRows==null||connectionMenuOpen)return;ConnectionRows.Children.Clear();
        string query=ConnectionSearch.Text.Trim();
        var rows=CoreConnections().Where(c=>query.Length==0||(c.App+" "+c.ProcessName+" "+c.Host+" "+c.Ip).Contains(query,StringComparison.OrdinalIgnoreCase)).ToList();
        foreach(var connection in rows)
        {
            var winner=RuleMatching.Winner(state.Rules,connection);
            var layout=new Grid();foreach(double width in new[]{150d,235,70,140})layout.ColumnDefinitions.Add(new(){Width=new GridLength(width)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            void Cell(int col,string text,Brush? brush=null)
            {var label=new TextBlock{Text=text,Foreground=brush??(Brush)FindResource("Ink"),Margin=new Thickness(10,12,10,12),FontSize=13,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,ToolTip=text};Grid.SetColumn(label,col);layout.Children.Add(label);}
            Cell(0,connection.App+" · "+connection.ProcessName);
            Cell(1,connection.Destination);
            Cell(2,connection.Protocol,(Brush)FindResource("Muted"));Cell(3,connection.ActualRoute);
            Cell(4,connection.ActualRule,(Brush)FindResource("Muted"));
            var row=new Button{Content=layout,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,5),ToolTip="Нажми, чтобы назначить маршрут приложению, домену или IP"};
            System.Windows.Automation.AutomationProperties.SetName(row,"Соединение: "+connection.App+" → "+(connection.Host.Length>0?connection.Host:connection.Ip));
            row.Click+=(_,_)=>OpenConnectionActions(row,connection);ConnectionRows.Children.Add(row);
        }
        if(rows.Count==0)ConnectionRows.Children.Add(new TextBlock{Text="По фильтру ничего не найдено.",Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(12)});
        ConnectionSummary.Text=backend.ModernConnectionsStatus;
    }
    void ConnectionFilterChanged(object sender,TextChangedEventArgs e){if(initialized)RefreshConnections();}
    void OpenConnectionActions(Button target,DemoConnection connection)
    {
        if(!connection.CanEdit){Toast("Ядро сообщает маршрут, но не поддерживает быстрые правила для этого формата. Используй исходную конфигурацию.");return;}
        var active=backend.ModernSession.ActiveProfile;
        if(active!=null&&backend.ModernProfile?.Id!=active.Id){if(!backend.ModernSelect(active))return;LoadProfileRules();}
        var menu=new ContextMenu{Background=new SolidColorBrush(Color.FromRgb(27,30,37)),Foreground=(Brush)FindResource("Ink"),PlacementTarget=target,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        void Scope(string label,string kind,string value,string name)
        {
            var group=new MenuItem{Header=label};
            foreach(string route in new[]{"Через VPN","Через zapret","Напрямую"})
            {
                var item=new MenuItem{Header=route};item.Click+=(_,_)=>ApplyConnectionRoute(connection,new(){Kind=kind,Value=value,Name=name,Route=route});group.Items.Add(item);
            }
            menu.Items.Add(group);
        }
        if(connection.CanApplication)Scope("Для приложения: "+connection.ProcessName,connection.ProcessPath.Length>0?"process_path":"process_name",connection.ProcessPath.Length>0?connection.ProcessPath:connection.ProcessName,connection.App);
        if(connection.Host.Length>0)
        {
            Scope("Для домена: "+connection.Host,"domain",connection.Host,connection.Host);
            Scope("Для домена и поддоменов: "+connection.Host,"domain_suffix",connection.Host,connection.Host+" и поддомены");
        }
        else if(System.Net.IPAddress.TryParse(connection.Ip,out _))Scope("Для IP: "+connection.Ip,"ip_cidr",RouteRules.Normalize("ip_cidr",connection.Ip),connection.Ip);
        var winner=RuleMatching.Winner(state.Rules,connection);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem{Header="Сработало: "+(winner?.Name??"нет правила"),IsEnabled=false});
        if(winner!=null&&winner.Kind!="default")
        {
            var edit=new MenuItem{Header="Изменить сработавшее правило…"};edit.Click+=(_,_)=>OpenRuleEditor(winner,winner.Kind);menu.Items.Add(edit);
        }
        connectionMenuOpen=true;menu.Closed+=(_,_)=>connectionMenuOpen=false;target.ContextMenu=menu;menu.IsOpen=true;
    }
    void ApplyConnectionRoute(DemoConnection connection,RouteRule candidate)
    {
        var existing=state.Rules.FirstOrDefault(r=>r.Kind!="default"&&RuleMatching.SameTarget(r,candidate));
        if(existing!=null){candidate.Id=existing.Id;candidate.QuickKey=existing.QuickKey;}
        var conflicts=RuleMatching.Overlaps(candidate,state.Rules);
        if(conflicts.Count>0){OpenRuleEditor(existing,candidate.Kind,candidate,true);return;}
        try{CommitRule(candidate,existing,true);}catch(Exception ex){Toast(ex.Message);return;}
        var winner=RuleMatching.Winner(state.Rules,connection);
        Toast("Правило для "+candidate.Name+" сохранено. Нажми «Применить правила», чтобы переподключить активный профиль.");
    }
    void CommitRule(RouteRule result,RouteRule? existing,bool first)
    {
        result.AutoResolve=existing?.AutoResolve==true&&RuleMatching.SameTarget(result,existing);
        if(result.AutoResolve&&existing?.Kind=="browser_default")result.Kind="browser_default";
        if(existing!=null)
        {
            int index=state.Rules.FindIndex(r=>r.Id==existing.Id);if(index>=0)state.Rules.RemoveAt(index);
            state.Rules.Insert(first?0:Math.Clamp(index,0,state.Rules.Count),result);
        }
        else state.Rules.Insert(0,result);
        if(!PersistRules())throw new InvalidOperationException(Notice.Text);
    }
}
