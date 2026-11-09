using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace RelayDesk;
public sealed class QrShareWindow:ThemedWindow
{
    sealed record Choice(string Name,Profile? Profile=null,SubscriptionSource? Subscription=null){public override string ToString()=>Name;}
    readonly ComboBox source=new();readonly CheckBox common=new(){Content="Включить общие правила и режим подключения"};
    readonly Image image=new(){MaxWidth=390,MaxHeight=390,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};readonly TextBlock status=new(),page=new();
    readonly Preferences settings;string[] parts=[];int index;
    public QrShareWindow(Store store,Profile? selected)
    {
        settings=store.Settings;Title="Поделиться QR-кодом";Width=600;Height=740;MinWidth=540;MinHeight=640;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(24)};Content=root;
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Что передать",FontSize=24,Margin=new Thickness(0,0,0,12)});
        var choices=store.Profiles.Where(p=>p.Core!="zapret").Select(p=>new Choice("Профиль · "+p.Name,p)).Concat(store.Settings.Subscriptions.Select(s=>new Choice("Подписка · "+(s.Group.Length>0?s.Group:"без группы"),Subscription:s))).Append(new Choice("Общие правила и режим подключения")).ToArray();
        source.ItemsSource=choices;System.Windows.Automation.AutomationProperties.SetName(source,"Данные для QR-кода");top.Children.Add(source);top.Children.Add(common);
        top.Children.Add(new TextBlock{Text="QR-код содержит доступ к VPN или подписке. Общие настройки передаются пакетом RelayDesk; ссылка подписки — обычным QR-кодом.",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,6,0,12)});
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,8)};
        var previous=new Button{Content="Назад"};var next=new Button{Content="Дальше"};page.Margin=new Thickness(14,8,14,0);navigation.Children.Add(previous);navigation.Children.Add(page);navigation.Children.Add(next);bottom.Children.Add(navigation);
        var actions=new WrapPanel();bottom.Children.Add(actions);
        void Button(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,0,8,8)};b.Click+=(_,_)=>{try{action();}catch(Exception ex){status.Text=ProgramJournal.Clean(ex.Message);}};actions.Children.Add(b);}
        Button("Копировать QR",()=>{if(image.Source is System.Windows.Media.Imaging.BitmapSource bitmap)Clipboard.SetImage(bitmap);});
        Button("Сохранить PNG…",()=>{if(parts.Length==0)return;var dialog=new SaveFileDialog{Filter="PNG|*.png",FileName="RelayDesk-QR.png"};if(dialog.ShowDialog(this)!=true)return;if(parts.Length>1&&Enumerable.Range(0,parts.Length).Any(i=>File.Exists(Path.Combine(Path.GetDirectoryName(dialog.FileName)!,Path.GetFileNameWithoutExtension(dialog.FileName)+$"-{i+1:000}.png"))))throw new IOException("Файлы QR-пакета уже существуют. Выбери другое имя.");for(int i=0;i<parts.Length;i++){var path=parts.Length==1?dialog.FileName:Path.Combine(Path.GetDirectoryName(dialog.FileName)!,Path.GetFileNameWithoutExtension(dialog.FileName)+$"-{i+1:000}.png");QrImages.Save(QrImages.Encode(parts[i]),path);}status.Text=parts.Length==1?"QR-код сохранён.":$"Сохранено кодов: {parts.Length}. Получателю нужны все части.";});
        bottom.Children.Add(status);var close=new Button{Content="Закрыть",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right};close.Click+=(_,_)=>Close();bottom.Children.Add(close);
        root.Children.Add(image);RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
        void Display(){if(parts.Length==0)return;image.Source=QrImages.Encode(parts[index]);page.Text=$"{index+1} / {parts.Length}";previous.IsEnabled=index>0;next.IsEnabled=index<parts.Length-1;}
        void Refresh()
        {
            try
            {
                if(source.SelectedItem is not Choice choice)return;common.IsEnabled=choice.Profile!=null;
                string text=choice.Subscription!=null?choice.Subscription.Url:QrShare.Create(choice.Profile,settings,choice.Profile==null||common.IsChecked==true).Encode();
                parts=QrParts.Split(text);index=0;Display();status.Text=parts.Length>1?"Получатель должен считать все части. До этого настройки не меняются.":"Готово к копированию или сохранению.";
            }
            catch(Exception ex){parts=[];image.Source=null;page.Text="";status.Text=ProgramJournal.Clean(ex.Message);}
        }
        previous.Click+=(_,_)=>{if(index>0){index--;Display();}};next.Click+=(_,_)=>{if(index<parts.Length-1){index++;Display();}};
        source.SelectionChanged+=(_,_)=>Refresh();common.Checked+=(_,_)=>Refresh();common.Unchecked+=(_,_)=>Refresh();source.SelectedItem=choices.FirstOrDefault(c=>c.Profile?.Id==selected?.Id)??choices[0];
    }
}
