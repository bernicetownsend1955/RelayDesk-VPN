using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RelayDesk;

static partial class Program
{
    static void SetClipboard(Action action){for(int retry=0;;retry++){try{action();return;}catch(System.Runtime.InteropServices.ExternalException)when(retry<49){Pump(50);}}}
    static void SetText(string text)=>SetClipboard(()=>Clipboard.SetText(text));
    static void ClipboardChecks(MainWindow window,Workspace backend,string folder)
    {
        // Snapshot clipboard data in memory before replacing it with synthetic fixtures.
        var original=Clipboard.GetDataObject();var backup=new DataObject();
        if(original!=null)foreach(string format in original.GetFormats(false))
        {
            object? value=original.GetData(format,false);if(value==null)continue;
            if(value is BitmapSource bitmap){value=bitmap.CloneCurrentValue();((BitmapSource)value).Freeze();}
            else if(value is Stream stream){long position=stream.CanSeek?stream.Position:0;if(stream.CanSeek)stream.Position=0;using var copy=new MemoryStream();stream.CopyTo(copy);if(stream.CanSeek)stream.Position=position;value=new MemoryStream(copy.ToArray());}
            else if(value is string[] files)value=files.ToArray();
            backup.SetData(format,value);
        }
        const string link="vless://00000000-0000-4000-8000-000000000001@vpn.example.org:443?security=none&type=tcp#Clipboard-QA";
        string profiles=File.ReadAllText(Path.Combine(folder,"profiles.json")),settings=File.ReadAllText(Path.Combine(folder,"settings.json"));
        void Expect(Type type,Action action,string name)
        {
            bool seen=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            timer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>type.IsInstanceOfType(w));if(dialog==null)return;seen=true;dialog.Close();timer.Stop();};
            timer.Start();try{action();Wait(()=>seen);}finally{timer.Stop();}Check(seen,name);
        }
        try
        {
            Call(window,"SelectPage",1);
            SetText(link);Expect(typeof(ImportWindow),()=>Call(backend,"Paste",window,new RoutedEventArgs()),"Existing profile clipboard action previews a VLESS link");
            SetText(backend.ModernProfile!.Config);Expect(typeof(ImportWindow),()=>Call(backend,"Paste",window,new RoutedEventArgs()),"Existing profile clipboard action recognizes full JSON");
            SetText("https://subscription.example.org/test");Expect(typeof(SubscriptionWindow),()=>Call(backend,"Paste",window,new RoutedEventArgs()),"HTTPS clipboard subscription opens confirmation before making a request");
            SetText("happ://add/https://subscription.example.org/test");Expect(typeof(SubscriptionWindow),()=>Call(backend,"Paste",window,new RoutedEventArgs()),"Happ subscription URL works through existing clipboard import");
            SetClipboard(()=>Clipboard.SetImage(QrImages.Encode(link)));Expect(typeof(ImportWindow),()=>Call(window,"ReadQrClipboard",window,new RoutedEventArgs()),"QR image actually read from Windows clipboard reaches import preview");
            SetText(link);Expect(typeof(ImportWindow),()=>Call(window,"PasteFromClipboard",window,new RoutedEventArgs()),"Main clipboard action imports protocol link with preview");
            Call(window,"SelectPage",2);SetText("https://clipboard.example.org/path");Expect(typeof(DropRouteWindow),()=>Click(window,"Вставить"),"Apps Paste button opens VPN, zapret and direct route menu for copied website");
            Call(window,"SelectPage",4);((Button)window.FindName("HomeNav")).Focus();Pump();
            SetText("https://clipboard.example.org/path");Expect(typeof(DropRouteWindow),()=>ApplicationCommands.Paste.Execute(null,window),"Ctrl+V routed paste command opens site route menu outside input fields");
            var field=(TextBox)window.FindName("ConnectionSearch");string before=field.Text;field.Focus();field.SelectAll();SetText("clipboard-field-fixture");ApplicationCommands.Paste.Execute(null,field);Pump();Check(field.Text=="clipboard-field-fixture","Ctrl+V inside text input retains normal text editing");field.Text=before;
            Call(window,"SelectPage",1);((Button)window.FindName("HomeNav")).Focus();SetText("https://subscription.example.org/test");Expect(typeof(SubscriptionWindow),()=>ApplicationCommands.Paste.Execute(null,window),"Ctrl+V on Profiles keeps subscription import context");
            SetClipboard(Clipboard.Clear);Call(window,"PasteFromClipboard",window,new RoutedEventArgs());Check(((TextBlock)window.FindName("Notice")).Text.Contains("В буфере нет"),"Empty clipboard produces an explicit message");
            Check(File.ReadAllText(Path.Combine(folder,"profiles.json"))==profiles&&File.ReadAllText(Path.Combine(folder,"settings.json"))==settings,"Cancelling all clipboard previews preserves profiles and settings byte-for-byte");
        }
        finally{if(backup.GetFormats(false).Length==0)SetClipboard(Clipboard.Clear);else SetClipboard(()=>Clipboard.SetDataObject(backup,true));}
    }
}
