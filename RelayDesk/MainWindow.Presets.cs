using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RelayDesk;
public partial class MainWindow
{
    void UpdatePowerControl(Button button,TextBlock label,Path glyph,bool running,bool busy,string name)
    {
        button.IsEnabled=!busy;label.Text=busy?"Подожди…":running?"Остановить "+name:"Включить "+name;
        button.Background=new SolidColorBrush(running?Color.FromRgb(220,226,235):Color.FromRgb(36,40,48));
        button.Foreground=new SolidColorBrush(running?Color.FromRgb(16,19,24):Color.FromRgb(236,238,242));
        button.BorderBrush=new SolidColorBrush(running?Color.FromRgb(220,226,235):Color.FromRgb(76,83,94));
        glyph.Data=Geometry.Parse(running?"M7,7 L17,7 L17,17 L7,17 Z":"M18.36,6.64 A9,9 0 1 1 5.64,6.64 M12,2 L12,12");
        System.Windows.Automation.AutomationProperties.SetHelpText(button,label.Text);
        System.Windows.Automation.AutomationProperties.SetItemStatus(button,busy?"Выполняется операция":running?"Включён":"Выключен");
    }
    void RefreshPresetButtons()
    {
        string preset=backend.ModernStore.Settings.RoutingPreset;
        foreach(var button in PresetButtons.Children.OfType<Button>())
        {
            bool selected=button.Tag?.ToString()==preset;button.IsEnabled=!backend.ModernBusy;
            button.Background=new SolidColorBrush(selected?Color.FromRgb(220,226,235):Color.FromRgb(25,29,36));
            button.Foreground=new SolidColorBrush(selected?Color.FromRgb(16,19,24):Color.FromRgb(236,238,242));
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Выбран":"Не выбран");
        }
        HomeRoutes.Visibility=preset=="rules"?Visibility.Visible:Visibility.Collapsed;
        PresetStatus.Text=(backend.ModernBusy?"Переключение…":(backend.ModernSession.ProxyRunning?"Активен: ":"При включении VPN: ")+RoutingPresets.Label(preset))+(preset=="rules"?". zapret управляется отдельно.":". Сохранённые правила временно не используются; zapret управляется отдельно.");
    }
    async void ChangePreset(object sender,RoutedEventArgs e)
    {
        if(sender is not Button button||button.Tag is not string preset)return;
        try{await backend.ModernPreset(preset);Toast("Пресет: "+RoutingPresets.Label(preset)+". Правила сохранены.");}
        catch(Exception ex){Toast(ex.Message);}
        finally{RefreshFromBackend();}
    }
}
