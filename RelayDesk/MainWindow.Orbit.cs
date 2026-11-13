using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RelayDesk.Orbit;

namespace RelayDesk;
public partial class MainWindow
{
    readonly Dictionary<string,Button> orbitRows=[];
    readonly HashSet<string> orbitCollapsed=[];
    readonly Dictionary<FrameworkElement,int> orbitTransitions=[];
    string orbitComponent="vpn",orbitTreeKey="",orbitDisplayId="";
    bool orbitRequestBusy;
    bool OrbitMotion=>initialized&&App.Motion;
    void InitializeOrbit()
    {
        InitializeInterfaceChoice();if(legacyUi)return;
        OrbitSettingsDrawer.Visibility=Visibility.Collapsed;
        backend.ModernSession.Changed+=()=>Dispatcher.BeginInvoke(RefreshFromBackend);
        SetOrbitBackground(backend.ModernStore.Settings.OrbitBackground,false);
    }
    static string OrbitGroup(Profile p)=>p.SubscriptionGroup.Length>0?p.SubscriptionGroup:p.SubscriptionId.Length>0?"Подписка":"Мои конфигурации";
    Place? OrbitPlace(Profile? profile)
    {
        if(profile==null)return null;
        string code=backend.ModernStore.Settings.ServerCountries?.GetValueOrDefault(profile.Id)??"";
        return Locations.Countries.FirstOrDefault(c=>c.Code==code)??Locations.Detect(profile.Name);
    }
    void RefreshOrbit()
    {
        if(legacyUi)return;
        var store=backend.ModernStore;var session=backend.ModernSession;bool running=session.ProxyRunning,busy=backend.ModernBusy||orbitRequestBusy;
        var profile=running?session.ActiveProfile:backend.ModernProfile;
        string treeKey=string.Join('|',store.Profiles.Select(p=>p.Id+":"+p.Name+":"+p.SubscriptionGroup+":"+p.Core));
        if(treeKey!=orbitTreeKey){orbitTreeKey=treeKey;RefreshOrbitTree();}
        OrbitSelectedName.Text=orbitComponent=="zapret"?"zapret":!running&&store.Settings.AutoServer?"Автовыбор сервера":profile?.Name??"Добавь сервер или подписку";
        string transport=session.ActiveTransport;
        string mode=!running?"off":transport.StartsWith("TUN",StringComparison.Ordinal)?"tun":"proxy";
        bool active=orbitComponent=="vpn"?running:session.ZapretRunning;
        OrbitIndicator.Fill=new SolidColorBrush(active?Color.FromRgb(117,199,147):Color.FromRgb(119,119,119));
        OrbitModeStatus.Foreground=new SolidColorBrush(active?Color.FromRgb(117,199,147):Color.FromRgb(144,144,144));
        OrbitModeStatus.Text=busy?"Выполняется операция…":!active?"Выключен":orbitComponent=="zapret"?"winws запущен":mode=="tun"?"Запущен · TUN":transport.StartsWith("Локальный",StringComparison.Ordinal)?"Локальный прокси запущен":"Запущен · Прокси";
        OrbitSelectedCard.ToolTip=orbitComponent=="vpn"?(profile?.Name??"")+"\n"+transport:"Обход DPI без смены IP";
        var place=OrbitPlace(profile);OrbitSelectedFlag.Code=orbitComponent=="vpn"?place?.Code??"":"";
        OrbitServerGroup.Text=orbitComponent=="vpn"?(profile==null?"":OrbitGroup(profile)):store.Settings.ZapretDiscordOnly?"Discord и правила zapret":"Все подходящие подключения";
        OrbitScene.Focus(orbitComponent=="vpn"?place:null);OrbitScene.Connected=running;
        OrbitLocationDescription.Text=place==null?"Страна не определена. Можно выбрать её вручную.":place.Country+" · приблизительно по стране сервера.";
        OrbitProxy.Content=profile?.Core=="mihomo"?"Конфиг":"Прокси";
        OrbitProxy.ToolTip=profile?.Core=="mihomo"?"Запустить исходную конфигурацию mihomo":"Включить системный прокси";
        OrbitOff.IsEnabled=!busy;OrbitProxy.IsEnabled=!busy&&profile!=null;OrbitTun.IsEnabled=!busy&&profile?.Core=="sing-box";
        OrbitTun.ToolTip=profile?.Core=="sing-box"?"TUN — перехват трафика приложений":"TUN доступен для sing-box. Для Xray используется системный прокси.";
        OrbitZapretPower.IsEnabled=!busy;OrbitZapretPower.Content=session.ZapretRunning?"Выключить zapret":"Включить zapret";
        OrbitVpnModes.Visibility=orbitComponent=="vpn"?Visibility.Visible:Visibility.Collapsed;OrbitZapretPower.Visibility=orbitComponent=="zapret"?Visibility.Visible:Visibility.Collapsed;
        OrbitPaint(OrbitVpnTab,BackgroundProperty,orbitComponent=="vpn"?"#303030":"#101010");OrbitPaint(OrbitZapretTab,BackgroundProperty,orbitComponent=="zapret"?"#303030":"#101010");
        PositionOrbitMode(mode);OrbitAutoChoice.Content=store.Settings.AutoServer?"Автовыбор включён":"Автовыбор при подключении";
        OrbitPresetChoice.Content=RoutingPresets.Label(store.Settings.RoutingPreset);
        OrbitUpdateStatus.Text=backend.ModernHasUpdate?"Доступна версия "+backend.ModernUpdateVersion:"RelayDesk "+AppUpdates.CurrentVersion+" · GitHub";
        foreach(var pair in orbitRows)
        {
            bool selected=pair.Key==backend.ModernProfile?.Id;
            OrbitPaint(pair.Value,BackgroundProperty,selected?"#303030":"#00000000");OrbitPaint(pair.Value,BorderBrushProperty,selected?"#4D4D4D":"#00000000");
            if(pair.Value.Content is Grid grid&&grid.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock latency)latency.Text=latencyResults.GetValueOrDefault(pair.Key,"");
        }
        orbitDisplayId=profile?.Id??"";
    }
    void OrbitSearchChanged(object sender,TextChangedEventArgs e){if(initialized&&!legacyUi)RefreshOrbitTree();}
    void RefreshOrbitTree()
    {
        if(legacyUi)return;OrbitServerTree.Children.Clear();orbitRows.Clear();string query=OrbitServerSearch.Text.Trim();
        foreach(var group in backend.ModernStore.Profiles.Where(p=>p.Core!="zapret"&&(query.Length==0||p.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)||OrbitGroup(p).Contains(query,StringComparison.CurrentCultureIgnoreCase))).GroupBy(OrbitGroup).OrderBy(g=>g.Key=="Мои конфигурации"?1:0))
        {
            var box=new StackPanel{Margin=new Thickness(0,0,0,8)};OrbitServerTree.Children.Add(box);
            var body=new StackPanel{ClipToBounds=true};bool open=!orbitCollapsed.Contains(group.Key)||query.Length>0;body.Visibility=open?Visibility.Visible:Visibility.Collapsed;
            var header=new Grid();header.ColumnDefinitions.Add(new(){Width=new GridLength(20)});header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var arrow=new System.Windows.Shapes.Path{Data=Geometry.Parse("M1,1 L5,5 L1,9"),Width=5,Height=9,Stretch=Stretch.Uniform,Stroke=new SolidColorBrush(Color.FromRgb(137,137,137)),StrokeThickness=1.3,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(open?90:0)};header.Children.Add(arrow);
            var title=new TextBlock{Text=group.Key,FontSize=12,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(title,1);header.Children.Add(title);
            var count=new TextBlock{Text=group.Count().ToString(),FontSize=11,Foreground=(Brush)FindResource("Muted"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(count,2);header.Children.Add(count);
            var toggle=new Button{Content=header,Style=(Style)FindResource("Plain"),HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(10,9,10,9)};box.Children.Add(toggle);box.Children.Add(body);
            toggle.Click+=(_,_)=>{bool expand=orbitCollapsed.Contains(group.Key);if(expand)orbitCollapsed.Remove(group.Key);else orbitCollapsed.Add(group.Key);body.Visibility=expand?Visibility.Visible:Visibility.Collapsed;((RotateTransform)arrow.RenderTransform).Angle=expand?90:0;};
            foreach(var profile in group)
            {
                var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(32)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                row.Children.Add(new CountryFlag{Code=OrbitPlace(profile)?.Code??"",VerticalAlignment=VerticalAlignment.Center});
                var text=new StackPanel{Margin=new Thickness(6,0,0,0)};text.Children.Add(new TextBlock{Text=profile.Name,FontSize=13,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis});text.Children.Add(new TextBlock{Text=profile.Core,FontSize=10,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,3,0,0)});Grid.SetColumn(text,1);row.Children.Add(text);
                var latency=new TextBlock{Text=latencyResults.GetValueOrDefault(profile.Id,""),FontSize=10,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(latency,2);row.Children.Add(latency);
                var pick=new Button{Content=row,Style=(Style)FindResource("Plain"),HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,9,10,9),Margin=new Thickness(0,1,0,1),Tag=profile.Id,ToolTip=profile.Name};pick.Click+=async(_,_)=>await OrbitSelectServer(profile);System.Windows.Automation.AutomationProperties.SetName(pick,"Выбрать сервер: "+profile.Name);body.Children.Add(pick);orbitRows[profile.Id]=pick;
            }
        }
        if(OrbitServerTree.Children.Count==0)OrbitServerTree.Children.Add(new TextBlock{Text="Ничего не найдено",Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(12)});
    }
    async Task OrbitSelectServer(Profile profile)
    {
        try{if(backend.ModernBusy||orbitRequestBusy)return;backend.ModernAuto(false,backend.ModernStore.Settings.AutoZapret);if(!backend.ModernSelect(profile))return;if(backend.ModernSession.ProxyRunning){orbitRequestBusy=true;RefreshOrbit();await backend.ModernSession.SwitchProfile(profile);}SelectPage(0);}
        catch(Exception ex){Toast(ex.Message);}finally{orbitRequestBusy=false;RefreshFromBackend();}
    }
    async void OrbitVpnModeClick(object sender,RoutedEventArgs e)
    {
        if(orbitRequestBusy||backend.ModernBusy)return;orbitRequestBusy=true;RefreshOrbit();
        try{await backend.ModernSetVpnMode(((Button)sender).Tag.ToString()!);}
        catch(Exception ex){Toast(ex.Message);}finally{orbitRequestBusy=false;RefreshFromBackend();}
    }
    void OrbitComponentClick(object sender,RoutedEventArgs e){orbitComponent=((Button)sender).Tag.ToString()!;RefreshOrbit();}
    void OrbitAutoClick(object sender,RoutedEventArgs e){try{backend.ModernAuto(!backend.ModernStore.Settings.AutoServer,backend.ModernStore.Settings.AutoZapret);RefreshFromBackend();}catch(Exception ex){Toast(ex.Message);}}
    void OrbitRevealServer(object sender,RoutedEventArgs e)
    {
        var profile=backend.ModernSession.ActiveProfile??backend.ModernProfile;if(profile==null){ImportProfiles((Button)sender,e);return;}
        orbitCollapsed.Remove(OrbitGroup(profile));OrbitServerSearch.Clear();RefreshOrbitTree();orbitRows.GetValueOrDefault(profile.Id)?.BringIntoView();
    }
    void OrbitPasteSubscription(object sender,RoutedEventArgs e)=>PasteFromClipboard(sender,e);
    void OrbitSettingsClick(object sender,RoutedEventArgs e)=>ShowOrbitSettings(OrbitSettingsDrawer.Visibility!=Visibility.Visible||!OrbitSettingsDrawer.IsHitTestVisible);
    void ShowOrbitSettings(bool show)
    {
        if(legacyUi){if(show)SelectPage(3);return;}
        if(show){LoadSharedConnection();LoadModernStartup();StartupPanel.Visibility=Visibility.Visible;SettingsPage.Visibility=Visibility.Visible;}
        OrbitLayer(OrbitSettingsDrawer,show,18);
    }
    void OrbitOpenConnection(object sender,RoutedEventArgs e)
    {
        ShowOrbitSettings(false);LoadSharedConnection();LoadZapretStrategies();OrbitHome.Visibility=Visibility.Collapsed;OrbitContentArea.Visibility=Visibility.Collapsed;OrbitLayer(OrbitConnectionPage,true);backend.ModernConnectionsVisible=false;UpdateOrbitPlayback();
    }
    void OrbitOpenAutomation(object sender,RoutedEventArgs e){OrbitConnectionPage.Visibility=Visibility.Collapsed;OrbitContentArea.Visibility=Visibility.Visible;ShowAdvanced(4);}
    void OrbitBackgroundClick(object sender,RoutedEventArgs e)=>SetOrbitBackground(((Button)sender).Tag.ToString()!,true);
    void SetOrbitBackground(string value,bool save)
    {
        if(legacyUi)return;if(value is not ("earth" or "space" or "ascii" or "none"))value="earth";
        backend.ModernStore.Settings.OrbitBackground=value;OrbitScene.Scene=value is "earth" or "space"?value:"none";OrbitLayer(CityBackdrop,value=="ascii");CityMotionChoice.Visibility=value=="ascii"?Visibility.Visible:Visibility.Collapsed;
        MaterialLabel.Text=value switch{"earth"=>"Земля · страна приблизительно","space"=>"Космос","ascii"=>"ASCII-акулы",_=>"Чёрный фон"};if(save)Save();UpdateOrbitPlayback();
    }
    void UpdateOrbitPlayback(){if(legacyUi)return;bool animate=App.Motion&&!quiet&&OrbitHome.Visibility==Visibility.Visible;OrbitScene.AnimationEnabled=animate;CityBackdrop.AnimationEnabled=animate&&backend.ModernStore.Settings.AsciiBackground;}
    void OrbitConnectionLayout(object sender,SizeChangedEventArgs e){OrbitScene.ControlInset=Math.Max(240,e.NewSize.Height+54);OrbitScene.InvalidateVisual();}
    void OrbitModeTrackChanged(object sender,SizeChangedEventArgs e)=>PositionOrbitMode(!vpn?"off":backend.ModernSession.ActiveTransport.StartsWith("TUN",StringComparison.Ordinal)?"tun":"proxy");
    void PositionOrbitMode(string mode)
    {
        if(OrbitModeTrack==null||OrbitModeTrack.ActualWidth<1)return;double width=OrbitModeTrack.ActualWidth/3;OrbitModePill.Width=width;double target=width*(mode=="off"?0:mode=="proxy"?1:2);
        if(Math.Abs(OrbitModePillShift.X-target)<.01)return;
        if(OrbitMotion)OrbitModePillShift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(300)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}});else{OrbitModePillShift.BeginAnimation(TranslateTransform.XProperty,null);OrbitModePillShift.X=target;}
    }
    void OrbitPresetClick(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();foreach(string preset in new[]{"rules","direct","proxy"}){var item=new MenuItem{Header=RoutingPresets.Label(preset),IsCheckable=true,IsChecked=backend.ModernStore.Settings.RoutingPreset==preset};item.Click+=(_,_)=>ChangePreset(new Button{Tag=preset},new RoutedEventArgs());menu.Items.Add(item);}menu.PlacementTarget=OrbitPresetChoice;menu.IsOpen=true;
    }
    void OrbitPickCountry(object sender,RoutedEventArgs e)
    {
        var profile=backend.ModernProfile;if(profile==null){Toast("Выбери сервер.");return;}
        var window=new ThemedWindow{Title="Страна сервера",Width=460,Height=560,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};var list=new StackPanel{Margin=new Thickness(24)};window.Content=new ScrollViewer{Content=list};list.Children.Add(new TextBlock{Text="Приблизительное положение по стране. IP-адрес сервера не меняется.",Margin=new Thickness(0,0,0,16)});
        foreach(var country in Locations.Countries){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new CountryFlag{Code=country.Code,Margin=new Thickness(0,0,12,0)});row.Children.Add(new TextBlock{Text=country.Country,VerticalAlignment=VerticalAlignment.Center});var pick=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch};pick.Click+=(_,_)=>{backend.ModernStore.Settings.ServerCountries??=new();backend.ModernStore.Settings.ServerCountries[profile.Id]=country.Code;Save();window.Close();RefreshOrbitTree();RefreshOrbit();};list.Children.Add(pick);}window.ShowDialog();
    }
    void OrbitPaint(Control control,DependencyProperty property,string value)
    {
        var target=(Color)ColorConverter.ConvertFromString(value);var current=control.GetValue(property) as SolidColorBrush;if(current?.Color==target)return;
        var brush=new SolidColorBrush(current?.Color??target);control.SetValue(property,brush);if(OrbitMotion)brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(target,TimeSpan.FromMilliseconds(220)));else brush.Color=target;
    }
    void OrbitLayer(FrameworkElement element,bool show,double offset=0)
    {
        int stamp=orbitTransitions.GetValueOrDefault(element)+1;orbitTransitions[element]=stamp;if(element!=CityBackdrop)element.IsHitTestVisible=show;
        if(!OrbitMotion){element.BeginAnimation(OpacityProperty,null);element.Opacity=element==CityBackdrop ? .6 : 1;element.Visibility=show?Visibility.Visible:Visibility.Collapsed;return;}
        double target=element==CityBackdrop ? .6 : 1,from=element.Visibility==Visibility.Visible?element.Opacity:0;if(show)element.Visibility=Visibility.Visible;
        var opacity=new DoubleAnimation(from,show?target:0,TimeSpan.FromMilliseconds(show?320:240)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}};
        opacity.Completed+=(_,_)=>{if(orbitTransitions.GetValueOrDefault(element)!=stamp)return;element.BeginAnimation(OpacityProperty,null);element.Opacity=target;if(!show)element.Visibility=Visibility.Collapsed;};element.BeginAnimation(OpacityProperty,opacity);
        var shift=element.RenderTransform as TranslateTransform??new TranslateTransform();element.RenderTransform=shift;shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(show?0:offset,TimeSpan.FromMilliseconds(340)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}});
    }
}
