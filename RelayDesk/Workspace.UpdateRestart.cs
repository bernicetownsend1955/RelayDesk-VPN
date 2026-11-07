using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public partial class Workspace
{
    readonly CancellationTokenSource updateHandoffCancel=new();
    bool updateClosing;
    public UpdateResume? RestoredUpdate {get;private set;}
    static readonly string[] draftTextFields=["ProfileName","Dns","LocalPort","ProxyAddress","TcpHost","TcpPort","ConfigEditor"];
    public UpdateResume CaptureUpdate(string? token=null)
    {
        var window=Window.GetWindow(this);
        var state=new UpdateResume{Token=token??Guid.NewGuid().ToString("N"),Vpn=session.ProxyRunning,Zapret=session.ZapretRunning,ProfileId=session.ActiveProfile?.Id??"",SelectedProfileId=selected?.Id??"",Hidden=window?.IsVisible==false,Minimized=window?.WindowState==WindowState.Minimized};
        if(window is MainWindow modern){state.Page=modern.UpdatePage;state.Section=modern.UpdateSection;}
        if(HasChanges())
        {
            foreach(var name in draftTextFields)state.Draft[name]=((TextBox)FindName(name)).Text;
            state.Draft["CoreChoice"]=CoreChoice.SelectedItem?.ToString()??"";
            state.Draft["Tun"]=(Tun.IsChecked==true).ToString();state.Draft["SystemProxy"]=(SystemProxy.IsChecked==true).ToString();state.Draft["AutoCandidate"]=(AutoCandidate.IsChecked==true).ToString();
        }
        UpdateRestart.Save(store.Folder,state);return state;
    }
    void InitializeUpdateHandoff()
    {
        _=ListenForInstaller();
        var token=App.UpdateResumeToken;
        if(token.Length>0)Safe(async()=>await ResumeAfterUpdate(token));
    }
    async Task ListenForInstaller()
    {
        try
        {
            while(!updateHandoffCancel.IsCancellationRequested)
            {
                await using var pipe=new NamedPipeServerStream("RelayDesk.Update."+Environment.ProcessId,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(updateHandoffCancel.Token);
                using var reader=new StreamReader(pipe,leaveOpen:true);using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
                var token=await reader.ReadLineAsync(updateHandoffCancel.Token);
                if(!Guid.TryParseExact(token,"N",out _)){await writer.WriteLineAsync("ERROR: invalid request");continue;}
                string result;
                if(closing||session.Busy||automationBusy||updatesBusy||subscriptionBusy||pingBusy)result="BUSY";
                else
                {
                    var state=CaptureUpdate(token);updateClosing=true;
                    result="READY";
                }
                await writer.WriteLineAsync(result);
                if(result=="READY"){_=Dispatcher.BeginInvoke(()=>Window.GetWindow(this).Close());return;}
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){Write("Подготовка к обновлению: "+ex.Message);}
    }
    public async Task ResumeAfterUpdate(string token)
    {
        var state=UpdateRestart.Take(store.Folder,token);if(state==null)return;
        RestoredUpdate=state;
        try
        {
            if(state.Vpn)
            {
                var profile=store.Profiles.FirstOrDefault(p=>p.Id==state.ProfileId)??throw new InvalidOperationException("Профиль прежнего подключения не найден.");
                if(!session.ProxyRunning)await session.Connect(profile,state.Zapret);
            }
            else if(state.Zapret&&!session.ZapretRunning)await session.StartZapret();
        }
        catch(Exception ex){Write("Восстановление подключения после обновления: "+ex.Message);ActionMessage.Text=ex.Message;}
        try
        {
            var pick=store.Profiles.FirstOrDefault(p=>p.Id==state.SelectedProfileId);
            if(pick!=null)ModernSelect(pick);
            foreach(var pair in state.Draft)if(FindName(pair.Key) is TextBox field)field.Text=pair.Value;
            if(state.Draft.TryGetValue("CoreChoice",out var core))CoreChoice.SelectedItem=core;
            foreach(var name in new[]{"Tun","SystemProxy","AutoCandidate"})if(state.Draft.TryGetValue(name,out var value)&&bool.TryParse(value,out var enabled))((CheckBox)FindName(name)).IsChecked=enabled;
            Write("После обновления: "+RuntimePolicy.Label(session.Mode));
            if(Window.GetWindow(this) is MainWindow modern)modern.RestoreUpdateView(state);
        }
        catch(Exception ex){Write("Восстановление после обновления: "+ex.Message);ActionMessage.Text=ex.Message;}
    }
}
