using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RelayDesk;

public partial class Workspace
{
    readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    CancellationTokenSource updateCancel = new();
    bool updatesBusy, installingUpdate;
    ComponentUpdate? singUpdate, zapretUpdate;
    void InitializeUpdates()
    {
        AutoUpdates.IsChecked = store.Settings.CheckComponentUpdates;
        updateTimer.Tick += (_, _) => { if (store.Settings.CheckComponentUpdates) Safe(CheckUpdates); };
        if (store.Settings.CheckComponentUpdates && Environment.GetEnvironmentVariable("RELAYDESK_DATA_DIR") == null) { updateTimer.Start(); Safe(CheckUpdates); }
    }
    void RefreshUpdateButtons()
    {
        var ready = !updatesBusy && !session.Busy && !automationBusy && !session.ProxyRunning && !session.ZapretRunning;
        InstallSingButton.IsEnabled = ready && singUpdate?.Available == true;
        InstallZapretButton.IsEnabled = ready && zapretUpdate?.Available == true;
        InstallRequiredButton.IsEnabled = ready;
        InstallAppButton.IsEnabled = InstallAppBannerButton.IsEnabled = !updatesBusy && !session.Busy && !automationBusy && !closing && appRelease?.Available == true;
        CheckUpdatesButton.IsEnabled = !updatesBusy;
        AutoUpdates.IsEnabled = !installingUpdate;
    }
    async Task CheckUpdates()
    {
        if (updatesBusy || closing) return;
        updatesBusy = true; RefreshUpdateButtons(); UpdatesStatus.Text = "Проверяю официальные источники…";
        updateCancel.Dispose(); updateCancel = new(); var checker = new ComponentUpdates(store);
        int errors = 0;
        try
        {
            if (!await CheckAppUpdates(updateCancel.Token)) errors++;
            try { singUpdate = await checker.CheckSingBox(updateCancel.Token); SingUpdateStatus.Text = Describe(singUpdate); }
            catch (Exception ex) when (!updateCancel.IsCancellationRequested) { singUpdate = null; errors++; SingUpdateStatus.Text = "Не удалось проверить: " + ex.Message; }
            try { zapretUpdate = await checker.CheckZapret(updateCancel.Token); ZapretUpdateStatus.Text = Describe(zapretUpdate); }
            catch (Exception ex) when (!updateCancel.IsCancellationRequested) { zapretUpdate = null; errors++; ZapretUpdateStatus.Text = "Не удалось проверить: " + ex.Message; }
            var available = new[] { singUpdate, zapretUpdate }.Where(u => u?.Available == true).Select(u => u!.Name).ToArray();
            UpdateBanner.Visibility = available.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateNotice.Text = "Доступна установка: " + string.Join(", ", available) + ". Требуется подтверждение.";
            UpdatesStatus.Text = $"Проверено {DateTime.Now:dd.MM.yyyy HH:mm}. Ошибок: {errors}. Ничего не установлено.";
        }
        catch (OperationCanceledException) { UpdatesStatus.Text = "Проверка обновлений отменена."; }
        finally { updatesBusy = false; RefreshUpdateButtons(); }
    }
    static string Describe(ComponentUpdate update) => "Сейчас: " + update.Current + "\nВ источнике: " + ComponentUpdates.RevisionLabel(update.Revision) + "\n" + update.Status;
    void GuardUpdate()
    {
        if (closing || updatesBusy || session.Busy || automationBusy || session.ProxyRunning || session.ZapretRunning) throw new InvalidOperationException("Сначала останови VPN и zapret и дождись завершения операций.");
    }
    async Task Install(ComponentUpdate? update)
    {
        GuardUpdate();
        if (update == null || !update.Available) throw new InvalidOperationException("Сначала проверь обновления.");
        var version = ComponentUpdates.RevisionLabel(update.Revision);
        if (new UpdateConsentWindow(update.Name, version) { Owner = Window.GetWindow(this) }.ShowDialog() != true) return;
        updatesBusy = installingUpdate = true; RefreshUpdateButtons(); updateCancel.Dispose(); updateCancel = new();
        Tabs.SelectedIndex = 6;
        foreach (System.Windows.Controls.TabItem tab in Tabs.Items) tab.IsEnabled = tab.Header?.ToString() == "Обновления";
        ProfileList.IsEnabled = false; UpdateStatus();
        var old = update.Id == "sing-box" ? store.Settings.Engines.GetValueOrDefault("sing-box", "") : store.Settings.ZapretExe;
        var receipt = store.Settings.ComponentInstalls.GetValueOrDefault(update.Id);
        var oldArgs = store.Settings.ZapretArgs; var oldStrategies = store.Settings.StrategyList;
        bool activated = false;
        try
        {
            var executable = await new ComponentUpdates(store).PrepareApproved(update, text => UpdatesStatus.Text = text, updateCancel.Token);
            if (session.Busy || automationBusy || session.ProxyRunning || session.ZapretRunning) throw new InvalidOperationException("Во время загрузки запущен компонент. Обновление не применено; останови его и повтори.");
            if (update.Id == "sing-box") await ValidateNewSingBox(executable, updateCancel.Token);
            updateCancel.Token.ThrowIfCancellationRequested();
            var initialStrategies = update.Id == "zapret" && string.IsNullOrWhiteSpace(store.Settings.ZapretArgs) ? FlowsealStrategies.Load(executable) : null;
            if (update.Id == "sing-box") store.Settings.Engines["sing-box"] = executable; else store.Settings.ZapretExe = executable;
            if (initialStrategies != null)
            {
                store.Settings.ZapretArgs = initialStrategies[0].Arguments;
                if (string.IsNullOrWhiteSpace(store.Settings.StrategyList)) store.Settings.StrategyList = FlowsealStrategies.Format(initialStrategies);
            }
            store.Settings.ComponentInstalls[update.Id] = new(update.Revision, executable, old, DateTimeOffset.UtcNow);
            try { store.Save(); activated = true; }
            catch { if (update.Id == "sing-box") store.Settings.Engines["sing-box"] = old; else store.Settings.ZapretExe = old; store.Settings.ZapretArgs = oldArgs; store.Settings.StrategyList = oldStrategies; if (receipt == null) store.Settings.ComponentInstalls.Remove(update.Id); else store.Settings.ComponentInstalls[update.Id] = receipt; throw; }
            if (update.Id == "sing-box") SingExe.Text = executable; else ZapretExe.Text = executable;
            if (update.Id == "zapret" && string.IsNullOrWhiteSpace(oldArgs)) { ZapretArgs.Text = store.Settings.ZapretArgs; StrategyList.Text = store.Settings.StrategyList; }
            UpdatesStatus.Text = update.Name + " обновлён. Запуск — отдельной кнопкой."; Write(UpdatesStatus.Text);
        }
        catch (Exception ex) { UpdatesStatus.Text = ex is OperationCanceledException ? "Обновление отменено. Прежний компонент сохранён." : "Обновление не применено: " + ex.Message; throw; }
        finally { updatesBusy = installingUpdate = false; foreach (System.Windows.Controls.TabItem tab in Tabs.Items) tab.IsEnabled = true; ProfileList.IsEnabled = true; RefreshSetup(); UpdateStatus(); }
        if (activated) await CheckUpdates();
    }
    async Task ValidateNewSingBox(string executable, CancellationToken cancellation)
    {
        UpdatesStatus.Text = "Проверяю сохранённые конфигурации новым sing-box…";
        var scratch = new Store(Path.Combine(store.Folder, "components", "validation-" + Guid.NewGuid().ToString("N")));
        scratch.Settings.Engines["sing-box"] = executable;
        var validator = new EngineSession(scratch, Write);
        foreach (var profile in store.Profiles.Where(p => p.Core == "sing-box"))
        {
            cancellation.ThrowIfCancellationRequested(); await validator.Check(profile);
            bool supportsCombined = profile.Managed || (JsonTools.Object(profile.Config)["inbounds"] as System.Text.Json.Nodes.JsonArray)?.Any(n => JsonTools.Text(n?["type"]) == "tun" && JsonTools.Text(n?["auto_route"]) == "true") == true;
            if (supportsCombined && store.Settings.ConditionalRouting)
            {
                var combined = RuntimePolicy.Config(profile, store.Settings, ConnectionMode.Combined);
                await validator.Check(new Profile { Config = combined });
            }
        }
    }
    void AutoUpdatesChanged(object sender, RoutedEventArgs e) => Safe(() =>
    {
        store.Settings.CheckComponentUpdates = AutoUpdates.IsChecked == true; store.Save();
        if (store.Settings.CheckComponentUpdates) { updateTimer.Start(); Safe(CheckUpdates); } else { updateTimer.Stop(); if (!installingUpdate) updateCancel.Cancel(); }
        return Task.CompletedTask;
    });
    void OpenUpdatesClick(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = 6;
    void CheckUpdatesClick(object sender, RoutedEventArgs e) => Safe(CheckUpdates);
    void InstallSingClick(object sender, RoutedEventArgs e) => Safe(() => Install(singUpdate));
    void InstallZapretClick(object sender, RoutedEventArgs e) => Safe(() => Install(zapretUpdate));
    void CancelUpdatesClick(object sender, RoutedEventArgs e) => updateCancel.Cancel();
    void ReleasePage(ComponentUpdate? update)
    {
        if (update == null) throw new InvalidOperationException("Сначала проверь обновления.");
        if (!update.Page.StartsWith("https://github.com/", StringComparison.Ordinal)) throw new InvalidDataException("Некорректная страница источника.");
        Process.Start(new ProcessStartInfo(update.Page) { UseShellExecute = true });
    }
    void SingReleaseClick(object sender, RoutedEventArgs e) => Safe(() => { ReleasePage(singUpdate); return Task.CompletedTask; });
    void ZapretReleaseClick(object sender, RoutedEventArgs e) => Safe(() => { ReleasePage(zapretUpdate); return Task.CompletedTask; });
    void Rollback(string id)
    {
        GuardUpdate(); var record = store.Settings.ComponentInstalls.GetValueOrDefault(id) ?? throw new InvalidOperationException("Предыдущая установка не сохранена.");
        if (!File.Exists(record.PreviousExecutable)) throw new InvalidOperationException("Прежний EXE недоступен. Укажи путь на вкладке «Ядра».");
        if (MessageBox.Show(Window.GetWindow(this), "Вернуть прежний путь к " + id + "?\n" + record.PreviousExecutable, "Откат компонента", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (id == "sing-box") { store.Settings.Engines[id] = record.PreviousExecutable; SingExe.Text = record.PreviousExecutable; } else { store.Settings.ZapretExe = record.PreviousExecutable; ZapretExe.Text = record.PreviousExecutable; }
        store.Settings.ComponentInstalls.Remove(id); store.Save(); UpdatesStatus.Text = "Прежний компонент выбран. Запуск — вручную.";
    }
    void RollbackSingClick(object sender, RoutedEventArgs e) => Safe(() => { Rollback("sing-box"); return Task.CompletedTask; });
    void RollbackZapretClick(object sender, RoutedEventArgs e) => Safe(() => { Rollback("zapret"); return Task.CompletedTask; });
}
