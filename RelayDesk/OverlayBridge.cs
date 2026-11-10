using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using Microsoft.Win32.SafeHandles;

namespace RelayDesk;
public interface ITrafficOverlay
{
    void Show();void Close();void Configure(Preferences settings);void Place(Preferences settings);void Update(string name,TrafficRate? rate,string status);
}
public sealed class OverlayPacket
{
    public string Kind {get;set;}="";public Preferences? Appearance {get;set;}public string Name {get;set;}="";public string Status {get;set;}="";public TrafficRate? Rate {get;set;}public OverlayDrop? Drop {get;set;}public double Left {get;set;}public double Top {get;set;}public long TargetWindow {get;set;}
}
public sealed class OverlayBridge:ITrafficOverlay
{
    readonly Action<double,double> moved;readonly Action settings,hide,scan;readonly Action<OverlayDrop> dropped;readonly Action<string> error;
    readonly CancellationTokenSource cancel=new();readonly Channel<OverlayPacket> outgoing=Channel.CreateUnbounded<OverlayPacket>();
    readonly string name="RelayDesk.Overlay."+Guid.NewGuid().ToString("N"),token=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    Process? child;bool closed;readonly bool dropMode;
    public string DisplayedName {get;private set;}="";
    public OverlayBridge(Action<double,double> moved,Action settings,Action hide,Action<OverlayDrop> dropped,Action<string> error,Action scan,long dropTarget=0){this.moved=moved;this.settings=settings;this.hide=hide;this.dropped=dropped;this.error=error;this.scan=scan;dropMode=dropTarget!=0;if(dropMode)Send(new(){Kind="drop-target",TargetWindow=dropTarget});}
    public static Preferences Appearance(Preferences p)=>new(){OverlayOpacity=p.OverlayOpacity,OverlayTextSize=p.OverlayTextSize,OverlayCorner=p.OverlayCorner,OverlayLeft=p.OverlayLeft,OverlayTop=p.OverlayTop,OverlaySharks=p.OverlaySharks,OverlaySharkMotion=p.OverlaySharkMotion,OverlaySharkOpacity=p.OverlaySharkOpacity};
    public void Configure(Preferences p)=>Send(new(){Kind="configure",Appearance=Appearance(p)});
    public void Place(Preferences p)=>Send(new(){Kind="place",Appearance=Appearance(p)});
    public void Update(string connection,TrafficRate? rate,string status)=>Send(new(){Kind="update",Name=connection,Rate=rate,Status=status});
    void Send(OverlayPacket packet){if(!closed)outgoing.Writer.TryWrite(packet);}
    public void Show()=>_=Run();
    public void Close(){if(closed)return;closed=true;cancel.Cancel();outgoing.Writer.TryComplete();}
    async Task Run()
    {
        var security=new PipeSecurity();var sid=WindowsIdentity.GetCurrent().User!;security.SetOwner(sid);security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(sid,PipeAccessRights.FullControl,AccessControlType.Allow));
        using var server=NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,0,0,security);
        try
        {
            child=StartDesktopProcess($"--overlay-host {Environment.ProcessId} {name} {token}");
            using var startup=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);startup.CancelAfter(TimeSpan.FromSeconds(15));await server.WaitForConnectionAsync(startup.Token);
            if(!GetNamedPipeClientProcessId(server.SafePipeHandle,out var client)||client!=child.Id)throw new InvalidOperationException("Оверлей подключился из другого процесса.");
            using var reader=new StreamReader(server,leaveOpen:true);using var writer=new StreamWriter(server,leaveOpen:true){AutoFlush=true};
            if(await reader.ReadLineAsync(startup.Token)!=token)throw new InvalidOperationException("Оверлей не прошёл проверку подключения.");
            var receive=Task.Run(async()=>
            {
                while(!cancel.IsCancellationRequested)
                {
                    var line=await ReadLine(reader,cancel.Token);if(line==null)break;
                    var packet=JsonSerializer.Deserialize<OverlayPacket>(line);if(packet==null)continue;
                    await Application.Current.Dispatcher.InvokeAsync(()=>
                    {
                        if(closed)return;
                        if(packet.Kind=="ready"){DisplayedName=packet.Name;if(dropMode&&packet.Name.StartsWith("drop-error: ",StringComparison.Ordinal))error(packet.Name[12..]);}
                        else if(packet.Kind=="move"&&double.IsFinite(packet.Left)&&double.IsFinite(packet.Top))moved(packet.Left,packet.Top);
                        else if(packet.Kind=="settings")settings();else if(packet.Kind=="hide")hide();else if(packet.Kind=="qr-screen")scan();else if(packet.Kind=="drop"&&packet.Drop!=null)dropped(packet.Drop);
                    });
                }
            },cancel.Token);
            var send=Task.Run(async()=>{await foreach(var packet in outgoing.Reader.ReadAllAsync(cancel.Token))await writer.WriteLineAsync(JsonSerializer.Serialize(packet).AsMemory(),cancel.Token);},cancel.Token);
            await Task.WhenAny(send,receive);cancel.Cancel();try{await Task.WhenAll(send,receive);}catch(OperationCanceledException){}
            if(!closed)await Application.Current.Dispatcher.InvokeAsync(hide);
        }
        catch(OperationCanceledException)when(closed){}
        catch(Exception ex){if(!closed)await Application.Current.Dispatcher.InvokeAsync(()=>{hide();error((dropMode?"Перетаскивание не включено: ":"Оверлей не запущен: ")+ProgramJournal.Clean(ex.Message));});}
        finally{closed=true;cancel.Cancel();server.Dispose();if(child!=null){try{if(!child.HasExited){using var timeout=new CancellationTokenSource(1500);try{await child.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){child.Kill();}}}catch(InvalidOperationException){}child.Dispose();}}
    }
    static async Task<string?> ReadLine(StreamReader reader,CancellationToken cancellation)
    {
        var buffer=new System.Text.StringBuilder();var character=new char[1];
        while(await reader.ReadAsync(character.AsMemory(),cancellation)>0){if(character[0]=='\n')return buffer.ToString().TrimEnd('\r');if(buffer.Length>=200_000)throw new InvalidDataException("Слишком большое сообщение оверлея.");buffer.Append(character[0]);}
        return buffer.Length==0?null:throw new EndOfStreamException();
    }
    public static async Task Host(string[] args)
    {
        try
        {
            if(args.Length!=4||!int.TryParse(args[1],out int parent)||parent<=0||!args[2].StartsWith("RelayDesk.Overlay.",StringComparison.Ordinal)||args[2].Length!=50||args[3].Length!=64||!args[3].All(char.IsAsciiHexDigit))throw new InvalidDataException("Некорректный запуск оверлея.");
            using var pipe=new NamedPipeClientStream(".",args[2],PipeDirection.InOut,PipeOptions.Asynchronous);using var cancel=new CancellationTokenSource();
            await pipe.ConnectAsync(15000).ConfigureAwait(false);using var reader=new StreamReader(pipe,leaveOpen:true);using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};await writer.WriteLineAsync(args[3]).ConfigureAwait(false);
            var messages=Channel.CreateUnbounded<OverlayPacket>();
            void Send(OverlayPacket packet)=>messages.Writer.TryWrite(packet);
            TrafficOverlayWindow? overlay=null;MainDropWindow? dropWindow=null;
            var sending=Task.Run(async()=>{await foreach(var packet in messages.Reader.ReadAllAsync(cancel.Token))await writer.WriteLineAsync(JsonSerializer.Serialize(packet).AsMemory(),cancel.Token);},cancel.Token);
            using var owner=Process.GetProcessById(parent);var monitor=Task.Run(async()=>{await owner.WaitForExitAsync(cancel.Token);cancel.Cancel();},cancel.Token);
            try
            {
                while(true)
                {
                    var line=await ReadLine(reader,cancel.Token);if(line==null)break;var packet=JsonSerializer.Deserialize<OverlayPacket>(line);if(packet==null)continue;
                    await Application.Current.Dispatcher.InvokeAsync(()=>
                    {
                        if(packet.Kind=="drop-target")
                        {
                            if(dropWindow!=null||overlay!=null)throw new InvalidDataException("Повторное назначение окна.");
                            dropWindow=new MainDropWindow(new IntPtr(packet.TargetWindow),parent,data=>Send(new(){Kind="drop",Drop=data}),state=>Send(new(){Kind="ready",Name=state}));
                            Send(new(){Kind="ready",Name="drop-target"});return;
                        }
                        if(dropWindow!=null)return;
                        if(overlay==null){overlay=new TrafficOverlayWindow((x,y)=>Send(new(){Kind="move",Left=x,Top=y}),()=>Send(new(){Kind="settings"}),()=>Send(new(){Kind="hide"}),data=>Send(new(){Kind="drop",Drop=data}),()=>Send(new(){Kind="qr-screen"}));Application.Current.MainWindow=overlay;overlay.Show();}
                        if(packet.Kind=="configure"&&packet.Appearance!=null)overlay!.Configure(packet.Appearance);
                        else if(packet.Kind=="place"&&packet.Appearance!=null)overlay!.Place(packet.Appearance);
                        else if(packet.Kind=="update"){overlay!.Update(packet.Name,packet.Rate,packet.Status);Send(new(){Kind="ready",Name=packet.Name});}
                    });
                }
            }
            finally{cancel.Cancel();await Application.Current.Dispatcher.InvokeAsync(()=>{overlay?.Close();dropWindow?.Dispose();});try{await Task.WhenAll(sending,monitor);}catch(OperationCanceledException){}}
        }
        catch(Exception){/* Host never changes profiles or starts cores. Parent reports launch errors. */}
        finally{await Application.Current.Dispatcher.InvokeAsync(()=>Application.Current.Shutdown());}
    }
    public static Process StartDesktopProcess(string arguments)
    {
        var executable=Path.Combine(AppContext.BaseDirectory,"RelayDesk.exe");
        if(!EngineSession.IsAdmin())return Process.Start(new ProcessStartInfo(executable){Arguments=arguments,UseShellExecute=false,WorkingDirectory=AppContext.BaseDirectory})!;
        GetWindowThreadProcessId(GetShellWindow(),out uint pid);if(pid==0)throw new InvalidOperationException("Нет сеанса Проводника Windows.");
        using var shell=Process.GetProcessById((int)pid);
        if(!OpenProcessToken(shell.Handle,0x000B,out var access))throw new System.ComponentModel.Win32Exception();
        using(access)
        {
            using var identity=new WindowsIdentity(access.DangerousGetHandle());using var current=WindowsIdentity.GetCurrent();if(identity.User!=current.User)throw new InvalidOperationException("Открой RelayDesk из своей учётной записи Windows.");
            if(!DuplicateTokenEx(access,0x02000000,IntPtr.Zero,2,1,out var primary))throw new System.ComponentModel.Win32Exception();
            using(primary)
            {
                var startup=new Startup{Size=Marshal.SizeOf<Startup>(),Desktop="winsta0\\default"};var command=new System.Text.StringBuilder('"'+executable+"\" "+arguments);
                if(!CreateProcessWithTokenW(primary,0,executable,command,0,IntPtr.Zero,AppContext.BaseDirectory,ref startup,out var created))throw new System.ComponentModel.Win32Exception();
                CloseHandle(created.Process);CloseHandle(created.Thread);return Process.GetProcessById((int)created.Id);
            }
        }
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Startup{public int Size;public string? Reserved,Desktop,Title;public int X,Y,XSize,YSize,XChars,YChars,Fill,Flags;public short Show,ReservedSize;public IntPtr ReservedPointer,Input,Output,Error;}
    [StructLayout(LayoutKind.Sequential)]struct Created{public IntPtr Process,Thread;public uint Id,ThreadId;}
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll",SetLastError=true)]static extern bool DuplicateTokenEx(SafeAccessTokenHandle token,uint access,IntPtr attributes,int impersonation,int type,out SafeAccessTokenHandle primary);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token,uint logon,string? application,System.Text.StringBuilder command,uint flags,IntPtr environment,string directory,ref Startup startup,out Created created);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
}
