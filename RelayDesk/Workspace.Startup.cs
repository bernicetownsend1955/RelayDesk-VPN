using System.IO;
using System.Net.NetworkInformation;
using System.Windows;

namespace RelayDesk;

public partial class Workspace
{
    readonly CancellationTokenSource startupCancel = new();
    bool startupWaiting;
    void RefreshStartupProfiles()
    {
        if (StartupProfileChoice == null) return;
        var id = (StartupProfileChoice.SelectedItem as Profile)?.Id ?? store.Settings.StartupProfileId;
        StartupProfileChoice.ItemsSource = store.Profiles.Where(p => p.Core != "zapret").ToList();
        StartupProfileChoice.SelectedItem = store.Profiles.FirstOrDefault(p => p.Id == id) ?? (id.Length == 0 ? selected ?? store.Profiles.FirstOrDefault(p => p.Core != "zapret") : null);
    }
    void InitializeStartup()
    {
        WindowsStartupToggle.IsChecked = store.Settings.WindowsStartup;
        StartupVpnToggle.IsChecked = store.Settings.StartupVpn; StartupZapretToggle.IsChecked = store.Settings.StartupZapret;
        RefreshStartupProfiles();
        StartupStatus.Text = store.Settings.WindowsStartup ? "Автозапуск включён для текущей учётной записи Windows." : "Автозапуск выключен.";
        if (store.Settings.WindowsStartup && Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") == null)
        {
            try { StartupRegistration.Apply(true, Environment.ProcessPath!); }
            catch (Exception ex) { StartupStatus.Text = "Не удалось настроить задачу Windows: " + ex.Message; Write(StartupStatus.Text); }
        }
        if (App.WindowsStartupLaunch && Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") == null) Safe(RunWindowsStartup);
    }
    void SaveStartupClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        try { SaveStartupSettings(); }
        catch (Exception ex) { StartupStatus.Text = ex.Message; throw; }
        return Task.CompletedTask;
    });
    void SaveStartupSettings()
    {
        if (session.Busy || automationBusy || installingUpdate) throw new InvalidOperationException("Дождись завершения текущей операции.");
        var enabled = WindowsStartupToggle.IsChecked == true;
        var vpn = StartupVpnToggle.IsChecked == true; var zapret = StartupZapretToggle.IsChecked == true;
        var profile = StartupProfileChoice.SelectedItem as Profile;
        if (enabled && vpn && profile == null) throw new InvalidOperationException("Выбери профиль для автоматического подключения VPN.");
        if (enabled && vpn && !File.Exists(session.Executable(profile!))) throw new InvalidOperationException("Сначала установи ядро выбранного профиля.");
        if (enabled && zapret && (!File.Exists(store.Settings.ZapretExe) || string.IsNullOrWhiteSpace(store.Settings.ZapretArgs))) throw new InvalidOperationException("Сначала установи zapret и выбери стратегию.");
        var previous = (store.Settings.WindowsStartup, store.Settings.StartupVpn, store.Settings.StartupZapret, store.Settings.StartupProfileId);
        store.Settings.WindowsStartup = enabled; store.Settings.StartupVpn = vpn; store.Settings.StartupZapret = zapret; store.Settings.StartupProfileId = profile?.Id ?? "";
        try
        {
            store.Save();
            if (Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") == null) StartupRegistration.Apply(enabled, Environment.ProcessPath!);
        }
        catch
        {
            (store.Settings.WindowsStartup, store.Settings.StartupVpn, store.Settings.StartupZapret, store.Settings.StartupProfileId) = previous;
            store.Save(); throw;
        }
        StartupStatus.Text = enabled ? "Сохранено. RelayDesk запустится через 15 секунд после входа в Windows и включит выбранные компоненты, когда появится сеть." : "Автозапуск выключен. Задача Windows удалена.";
        if (!enabled) CancelWindowsStartup();
    }
    void CancelWindowsStartup()
    {
        if (!startupWaiting) return;
        startupCancel.Cancel(); StartupStatus.Text = "Автоматическое подключение отменено.";
    }
    async Task RunWindowsStartup()
    {
        if (!store.Settings.WindowsStartup || !store.Settings.StartupVpn && !store.Settings.StartupZapret) return;
        startupWaiting = true;
        try
        {
            StartupStatus.Text = "Автозапуск: ожидаю сетевой интерфейс…"; Write(StartupStatus.Text);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 && n.GetIPProperties().GatewayAddresses.Count > 0))
            {
                if (DateTime.UtcNow > deadline) throw new InvalidOperationException("Сеть не появилась за минуту. Подключись кнопками VPN / zapret.");
                await Task.Delay(1000, startupCancel.Token);
            }
            startupCancel.Token.ThrowIfCancellationRequested();
            if (closing || session.ProxyRunning || session.ZapretRunning) return;
            await Automatic(async cancellation =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, startupCancel.Token);
                var token = linked.Token;
                if (store.Settings.StartupVpn)
                {
                    var profile = store.Profiles.FirstOrDefault(p => p.Id == store.Settings.StartupProfileId && p.Core != "zapret") ?? throw new InvalidOperationException("Профиль автозапуска удалён. Выбери его заново в настройках.");
                    if (store.Settings.AutoServer) profile = await PickServer(token);
                    token.ThrowIfCancellationRequested();
                    selected = profile; loading = true; RefreshProfiles(); ProfileList.SelectedItem = profile; loading = false; DisplayProfile();
                    await session.Connect(profile, store.Settings.StartupZapret);
                }
                else if (store.Settings.StartupZapret) await session.StartZapret();
                if (token.IsCancellationRequested) { await session.StopAll(); token.ThrowIfCancellationRequested(); }
            });
            StartupStatus.Text = "Автозапуск: " + RuntimePolicy.Label(session.Mode); Write(StartupStatus.Text);
        }
        catch (OperationCanceledException) { StartupStatus.Text = "Автоматическое подключение отменено."; }
        catch (Exception ex) { StartupStatus.Text = "Автозапуск: " + ex.Message; Write(StartupStatus.Text); ActionMessage.Text = StartupStatus.Text; }
        finally { startupWaiting = false; }
    }
}
