using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace RelayDesk;
public partial class Workspace
{
    public Task ModernRestartInterface(string mode)
    {
        if(mode is not ("classic" or "orbit"))throw new ArgumentException("Неизвестный интерфейс.");
        if(ModernBusy||updatesBusy||subscriptionBusy||pingBusy)throw new InvalidOperationException("Дождись завершения текущей операции.");
        var previous=store.Settings.UiMode;store.Settings.UiMode=mode;
        try
        {
            store.Save();var resume=CaptureUpdate();
            static string Literal(string value)=>"'"+value.Replace("'","''")+"'";
            string exe=Environment.ProcessPath??throw new InvalidOperationException("Не найден путь программы.");
            string script="$ErrorActionPreference='Stop'; Wait-Process -Id "+Environment.ProcessId+" -ErrorAction SilentlyContinue; Start-Process -FilePath "+Literal(exe)+" -WorkingDirectory "+Literal(Path.GetDirectoryName(exe)!)+" -ArgumentList @('--resume-update',"+Literal(resume.Token)+")";
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-WindowStyle","Hidden","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})start.ArgumentList.Add(arg);
            _=Process.Start(start)??throw new InvalidOperationException("Не удалось подготовить перезапуск.");
            updateClosing=true;Dispatcher.BeginInvoke(()=>Window.GetWindow(this).Close());return Task.CompletedTask;
        }
        catch{store.Settings.UiMode=previous;store.Save();throw;}
    }
}
