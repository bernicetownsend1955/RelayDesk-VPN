using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public partial class Workspace
{
    bool automationBusy;
    CancellationTokenSource automationCancel = new();
    void LoadAutomation()
    {
        AutoServer.IsChecked = store.Settings.AutoServer; DiscordSplit.IsChecked = store.Settings.DiscordSplit;
        ConditionalRouting.IsChecked = store.Settings.ConditionalRouting;
        DiscordOnly.IsChecked = store.Settings.ZapretDiscordOnly; AutoZapret.IsChecked = store.Settings.AutoZapret;
        VoiceCidrs.Text = store.Settings.DiscordVoiceCidrs; VoiceEndpoint.Text = store.Settings.VoiceEndpoint; VoiceSsrc.Text = store.Settings.VoiceSsrc == 0 ? "" : store.Settings.VoiceSsrc.ToString();
        StrategyList.Text = store.Settings.StrategyList.Length > 0 ? store.Settings.StrategyList : ZapretAudit.DefaultStrategies;
        if (string.IsNullOrWhiteSpace(store.Settings.StrategyList) || store.Settings.StrategyList.Replace("\r", "").Trim() == ZapretAudit.DefaultStrategies.Trim())
        {
            try
            {
                var strategies = FlowsealStrategies.Load(store.Settings.ZapretExe);
                StrategyList.Text = FlowsealStrategies.Format(strategies);
                FlowsealChoice.ItemsSource = strategies;
                FlowsealChoice.SelectedIndex = 0;
                store.Settings.StrategyList = StrategyList.Text;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException) { AutomationStatus.Text = "Для проверки выбери набор Flowseal: " + ex.Message; }
        }
        ImportFolder.Text = store.Settings.ImportFolder; SyncImports.IsChecked = store.Settings.SyncOnStart;
        ApplicationList.ItemsSource = store.Settings.ZapretApplications.ToList();
        UpdateConditionalControls();
        if (store.Settings.LastDiscordAuditSummary.Length > 0) AutomationStatus.Text = store.Settings.LastDiscordAuditSummary;
    }
    string BuildPreview(Profile p)
    {
        return JsonTools.Build(p, RuntimePolicy.Settings(store.Settings, session.ZapretRunning ? ConnectionMode.Combined : ConnectionMode.VpnOnly));
    }
    void SaveAutomation(bool validateZapret = true)
    {
        if (session.Busy || automationBusy || installingUpdate) throw new InvalidOperationException("Дождись завершения переключения, проверки или установки.");
        if (validateZapret || DiscordSplit.IsChecked == true) _ = DiscordScope.Cidrs(VoiceCidrs.Text);
        var applications = ApplicationRoutes.Paths(ApplicationList.Items.Cast<string>());
        var strategies = validateZapret ? ZapretAudit.Parse(StrategyList.Text) : new List<Strategy>();
        uint ssrc = store.Settings.VoiceSsrc;
        if (validateZapret)
        {
            ssrc = 0;
            if (VoiceSsrc.Text.Trim().Length > 0 && !uint.TryParse(VoiceSsrc.Text, out ssrc)) throw new FormatException("SSRC должен быть целым числом от 1 до 4294967295.");
            if ((VoiceEndpoint.Text.Trim().Length > 0) != (ssrc > 0)) throw new FormatException("Для UDP-проверки укажи вместе endpoint и ненулевой SSRC либо оставь оба поля пустыми.");
            if (VoiceEndpoint.Text.Trim().Length > 0 && !System.Net.IPEndPoint.TryParse(VoiceEndpoint.Text.Trim(), out _)) throw new FormatException("Голосовой endpoint: IP:порт.");
        }
        var scope = new Preferences { ZapretDiscordOnly = DiscordOnly.IsChecked == true, DiscordVoiceCidrs = VoiceCidrs.Text };
        foreach (var strategy in strategies) _ = DiscordScope.Arguments(scope, strategy.Arguments);
        store.Settings.AutoServer = AutoServer.IsChecked == true; store.Settings.DiscordSplit = DiscordSplit.IsChecked == true;
        store.Settings.ConditionalRouting = ConditionalRouting.IsChecked == true;
        store.Settings.ZapretApplications = applications.ToList();
        store.Settings.ZapretDiscordOnly = DiscordOnly.IsChecked == true; store.Settings.AutoZapret = AutoZapret.IsChecked == true;
        store.Settings.DiscordVoiceCidrs = VoiceCidrs.Text; store.Settings.StrategyList = StrategyList.Text;
        store.Settings.VoiceEndpoint = VoiceEndpoint.Text.Trim(); store.Settings.VoiceSsrc = ssrc;
        SaveGlobal();
    }
    void SaveAutomationClick(object sender, RoutedEventArgs e) => Safe(() => { if (HasChanges()) throw new InvalidOperationException("Сначала сохрани изменения профиля."); SaveAutomation(); DisplayProfile(); AutomationStatus.Text = "Настройки сохранены. Работающие подключения не менялись; правила применятся при следующем запуске или переключении режима."; return Task.CompletedTask; });
    async Task Automatic(Func<CancellationToken, Task> action, bool allowVpn = false)
    {
        if (automationBusy || session.Busy || !allowVpn && session.ProxyRunning || installingUpdate) throw new InvalidOperationException("Дождись текущей операции; для запуска нового VPN отключи прежнее подключение RelayDesk.");
        automationCancel.Dispose(); automationCancel = new(); automationBusy = true; UpdateStatus();
        try { await action(automationCancel.Token); }
        finally { automationBusy = false; UpdateStatus(); }
    }
    void Scores(List<ServerScore> scores)
    {
        AutomationResults.ItemsSource = scores.Select(s => s.Profile.Name + " / " + s.Result).ToList();
    }
    async Task<Profile> PickServer(CancellationToken cancellation)
    {
        AutomationStatus.Text = "Проверяю серверы: три HTTPS-запроса на каждый…";
        var results = await ServerPicker.Measure(store, Write, cancellation); Scores(results);
        var best = ServerPicker.Best(results) ?? throw new InvalidOperationException("Ни один сервер не дал два успешных HTTPS-ответа. Подключение не запущено.");
        AutomationStatus.Text = "Выбран: " + best.Name + ". Автоматических переключений после подключения нет.";
        return best;
    }
    async Task<bool> Audit(CancellationToken cancellation)
    {
        AutomationStatus.Text = "Проверяю Discord без zapret и с каждой стратегией…";
        var reports = await session.AuditZapret(cancellation);
        AutomationResults.ItemsSource = reports.Select(r => $"{r.Name}: {r.Api}; {r.Gateway}; {r.Voice}" + (r.Ms.HasValue ? $" / {r.Ms} мс" : "")).ToList();
        var baseline = reports.First();
        var best = ZapretAudit.Best(reports);
        var passed = reports.Skip(1).Count(r => r.Passed);
        string summary = baseline.Passed ? "Итог: API и Gateway доступны без zapret. Стратегия не изменена; голос смотри отдельно." : best != null ? $"Итог: успешно {passed} из {reports.Count - 1}. Сохранена {best.Name}: {best.Ms} мс. Голос смотри отдельно; аудио не проверялось." : $"Итог: успешно 0 из {reports.Count - 1}. Подходящая стратегия не найдена. Настройки сохранены прежними.";
        var report = new
        {
            checkedAtUtc = DateTimeOffset.UtcNow,
            summary,
            selected = baseline.Passed ? null : best?.Name,
            successfulStrategies = passed,
            testedStrategies = reports.Count - 1,
            results = reports.Select(r => new { r.Name, api = ProgramJournal.Clean(r.Api), gateway = ProgramJournal.Clean(r.Gateway), r.Voice, r.Ms, r.Passed })
        };
        var reportFile = Path.Combine(store.Folder, "discord-audit-last.json");
        File.WriteAllText(reportFile + ".tmp", System.Text.Json.JsonSerializer.Serialize(report, JsonTools.Options));
        File.Move(reportFile + ".tmp", reportFile, true);
        store.Settings.LastDiscordAuditSummary = summary;
        AutomationStatus.Text = summary;
        if (baseline.Passed) { store.Save(); Write(summary); Write("Отчёт Discord сохранён: " + reportFile); return false; }
        if (best == null) { store.Save(); Write(summary); Write("Отчёт Discord сохранён: " + reportFile); throw new InvalidOperationException(summary); }
        store.Settings.ZapretArgs = best.Arguments; ZapretArgs.Text = best.Arguments; store.Save();
        Write(summary); Write("Отчёт Discord сохранён: " + reportFile);
        return true;
    }
    async Task ConnectAutomatically(Profile profile)
    {
        SaveAutomation(validateZapret: WithZapret.IsChecked == true);
        await Automatic(async cancellation =>
        {
            if (store.Settings.SyncOnStart) { SyncApproved(); profile = store.Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? profile; }
            bool withZapret = WithZapret.IsChecked == true;
            if (withZapret && store.Settings.AutoZapret && !session.ZapretRunning) withZapret = await Audit(cancellation);
            if (store.Settings.AutoServer) profile = await PickServer(cancellation);
            cancellation.ThrowIfCancellationRequested();
            selected = profile; loading = true; RefreshProfiles(); ProfileList.SelectedItem = profile; loading = false; DisplayProfile();
            await session.Connect(profile, withZapret);
            if (cancellation.IsCancellationRequested) { await session.StopAll(); cancellation.ThrowIfCancellationRequested(); }
        });
    }
    void AuditDiscordClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        if (session.ZapretRunning || session.Busy || automationBusy) throw new InvalidOperationException("Останови только zapret перед сравнением. VPN можно оставить включённым.");
        var strategies = ZapretAudit.Parse(StrategyList.Text);
        var voiceCidrs = DiscordScope.Cidrs(VoiceCidrs.Text);
        foreach (var strategy in strategies) _ = DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = true, DiscordVoiceCidrs = VoiceCidrs.Text }, strategy.Arguments);
        var endpoint = VoiceEndpoint.Text.Trim(); uint ssrc = 0;
        if (VoiceSsrc.Text.Trim().Length > 0 && !uint.TryParse(VoiceSsrc.Text.Trim(), out ssrc)) throw new FormatException("Некорректный SSRC.");
        if ((endpoint.Length > 0) != (ssrc > 0) || endpoint.Length > 0 && !System.Net.IPEndPoint.TryParse(endpoint, out _)) throw new FormatException("Для проверки голоса нужны вместе IP:порт и SSRC текущей сессии, либо оставь оба поля пустыми.");
        store.Settings.ZapretExe = ZapretExe.Text.Trim(); store.Settings.StrategyList = StrategyList.Text;
        store.Settings.DiscordVoiceCidrs = VoiceCidrs.Text; store.Settings.VoiceEndpoint = endpoint; store.Settings.VoiceSsrc = ssrc; store.Save();
        await Automatic(async c => { await Audit(c); }, allowVpn: true);
    });
    void MeasureServersClick(object sender, RoutedEventArgs e) => Safe(async () => { SaveEngines(); SaveAutomation(); await Automatic(async c => { await PickServer(c); }); });
    void CancelAutomationClick(object sender, RoutedEventArgs e) { automationCancel.Cancel(); AutomationStatus.Text = "Отмена проверки…"; }
    void LoadFlowsealClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (automationBusy || installingUpdate) throw new InvalidOperationException("Дождись завершения текущей проверки или установки.");
        var strategies = FlowsealStrategies.Load(ZapretExe.Text.Trim());
        StrategyList.Text = FlowsealStrategies.Format(strategies);
        FlowsealChoice.ItemsSource = strategies;
        store.Settings.StrategyList = StrategyList.Text; store.Save();
        AutomationStatus.Text = $"Загружено TCP-стратегий Flowseal: {strategies.Count}. BAT-файлы прочитаны как текст. Запусти проверку Discord.";
        return Task.CompletedTask;
    });
    void ApplyFlowsealClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ZapretRunning || session.Busy || automationBusy || installingUpdate) throw new InvalidOperationException("Останови только zapret и дождись завершения операции. VPN можно оставить включённым.");
        if (FlowsealChoice.SelectedItem is not Strategy strategy) throw new InvalidOperationException("Загрузи набор Flowseal и выбери TCP-стратегию.");
        _ = DiscordScope.Arguments(new Preferences { ZapretDiscordOnly = true }, strategy.Arguments);
        store.Settings.ZapretArgs = strategy.Arguments; ZapretArgs.Text = strategy.Arguments; store.Save();
        AutomationStatus.Text = "Выбрана " + strategy.Name + ". Проверка и запуск — отдельными кнопками.";
        return Task.CompletedTask;
    });
    void ToggleVpnClick(object sender, RoutedEventArgs e)
    {
        if (session.ProxyRunning) DisconnectClick(sender, e); else ConnectClick(sender, e);
    }
    void ToggleZapretClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        CancelWindowsStartup();
        if (session.Busy || automationBusy || installingUpdate) throw new InvalidOperationException("Дождись завершения текущей операции.");
        if (session.ZapretRunning) { await session.StopZapret(); return; }
        SaveEngines();
        if (AutoZapret.IsChecked == true)
        {
            SaveAutomation();
            await Automatic(async cancellation => { if (await Audit(cancellation)) await session.StartZapret(); });
        }
        else await session.StartZapret();
    });
    void OpenVpnSettings(object sender, RoutedEventArgs e) { Tabs.SelectedIndex = 1; ProfileName.Focus(); }
    void OpenZapretSettings(object sender, RoutedEventArgs e) { Tabs.SelectedIndex = 3; ZapretArgs.Focus(); }
    void AddApplicationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Приложение Windows|*.exe", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        ApplicationList.ItemsSource = ApplicationList.Items.Cast<string>().Concat(dialog.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    void RemoveApplicationClick(object sender, RoutedEventArgs e)
    {
        if (ApplicationList.SelectedItem is string path) ApplicationList.ItemsSource = ApplicationList.Items.Cast<string>().Where(p => p != path).ToList();
    }
    void UpdateConditionalControls()
    {
        bool automatic = ConditionalRouting.IsChecked == true;
        DiscordSplit.IsEnabled = DiscordOnly.IsEnabled = !automatic;
        ManualRoutingButton.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
        RoutingControlsHint.Text = automatic ? "Этими двумя параметрами управляет режим выше: при VPN + zapret Discord идёт напрямую и zapret ограничен Discord; только VPN — исходный профиль; только zapret — все приложения. Для изменения флажков перейди к ручной настройке." : "Ручная настройка: флажки ниже задают разделение трафика. Нажми «Сохранить автоматику». Изменения применятся при следующем запуске или переключении режима; текущий VPN не прерывается.";
        DiscordSplit.ToolTip = DiscordOnly.ToolTip = automatic ? "Параметр задаётся автоматически. Нажми «Перейти к ручной настройке»." : null;
    }
    void ManualRoutingClick(object sender, RoutedEventArgs e)
    {
        ConditionalRouting.IsChecked = false;
        UpdateConditionalControls();
        AutomationStatus.Text = "Ручные флажки доступны. Изменения ещё не сохранены; текущее подключение не менялось.";
    }
    void ConditionalRoutingChanged(object sender, RoutedEventArgs e) => UpdateConditionalControls();
    void FindSourcesClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        SaveGlobal(); ImportStatus.Text = "Ищу папки NekoBox / NekoRay и v2rayN…";
        var folder = ImportFolder.Text;
        var sources = await Task.Run(() => AppImports.Discover(folder)); ImportSources.ItemsSource = sources;
        ImportStatus.Text = $"Источников: {sources.Count}. Выбери файл и открой предпросмотр.";
    });
    void BrowseSourceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Настройки приложений|*.json;*.txt;*.db;*.db3|Все файлы|*.*" };
        if (dialog.ShowDialog() == true) { ImportSources.ItemsSource = new[] { new ImportSource(dialog.FileName, "Выбранный файл", "Предпросмотр перед импортом") }; ImportSources.SelectedIndex = 0; }
    }
    void PreviewSourceClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (automationBusy || session.Busy || session.ProxyRunning) throw new InvalidOperationException("Перед импортом отключи VPN и дождись завершения операций.");
        if (HasChanges()) throw new InvalidOperationException("Сначала сохрани изменения профиля.");
        var source = ImportSources.SelectedItem as ImportSource ?? throw new InvalidOperationException("Выбери источник в списке.");
        if (Directory.Exists(source.Path)) throw new InvalidOperationException(source.Status);
        var batch = AppImports.Read(source.Path);
        var preview = new BatchImportWindow(batch) { Owner = Window.GetWindow(this) };
        if (preview.ShowDialog() != true) return Task.CompletedTask;
        var chosen = preview.Chosen;
        var result = AppImports.Apply(store, chosen);
        if (!store.Settings.ApprovedImportFiles.Contains(source.Path, StringComparer.OrdinalIgnoreCase)) store.Settings.ApprovedImportFiles.Add(source.Path);
        foreach (var p in chosen) if (!store.Settings.ApprovedImportKeys.Contains(p.SourceKey)) store.Settings.ApprovedImportKeys.Add(p.SourceKey);
        SaveGlobal(); var id = selected?.Id; selected = null; RefreshProfiles(); ProfileList.SelectedItem = store.Profiles.FirstOrDefault(p => p.Id == id) ?? store.Profiles.FirstOrDefault();
        ImportStatus.Text = $"Добавлено: {result.Added}. Обновлено: {result.Updated}. Пропущено: {batch.Warnings.Count}.";
        return Task.CompletedTask;
    });
    void SyncApproved()
    {
        var profiles = new List<Profile>(); int warnings = 0;
        foreach (var file in store.Settings.ApprovedImportFiles)
        {
            try { var batch = AppImports.Read(file); profiles.AddRange(batch.Profiles.Where(p => store.Settings.ApprovedImportKeys.Contains(p.SourceKey))); warnings += batch.Warnings.Count; }
            catch (Exception ex) { warnings++; Write("Источник недоступен: " + System.IO.Path.GetFileName(file) + ". " + ex.Message); }
        }
        var result = AppImports.Apply(store, profiles);
        ImportStatus.Text = $"Связанные профили: обновлено {result.Updated}, добавлено {result.Added}, ошибок {warnings}. Исчезнувшие записи не удаляются.";
        if (!automationBusy) { var id = selected?.Id; selected = null; RefreshProfiles(); ProfileList.SelectedItem = store.Profiles.FirstOrDefault(p => p.Id == id) ?? store.Profiles.FirstOrDefault(); }
    }
    void RefreshImportsClick(object sender, RoutedEventArgs e) => Safe(() => { if (session.ProxyRunning || automationBusy || session.Busy || HasChanges()) throw new InvalidOperationException("Сначала отключи VPN и сохрани изменения профиля."); SaveGlobal(); SyncApproved(); return Task.CompletedTask; });
}
