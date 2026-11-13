using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;

namespace RelayDesk;
public partial class MainWindow
{
    readonly bool legacyUi=ReadClassicPreference();
    public bool UsesClassicUi=>legacyUi;
    static bool ReadClassicPreference()
    {
        string folder=Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RelayDesk");
        try{using var json=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"settings.json")));return json.RootElement.TryGetProperty("UiMode",out var mode)&&mode.GetString()=="classic";}
        catch(Exception ex) when(ex is IOException or JsonException){return false;}
    }
    void LoadClassicLayout()
    {
        XNamespace p="http://schemas.microsoft.com/winfx/2006/xaml/presentation",x="http://schemas.microsoft.com/winfx/2006/xaml";
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("RelayDesk.LegacyLayout.xml")??throw new InvalidDataException("Не найден классический интерфейс.");
        var original=XDocument.Load(stream).Root!;var layout=original.Element(p+"Grid")!;
        var resources=new XElement(p+"Grid.Resources",original.Element(p+"Window.Resources")!.Elements().Select(e=>new XElement(e)));layout.AddFirst(resources);
        layout.SetAttributeValue(XNamespace.Xmlns+"local","clr-namespace:RelayDesk;assembly=RelayDesk");
        foreach(var element in layout.DescendantsAndSelf().Where(e=>e.Name.NamespaceName=="clr-namespace:RelayDesk"))element.Name=XName.Get(element.Name.LocalName,"clr-namespace:RelayDesk;assembly=RelayDesk");
        foreach(var resource in layout.Descendants(p+"ResourceDictionary"))if(resource.Attribute("Source") is XAttribute source&&!source.Value.Contains(':'))source.Value="pack://application:,,,/RelayDesk;component/"+source.Value;
        string[] events=["Click","SelectionChanged","TextChanged","ValueChanged","Checked","Unchecked","Loaded","SizeChanged","PreviewMouseLeftButtonDown","MouseDown","MouseMove","MouseUp"];
        var bindings=new List<(string Name,string Event,string Method)>();int number=0;
        foreach(var element in layout.DescendantsAndSelf())
        foreach(var attribute in element.Attributes().Where(a=>a.Name.NamespaceName.Length==0&&events.Contains(a.Name.LocalName)).ToArray())
        {
            string id=element.Attribute(x+"Name")?.Value??"ClassicEvent"+(number++);element.SetAttributeValue(x+"Name",id);bindings.Add((id,attribute.Name.LocalName,attribute.Value));attribute.Remove();
        }
        var view=(FrameworkElement)XamlReader.Parse(layout.ToString());
        foreach(var element in layout.DescendantsAndSelf().Where(e=>e.Attribute(x+"Name")!=null))
        {
            string id=element.Attribute(x+"Name")!.Value;var control=view.FindName(id);if(control==null)continue;
            var field=GetType().GetField(id,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);if(field!=null&&field.FieldType.IsInstanceOfType(control))field.SetValue(this,control);
            try{UnregisterName(id);}catch(ArgumentException){}RegisterName(id,control);
        }
        foreach(var binding in bindings)
        {
            object? target=view.FindName(binding.Name);if(target==null)continue;var ev=target.GetType().GetEvent(binding.Event);
            var method=GetType().GetMethod(binding.Method,BindingFlags.Instance|BindingFlags.NonPublic);if(ev!=null&&method!=null)ev.AddEventHandler(target,Delegate.CreateDelegate(ev.EventHandlerType!,this,method));
        }
        Content=view;FontSize=16;
    }
    void InitializeInterfaceChoice()
    {
        var panel=new StackPanel{Margin=new Thickness(0,8,0,18)};
        panel.Children.Add(new TextBlock{Text="Интерфейс",FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        var choice=new ComboBox{ItemsSource=new[]{"Новый · Orbit","Классический"},SelectedIndex=legacyUi?1:0,Margin=new Thickness(0,0,0,10)};panel.Children.Add(choice);
        System.Windows.Automation.AutomationProperties.SetAutomationId(choice,"InterfaceChoice");
        var restart=new Button{Content="Применить и перезапустить",IsEnabled=false,HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(restart);
        System.Windows.Automation.AutomationProperties.SetAutomationId(restart,"ApplyInterfaceChoice");
        panel.Children.Add(new TextBlock{Text="Выбранный сервер, VPN и zapret восстановятся после перезапуска.",Style=(Style)FindResource("MutedText"),Margin=new Thickness(0,8,0,0)});
        choice.SelectionChanged+=(_,_)=>restart.IsEnabled=choice.SelectedIndex!=(legacyUi?1:0);
        restart.Click+=async(_,_)=>
        {
            restart.IsEnabled=false;
            try{await backend.ModernRestartInterface(choice.SelectedIndex==1?"classic":"orbit");}
            catch(Exception ex){Toast(ex.Message);restart.IsEnabled=true;}
        };
        SettingsPage.Children.Insert(Math.Min(2,SettingsPage.Children.Count),panel);
    }
}
