using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RelayDesk;
public partial class MainWindow
{
    readonly QrParts qrParts=new();
    void ShareQr(object sender,RoutedEventArgs e)
    {
        WakeQuietMode();rulesDialogOpen=true;try{new QrShareWindow(backend.ModernStore,backend.ModernProfile){Owner=this}.ShowDialog();}catch(Exception ex){Toast(ex.Message);}finally{rulesDialogOpen=false;RegisterActivity();}
    }
    void ReadQr(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        void Item(string text,RoutedEventHandler action){var item=new MenuItem{Header=text};item.Click+=action;menu.Items.Add(item);}
        Item("Из изображения…",ReadQrFile);Item("Из буфера обмена",ReadQrClipboard);Item("Выделить QR-код на экране…",ReadQrScreen);
        menu.PlacementTarget=(UIElement)sender;menu.IsOpen=true;
    }
    async void ReadQrFile(object sender,RoutedEventArgs e)
    {
        try{var dialog=new OpenFileDialog{Filter="Изображение QR-кода|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"};if(dialog.ShowDialog(this)==true)await AcceptQrImage(QrImages.Load(dialog.FileName));}catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
    }
    async void ReadQrClipboard(object sender,RoutedEventArgs e)
    {
        try{if(Clipboard.ContainsImage())await AcceptQrImage(Clipboard.GetImage());else if(Clipboard.ContainsText())await AcceptQrText(Clipboard.GetText());else Toast("Скопируй QR-код изображением или его содержимое текстом.");}catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
    }
    async void ReadQrScreen(object sender,RoutedEventArgs e)
    {
        var before=WindowState;rulesDialogOpen=true;
        try{WindowState=WindowState.Minimized;await Task.Delay(250);var capture=new QrScreenWindow();if(capture.ShowDialog()==true&&capture.SelectedImage!=null){ShowFromTray();WindowState=before;Activate();await AcceptQrImage(capture.SelectedImage);}}
        catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
        finally{WindowState=before;rulesDialogOpen=false;Activate();RegisterActivity();}
    }
    async Task AcceptQrImage(BitmapSource image){try{var prepared=QrImages.PrepareForDecode(image);var text=await Task.Run(()=>QrImages.Decode(prepared));await AcceptQrText(text);}catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}}
    async Task AcceptQrText(string text)
    {
        WakeQuietMode();
        try
        {
            var part=qrParts.Accept(text.Trim());if(part.Text==null){Toast($"Считано QR-кодов: {part.Read} из {part.Total}. Считай оставшиеся части; настройки пока не менялись.");return;}
            text=part.Text;
            rulesDialogOpen=true;
            if(text.StartsWith("relaydesk-share-v1:",StringComparison.Ordinal))
            {
                var share=QrShare.Decode(text);var dialog=new ThemedWindow{Title="Предпросмотр QR-пакета",Owner=this,Width=560,Height=330,WindowStartupLocation=WindowStartupLocation.CenterOwner};var form=new StackPanel{Margin=new Thickness(24)};dialog.Content=form;
                form.Children.Add(new TextBlock{Text="Импорт из QR-кода",FontSize=24,Margin=new Thickness(0,0,0,16)});
                form.Children.Add(new TextBlock{Text=(share.Profile!=null?"Профиль: "+share.Profile.Name+" · "+share.Profile.Core+"\n":"")+(share.Settings!=null?$"Общие правила: {share.Settings.Rules.Count}. Режим подключения и маршрут по умолчанию заменят текущие настройки.\n":"")+"Работающее подключение останется прежним до переподключения.",TextWrapping=TextWrapping.Wrap});
                var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,24,0,0)};var add=new Button{Content="Сохранить",IsDefault=true,Margin=new Thickness(0,0,8,0)};var cancel=new Button{Content="Отмена",IsCancel=true};actions.Children.Add(add);actions.Children.Add(cancel);form.Children.Add(actions);add.Click+=(_,_)=>dialog.DialogResult=true;
                if(dialog.ShowDialog()==true){backend.ModernAcceptShare(share);foreach(var profile in backend.ModernStore.Profiles)pendingRules.Add(profile.Id);LoadProfileRules();RefreshFromBackend();Toast("QR-пакет сохранён. Общие правила применятся после переподключения VPN.");}
            }
            else if(text.StartsWith("https://",StringComparison.OrdinalIgnoreCase)||text.StartsWith("happ://add/",StringComparison.OrdinalIgnoreCase))await backend.ModernImportSubscription(text);
            else backend.ModernImportText(text);
            RefreshFromBackend();RefreshProfiles();
        }
        catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
        finally{rulesDialogOpen=false;RegisterActivity();}
    }
    async void HandleOverlayDrop(OverlayDrop data)
    {
        if(rulesDialogOpen){Toast("Сначала закрой текущее окно настройки.");return;}
        ShowFromTray();
        rulesDialogOpen=true;
        try
        {
            if(data.Files.Length>1)throw new FormatException("Перетаскивай по одному приложению или QR-коду.");
            string value=data.Files.FirstOrDefault()??data.Text;
            if(data.Files.Length>0&&new[]{".png",".jpg",".jpeg",".bmp",".gif",".tif",".tiff"}.Contains(Path.GetExtension(value).ToLowerInvariant())){await AcceptQrImage(QrImages.Load(value));return;}
            if(value.StartsWith("relaydesk-",StringComparison.Ordinal)||value.StartsWith("happ://",StringComparison.OrdinalIgnoreCase)||value.TrimStart().StartsWith('{')||new[]{"vless://","vmess://","trojan://","ss://"}.Any(p=>value.StartsWith(p,StringComparison.OrdinalIgnoreCase))){await AcceptQrText(value);return;}
            var candidate=OverlayDrops.Rule(value);
            if(Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https")
            {
                var chooser=new ThemedWindow{Title="Ссылка на оверлее",Owner=this,Width=500,Height=230,WindowStartupLocation=WindowStartupLocation.CenterOwner};var panel=new StackPanel{Margin=new Thickness(24)};chooser.Content=panel;panel.Children.Add(new TextBlock{Text=uri.Host,FontSize=22,Margin=new Thickness(0,0,0,18)});var site=new Button{Content="Настроить маршрут сайта",Margin=new Thickness(0,0,0,8)};var subscription=new Button{Content="Добавить как подписку"};panel.Children.Add(site);panel.Children.Add(subscription);site.Click+=(_,_)=>chooser.DialogResult=true;subscription.Click+=(_,_)=>{chooser.Tag="subscription";chooser.DialogResult=true;};if(chooser.ShowDialog()!=true)return;if(chooser.Tag is string){await backend.ModernImportSubscription(value);RefreshFromBackend();return;}
            }
            LoadProfileRules();var existing=state.Rules.FirstOrDefault(r=>r.Kind!="default"&&RuleMatching.SameTarget(r,candidate));OpenRuleEditor(existing,candidate.Kind,candidate,true);
            SelectPage(2);
        }
        catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
        finally{rulesDialogOpen=false;RegisterActivity();}
    }
}
