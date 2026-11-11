using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace RelayDesk;

public partial class MainWindow
{
    void InitializeWindowClipboard()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,PasteClipboardCommand,(_,e)=>
        {
            if(Keyboard.FocusedElement is TextBoxBase or PasswordBox)return;
            e.CanExecute=!rulesDialogOpen&&!backend.ModernBusy;e.Handled=true;
        }));
    }
    void PasteClipboardCommand(object sender,ExecutedRoutedEventArgs e){e.Handled=true;PasteFromClipboard(sender,e);}
    async void PasteFromClipboard(object sender,RoutedEventArgs e)
    {
        if(rulesDialogOpen||backend.ModernBusy){Toast("Сначала заверши текущую настройку.");return;}
        WakeQuietMode();
        try
        {
            var clipboard=await ClipboardContent.Read();
            if(clipboard.Files.Length>0){HandleWindowDrop(new(clipboard.Files,""));return;}
            if(clipboard.Image!=null){await AcceptQrImage(clipboard.Image);return;}
            if(string.IsNullOrWhiteSpace(clipboard.Text)){Toast("В буфере нет ссылки, конфигурации или QR-кода.");return;}
            string text=clipboard.Text.Trim();
            if(currentPage!=1&&BrowserAddressDrag.Normalize(text) is string address){HandleWindowDrop(new([],address));return;}
            await AcceptQrText(text);
        }
        catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
    }
}
