using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace RelayDesk;
public partial class MainWindow
{
    bool connectionMenuOpen;
    readonly Dictionary<string,DemoConnection> selectedConnections=new(StringComparer.Ordinal);
    string connectionProfile="";
    int connectionSort=-1;
    bool connectionDescending;
    IEnumerable<DemoConnection> CoreConnections()
    {
        foreach(var r in backend.ModernConnections)
        {
            int port=0;var end=r.Destination.LastIndexOf(':');if(end>=0)int.TryParse(r.Destination[(end+1)..],out port);
            yield return new(r.Application,r.Application,r.ProcessPath,r.Host,r.Address,r.Network,port,r.CanEditApplication,r.CanEdit,r.Route,r.Rule,r.Destination,r.Id);
        }
    }
    void RefreshConnections()
    {
        if(ConnectionRows==null||connectionMenuOpen||Mouse.LeftButton==MouseButtonState.Pressed)return;ConnectionRows.Children.Clear();
        string profile=backend.ModernSession.ActiveProfile?.Id??backend.ModernProfile?.Id??"";
        if(connectionProfile!=profile){selectedConnections.Clear();connectionProfile=profile;}
        var all=CoreConnections().ToList();
        foreach(var key in selectedConnections.Keys.ToArray()){var current=all.FirstOrDefault(c=>c.Id==key);if(current==null)selectedConnections.Remove(key);else selectedConnections[key]=current;}
        string query=ConnectionSearch.Text.Trim();
        var filters=new[]{ConnectionAppFilter.Text,ConnectionDestinationFilter.Text,ConnectionTypeFilter.Text,ConnectionRouteFilter.Text,ConnectionRuleFilter.Text};
        var rows=all.Where(c=>ConnectionBatch.Matches(c,query,filters)).ToList();
        if(connectionSort>=0)rows=(connectionDescending?rows.OrderByDescending(c=>ConnectionBatch.Cell(c,connectionSort),StringComparer.OrdinalIgnoreCase):rows.OrderBy(c=>ConnectionBatch.Cell(c,connectionSort),StringComparer.OrdinalIgnoreCase)).ThenBy(c=>c.Id,StringComparer.Ordinal).ToList();
        foreach(var connection in rows)
        {
            var winner=RuleMatching.Winner(state.Rules,connection);
            var layout=new Grid();foreach(double width in new[]{150d,235,90,160})layout.ColumnDefinitions.Add(new(){Width=new GridLength(width)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            void Cell(int col,string text,Brush? brush=null)
            {var label=new TextBlock{Text=text,Foreground=brush??(Brush)FindResource("Ink"),Margin=new Thickness(10,12,10,12),FontSize=13,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap,ToolTip=text};Grid.SetColumn(label,col);layout.Children.Add(label);}
            Cell(0,ConnectionBatch.Cell(connection,0));
            Cell(1,connection.Destination);
            Cell(2,connection.Protocol,(Brush)FindResource("Muted"));Cell(3,connection.ActualRoute);
            Cell(4,connection.ActualRule,(Brush)FindResource("Muted"));
            var row=new Button{Content=layout,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,5),ToolTip="Ctrl + щелчок — выделить. Щелчок — настроить одно соединение.",Tag=connection.Id};
            bool selected=selectedConnections.ContainsKey(connection.Id);
            if(selected){row.Background=new SolidColorBrush(Color.FromRgb(48,54,65));row.BorderBrush=new SolidColorBrush(Color.FromRgb(182,194,212));}
            System.Windows.Automation.AutomationProperties.SetItemStatus(row,selected?"Выбрано":"Не выбрано");
            System.Windows.Automation.AutomationProperties.SetName(row,"Соединение: "+connection.App+" → "+(connection.Host.Length>0?connection.Host:connection.Ip));
            row.Click+=(_,_)=>{if((Keyboard.Modifiers&ModifierKeys.Control)!=0){SelectConnection(connection,true);RefreshConnections();}else{SelectConnection(connection,false);row.Background=new SolidColorBrush(Color.FromRgb(48,54,65));OpenConnectionActions(row,connection);}};ConnectionRows.Children.Add(row);
        }
        if(rows.Count==0)ConnectionRows.Children.Add(new TextBlock{Text="По фильтру ничего не найдено.",Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(12)});
        ConnectionSummary.Text=$"Показано: {rows.Count} из {all.Count}. "+backend.ModernConnectionsStatus;
        UpdateConnectionSelection();
    }
    void SelectConnection(DemoConnection connection,bool toggle)
    {
        if(!toggle)selectedConnections.Clear();
        if(toggle&&selectedConnections.ContainsKey(connection.Id))selectedConnections.Remove(connection.Id);else selectedConnections[connection.Id]=connection;
        foreach(var row in ConnectionRows.Children.OfType<Button>())
        {
            bool selected=row.Tag is string id&&selectedConnections.ContainsKey(id);
            if(selected){row.Background=new SolidColorBrush(Color.FromRgb(48,54,65));row.BorderBrush=new SolidColorBrush(Color.FromRgb(182,194,212));}
            else{row.ClearValue(BackgroundProperty);row.ClearValue(BorderBrushProperty);}
            System.Windows.Automation.AutomationProperties.SetItemStatus(row,selected?"Выбрано":"Не выбрано");
        }
        UpdateConnectionSelection();
    }
    void UpdateConnectionSelection(){ConnectionSelectionCount.Text="Выбрано: "+selectedConnections.Count;ConfigureSelectedConnections.IsEnabled=selectedConnections.Count>0;}
    void ClearConnectionSelection(object sender,RoutedEventArgs e){selectedConnections.Clear();RefreshConnections();}
    void ClearConnectionFilters(object sender,RoutedEventArgs e){foreach(var box in new[]{ConnectionSearch,ConnectionAppFilter,ConnectionDestinationFilter,ConnectionTypeFilter,ConnectionRouteFilter,ConnectionRuleFilter})box.Clear();RefreshConnections();}
    void SortConnections(object sender,RoutedEventArgs e)
    {
        int column=int.Parse(((Button)sender).Tag.ToString()!);connectionDescending=connectionSort==column&&!connectionDescending;connectionSort=column;
        string[] labels=["Приложение","Назначение","Тип","Маршрут","Выбрано правилом"];
        foreach(var header in ConnectionColumnHeaders.Children.OfType<Button>()){int index=int.Parse(header.Tag.ToString()!);header.Content=labels[index]+(index==column?(connectionDescending?" ↓":" ↑"):"");}
        RefreshConnections();
    }
    void ConfigureConnections(object sender,RoutedEventArgs e)
    {
        if(selectedConnections.Count==0)return;
        var chosen=selectedConnections.Values.ToArray();var active=backend.ModernSession.ActiveProfile;
        if(active!=null&&backend.ModernProfile?.Id!=active.Id){if(!backend.ModernSelect(active))return;LoadProfileRules();}
        string profile=backend.ModernProfile?.Id??"";
        var dialog=new ConnectionBatchWindow(chosen,state.Rules){Owner=this};
        connectionMenuOpen=true;
        try
        {
            if(dialog.ShowDialog()!=true)return;
            if(backend.ModernProfile?.Id!=profile)throw new InvalidOperationException("Профиль изменился. Повтори настройку выбранных соединений.");
            SaveConnectionBatch(dialog.Result);
        }
        catch(Exception ex){Toast(ex.Message);}
        finally{connectionMenuOpen=false;RefreshConnections();}
    }
    void SaveConnectionBatch(IReadOnlyList<RouteRule> rules)
    {
        var merged=ConnectionBatch.Merge(state.Rules,rules);
        state.Rules=merged;
        if(!PersistRules())return;
        Toast($"Сохранено правил: {rules.Count}. Для работающего профиля нажми «Применить правила». Маршрут текущих соединений пока не меняется.");
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
