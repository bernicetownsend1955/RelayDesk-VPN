using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace RelayDesk;

public sealed class RuleEditorWindow : Window
{
    readonly ComboBox kind=new(),route=new();
    readonly TextBox name=new(),value=new(),search=new();
    readonly TextBlock hint=new(),status=new(),preview=new();
    readonly StackPanel appSection=new();
    readonly ListBox applications=new();
    readonly RouteRule? original;
    readonly RouteRule? draft;
    readonly Func<RouteRule,IReadOnlyList<RuleOverlap>>? findOverlaps;
    readonly Border conflictBox=new(){Background=new SolidColorBrush(Color.FromRgb(42,34,29)),BorderBrush=new SolidColorBrush(Color.FromRgb(97,76,57)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(12),Margin=new Thickness(0,10,0,0),Visibility=Visibility.Collapsed};
    readonly StackPanel conflictRows=new();
    Button saveButton=null!;
    public RouteRule? RequestedExistingRule {get;private set;}
    List<WindowsApplication> installed=[];
    public RouteRule? Result {get;private set;}
    static readonly SolidColorBrush Muted=new(Color.FromRgb(157,163,175));
    string Kind=>RouteRules.Kinds[Math.Max(0,kind.SelectedIndex)];
    public RuleEditorWindow(RouteRule? existing=null,string initialKind="process_path",Func<RouteRule,IReadOnlyList<RuleOverlap>>? conflicts=null,RouteRule? initialDraft=null)
    {
        original=existing;draft=initialDraft;findOverlaps=conflicts;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/RelayDesk;component/ModernTheme.xaml")});
        Title=existing==null?"Добавить правило":"Изменить правило";
        Width=760;Height=Math.Min(820,SystemParameters.WorkArea.Height-60);MinWidth=650;MinHeight=450;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(18,21,28));Foreground=new SolidColorBrush(Color.FromRgb(236,238,242));FontFamily=new FontFamily("Segoe UI");FontSize=14;
        var root=new DockPanel{Margin=new Thickness(24)};Content=root;
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        var cancel=new Button{Content="Отмена",IsCancel=true,Margin=new Thickness(0,0,10,0)};cancel.Click+=(_,_)=>Close();buttons.Children.Add(cancel);
        var save=new Button{Content="Сохранить правило",IsDefault=true};saveButton=save;save.Click+=Save;buttons.Children.Add(save);
        DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var routeFooter=new StackPanel{Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(routeFooter,Dock.Bottom);root.Children.Add(routeFooter);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};root.Children.Add(scroll);
        var form=new StackPanel();scroll.Content=form;
        form.Children.Add(new TextBlock{Text=Title,FontSize=26,FontWeight=FontWeights.Medium,Margin=new Thickness(0,0,0,16)});
        Add(form,"Тип правила",kind);kind.ItemsSource=RouteRules.Kinds.Select(RouteRules.Label).ToArray();
        Add(form,"Название в списке (необязательно)",name);
        Add(form,"Значение",value);
        hint.Foreground=Muted;hint.Margin=new Thickness(0,8,0,10);hint.TextWrapping=TextWrapping.Wrap;form.Children.Add(hint);
        System.Windows.Automation.AutomationProperties.SetName(kind,"Тип правила");
        System.Windows.Automation.AutomationProperties.SetName(name,"Название правила");
        System.Windows.Automation.AutomationProperties.SetName(value,"Значение правила");
        System.Windows.Automation.AutomationProperties.SetName(route,"Маршрут правила");
        var quick=new WrapPanel{Margin=new Thickness(0,4,0,8)};
        Button Quick(string title,Action action)
        {
            var b=new Button{Content=title,Margin=new Thickness(0,0,8,8),Padding=new Thickness(12,8,12,8)};b.Click+=(_,_)=>action();quick.Children.Add(b);return b;
        }
        Quick("Найти Discord",()=>ChooseQuick(false));Quick("Браузер по умолчанию",()=>ChooseQuick(true));Quick("Выбрать EXE…",Browse);
        appSection.Children.Add(quick);
        search.ToolTip="Поиск в установленных приложениях";search.Margin=new Thickness(0,0,0,8);
        System.Windows.Automation.AutomationProperties.SetName(search,"Поиск установленных приложений");appSection.Children.Add(search);
        applications.Height=165;applications.Background=new SolidColorBrush(Color.FromRgb(23,26,33));applications.Foreground=Foreground;applications.BorderBrush=new SolidColorBrush(Color.FromRgb(55,60,71));
        applications.Padding=new Thickness(6);applications.SelectionChanged+=(_,_)=>{if(applications.SelectedItem is WindowsApplication app)Choose(app);};
        var items=new Style(typeof(ListBoxItem));
        items.Setters.Add(new Setter(Control.ForegroundProperty,Foreground));
        items.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
        items.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'><Border x:Name='Row' Background='Transparent' Padding='{TemplateBinding Padding}' CornerRadius='5'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Row' Property='Background' Value='#3A404D'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Row' Property='Background' Value='#2B303B'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")));
        applications.ItemContainerStyle=items;
        System.Windows.Automation.AutomationProperties.SetName(applications,"Установленные приложения Windows");
        appSection.Children.Add(applications);form.Children.Add(appSection);
        search.TextChanged+=(_,_)=>FilterApplications();
        value.TextChanged+=(_,_)=>{status.Text="";UpdatePreview();};
        preview.Foreground=Muted;preview.Margin=new Thickness(0,10,0,6);preview.TextWrapping=TextWrapping.Wrap;form.Children.Add(preview);
        Add(routeFooter,"Куда направлять",route);route.ItemsSource=RouteRules.Routes;route.SelectedItem=(draft??existing)?.Route??"Через VPN";
        conflictBox.Child=conflictRows;routeFooter.Children.Add(conflictBox);
        route.SelectionChanged+=(_,_)=>UpdatePreview();
        status.Foreground=new SolidColorBrush(Color.FromRgb(226,178,148));status.Margin=new Thickness(0,8,0,0);status.TextWrapping=TextWrapping.Wrap;routeFooter.Children.Add(status);
        routeFooter.Children.Add(new TextBlock{Text="Правила сохраняются для выбранного профиля. Применение переподключает VPN.",Foreground=Muted,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        kind.SelectionChanged+=(_,_)=>{hint.Text=RouteRules.Example(Kind);appSection.Visibility=Kind=="process_path"?Visibility.Visible:Visibility.Collapsed;UpdatePreview();};
        string targetKind=(draft??existing)?.Kind??initialKind;
        if(targetKind=="browser_default")targetKind="process_path";
        kind.SelectedIndex=Math.Max(0,Array.IndexOf(RouteRules.Kinds,targetKind));
        var seed=draft??existing;if(seed!=null){name.Text=seed.Name;value.Text=seed.Value;}
        Loaded+=async(_,_)=>
        {
            try
            {
                int dark=1;Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,20,ref dark,4);
                status.Text="Читаю список приложений Windows…";
                installed=await Task.Run(WindowsApplications.Scan);
                if(!IsLoaded)return;FilterApplications();status.Text="Найдено в Windows: "+installed.Count+". Команды приложений не запускаются.";
            }
            catch(Exception ex){status.Text="Не удалось прочитать список: "+ex.Message+". Можно выбрать EXE вручную.";}
        };
    }
    static void Add(Panel panel,string label,FrameworkElement element)
    {
        panel.Children.Add(new TextBlock{Text=label,Foreground=Muted,Margin=new Thickness(0,10,0,6)});panel.Children.Add(element);
    }
    void FilterApplications()
    {
        string query=search.Text.Trim();applications.ItemsSource=installed.Where(a=>query.Length==0||a.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)).Take(350).ToList();
    }
    void Choose(WindowsApplication app)
    {
        name.Text=app.DefaultBrowser?app.Name+" · по умолчанию":app.Name;
        if(app.Executable.Length==0){status.Text="Windows не указала EXE для «"+app.Name+"». Нажми «Выбрать EXE…».";return;}
        value.Text=app.Executable;status.Text=app.Source+". EXE найден, файл не запускался.";
    }
    void ChooseQuick(bool browser)
    {
        var app=browser?installed.FirstOrDefault(a=>a.DefaultBrowser):installed.FirstOrDefault(a=>a.Name.Contains("Discord",StringComparison.OrdinalIgnoreCase)&&a.Executable.Length>0&&!Path.GetFileName(a.Executable).Equals("Update.exe",StringComparison.OrdinalIgnoreCase));
        if(app==null){status.Text=browser?"Windows не сообщила EXE браузера по умолчанию. Найди его в списке или выбери EXE.":"Discord пока не найден. Попробуй поиск Vesktop или выбери EXE клиента.";return;}
        applications.SelectedItem=app;Choose(app);
    }
    void Browse()
    {
        var picker=new Microsoft.Win32.OpenFileDialog{Title="EXE приложения",Filter="Приложения (*.exe)|*.exe",CheckFileExists=true};
        if(picker.ShowDialog(this)!=true)return;value.Text=picker.FileName;
        if(name.Text.Length==0)name.Text=Path.GetFileNameWithoutExtension(picker.FileName);
        status.Text="Выбран файл. Он не запускался.";
    }
    void UpdatePreview()
    {
        conflictBox.Visibility=Visibility.Collapsed;conflictRows.Children.Clear();
        if(saveButton==null)return;saveButton.IsEnabled=true;saveButton.Content="Сохранить правило";
        if(kind.SelectedIndex<0||value.Text.Length==0){preview.Text="";return;}
        try
        {
            string normalized=RouteRules.Normalize(Kind,value.Text);
            preview.Text=Kind switch{"domain"=>"Только: "+normalized,"domain_suffix"=>normalized+" и *."+normalized,"ip_cidr"=>"Сеть: "+normalized,_=>"Будет сохранено: "+normalized};
            if(findOverlaps!=null)
            {
                var candidate=new RouteRule{Id=original?.Id??draft?.Id??"new",Kind=Kind,Value=normalized,Name=name.Text,Route=route.SelectedItem?.ToString()??"Через VPN"};
                var overlaps=findOverlaps(candidate);
                if(overlaps.Count>0)
                {
                    conflictBox.Visibility=Visibility.Visible;
                    foreach(var item in overlaps.Take(3))
                    {
                        string target=item.Existing.Kind=="domain_suffix"?"*."+item.Existing.Value:item.Existing.Value;
                        conflictRows.Children.Add(new TextBlock{Text=item.Reason+"\nУже есть: "+target+" → "+item.Existing.Route+(item.SameRoute?" (тот же маршрут)":" (другой маршрут)"),FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
                        var edit=new Button{Content="Изменить существующее: "+item.Existing.Name,HorizontalAlignment=HorizontalAlignment.Left,FontSize=12,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,0,8)};
                        edit.Click+=(_,_)=>{RequestedExistingRule=item.Existing;DialogResult=false;};conflictRows.Children.Add(edit);
                    }
                    bool exact=overlaps.Any(o=>o.Exact);
                    saveButton.IsEnabled=!exact;
                    if(!exact)
                    {
                        if(original==null||draft!=null){saveButton.Content="Добавить правило выше";conflictRows.Children.Add(new TextBlock{Text="Новое правило окажется выше существующих и будет проверяться первым.",FontSize=12,TextWrapping=TextWrapping.Wrap});}
                        else conflictRows.Children.Add(new TextBlock{Text="Порядок сохраняется. Из пересекающихся правил срабатывает стоящее выше.",FontSize=12,TextWrapping=TextWrapping.Wrap});
                    }
                }
            }
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException){preview.Text="";}
    }
    void Save(object sender,RoutedEventArgs e)
    {
        try
        {
            string normalized=RouteRules.Normalize(Kind,value.Text);
            Result=original?.Copy()??draft?.Copy()??new();Result.Kind=Kind;Result.Value=normalized;Result.Route=route.SelectedItem?.ToString()??"Через VPN";
            if(findOverlaps?.Invoke(Result).Any(o=>o.Exact)==true){status.Text="Такое правило уже существует. Измени его вместо создания копии.";return;}
            Result.Name=string.IsNullOrWhiteSpace(name.Text)?Kind=="process_path"?Path.GetFileNameWithoutExtension(normalized):normalized:name.Text.Trim();
            if(Result.Name.Length>100)throw new InvalidDataException("Сократи название до 100 символов.");
            DialogResult=true;
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException){status.Text=ex.Message;}
    }
    static class Native
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]internal static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    }
}
