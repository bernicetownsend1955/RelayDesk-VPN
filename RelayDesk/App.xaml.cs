using System.Configuration;
using System.Data;
using System.Windows;

namespace RelayDesk;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? instance;
    public static bool WindowsStartupLaunch { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        WindowsStartupLaunch = e.Args.Contains("--windows-startup", StringComparer.Ordinal);
        var isolatedFolder = Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR");
        if (string.IsNullOrWhiteSpace(isolatedFolder) && !EngineSession.IsAdmin())
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = System.IO.Path.GetDirectoryName(Environment.ProcessPath!)! };
                foreach (var argument in e.Args) start.ArgumentList.Add(argument);
                System.Diagnostics.Process.Start(start);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { MessageBox.Show("Запуск отменён: Windows не получила разрешение администратора. Открой RelayDesk снова и подтверди запрос, чтобы использовать TUN и zapret.", "RelayDesk"); }
            catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show("Не удалось запросить права администратора: " + ex.Message, "RelayDesk"); }
            Shutdown(); return;
        }
        var suffix = string.IsNullOrWhiteSpace(isolatedFolder) ? "" : "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(isolatedFolder).ToUpperInvariant())))[..16];
        instance = new Mutex(true, @"Local\RelayDesk.Desktop" + suffix, out var created);
        if (!created) { if (!WindowsStartupLaunch) MessageBox.Show("RelayDesk уже открыт. Используй запущенное окно."); Shutdown(); return; }
        if (string.IsNullOrWhiteSpace(isolatedFolder))
        {
            try
            {
                var folder=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RelayDesk");
                if (PendingUpdates.StartApp(folder,AppContext.BaseDirectory)) { Shutdown(); return; }
            }
            catch(Exception ex) { ProgramJournal.Write("Автообновление не запущено: "+ex.Message,"updates"); }
        }
        ProgramJournal.Write("Приложение запущено.", "lifecycle");
        DispatcherUnhandledException += (_, error) => ProgramJournal.Write(error.Exception.ToString(), "crash");
        AppDomain.CurrentDomain.UnhandledException += (_, error) => ProgramJournal.Write(error.ExceptionObject.ToString() ?? "Unhandled exception", "crash");
        TaskScheduler.UnobservedTaskException += (_, error) => ProgramJournal.Write(error.Exception.ToString(), "task");
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { ProgramJournal.Write("Приложение закрыто.", "lifecycle"); instance?.Dispose(); base.OnExit(e); }
}

