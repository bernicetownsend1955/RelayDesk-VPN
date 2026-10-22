using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RelayDesk;

public partial class Workspace : UserControl
{
    readonly Store store;
    readonly EngineSession session;
    Profile? selected;
    bool loading, closing, closed;
    string savedFields = "", savedConfig = "";
    CancellationTokenSource diagnostics = new();
    readonly SemaphoreSlim diagnosticGate = new(1, 1);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public Workspace()
    {
        InitializeComponent();
        Navigation.SelectedIndex = 0;
        Navigation.PreviewMouseLeftButtonDown += NavigationMouseDown;
        AddHandler(Expander.ExpandedEvent, new RoutedEventHandler(DetailsExpanded));
        store = new Store();
        BundledCores.Bind(store, AppContext.BaseDirectory);
        session = new EngineSession(store, Write);
        session.Changed += UpdateStatus;
        ThemeChoice.ItemsSource = new[] { "Системная", "Светлая", "Тёмная" };
        ThemeChoice.SelectedItem = store.Settings.Theme;
        CoreChoice.ItemsSource = new[] { "sing-box", "Xray", "mihomo", "zapret" };
        SingExe.Text = store.Settings.Engines.GetValueOrDefault("sing-box", "");
        XrayExe.Text = store.Settings.Engines.GetValueOrDefault("Xray", "");
        MihomoExe.Text = store.Settings.Engines.GetValueOrDefault("mihomo", "");
        ZapretExe.Text = store.Settings.ZapretExe; ZapretArgs.Text = store.Settings.ZapretArgs;
        WithZapret.IsChecked = store.Settings.WithZapret; TestUrl.Text = store.Settings.TestUrl;
        LoadAutomation();
        if (store.Profiles.Count == 0)
        {
            store.Profiles.Add(new Profile { Name = "Локальный прокси / прямой выход", Protocol = "JSON", Config = "{\n  \"log\": {\"level\": \"info\"},\n  \"inbounds\": [{\"type\": \"mixed\", \"listen\": \"127.0.0.1\", \"listen_port\": 2080}],\n  \"outbounds\": [{\"type\": \"direct\"}]\n}" });
        }
        CoreChoice.SelectionChanged += (_, _) => { if (!loading && selected != null) SettingsHint.Text = "Изменение ядра применяется после сохранения. Формат конфигурации должен ему соответствовать."; };
        RefreshProfiles(); ProfileList.SelectedItem = store.Profiles.FirstOrDefault(p => p.Id == store.Settings.LastConnectedProfileId) ?? store.Profiles.FirstOrDefault();
        Loaded += LoadedWorkspace;
    }
    void LoadedWorkspace(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadedWorkspace;
        var window = Window.GetWindow(this);
        window.Closing += ClosingWindow;
        SystemEvents.UserPreferenceChanged += WindowsThemeChanged;
        ApplyTheme(); Safe(() => { session.Recover(); if (store.Settings.SyncOnStart) SyncApproved(); return Task.CompletedTask; }); UpdateStatus();
        InitializeAppUpdates();
        InitializeUpdates();
        RefreshSetup();
        InitializeExperimental();
        InitializeSupport();
        InitializeConnections();
        InitializeStartup();
        Write("RelayDesk " + AppUpdates.CurrentVersion + ". Сборка: " + Environment.ProcessPath);
    }
    void WindowsThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(() => { if (store.Settings.Theme == "Системная") ApplyTheme(); }));
    async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (closed) return;
        e.Cancel = true;
        if (closing) return;
        if (HasChanges() && MessageBox.Show(Window.GetWindow(this), "Есть несохранённые изменения профиля. Закрыть без сохранения?", "RelayDesk", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        closing = true; IsEnabled = false; startupCancel.Cancel(); diagnostics.Cancel(); automationCancel.Cancel(); updateTimer.Stop(); updateCancel.Cancel(); supportTimer.Stop(); supportCancel.Cancel(); connectionTimer.Stop(); connectionCancel.Cancel();
        try
        {
            // Let an in-flight startup finish before terminating owned processes.
            while (session.Busy || automationBusy || updatesBusy || supportBusy || readingConnections || subscriptionBusy || pingBusy) await Task.Delay(100);
            connectionMonitor.Dispose();
            support.Dispose();
            await session.StopAll(); SaveGlobal();
        }
        catch (Exception ex) { Write(ex.Message); MessageBox.Show("Ошибка при закрытии: " + ex.Message); }
        finally { SystemEvents.UserPreferenceChanged -= WindowsThemeChanged; closed = true; _ = Dispatcher.BeginInvoke(new Action(() => Window.GetWindow(this).Close())); }
    }
    async void Safe(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Write("Время ожидания истекло или проверка отменена."); ActionMessage.Text = "Операция отменена или превышено время ожидания."; }
        catch (Exception ex) { Write(ex.Message); ActionMessage.Text = ex.Message; if (ConnectionTab.IsSelected) ConnectionActionStatus.Text = ex.Message; }
    }
    void Write(string message)
    {
        foreach (var profile in store.Profiles) ProgramJournal.RegisterConfig(profile.Config);
        ProgramJournal.Write(message);
        message = ProgramJournal.Clean(message);
        if (closed) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Log.Text.Length > 120000) Log.Text = Log.Text[^60000..];
            Log.AppendText($"{DateTime.Now:HH:mm:ss}  {Regex.Replace(message, @"\x1B\[[0-9;]*[A-Za-z]", "")}\n"); Log.ScrollToEnd();
        }));
    }
    void UpdateStatus()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(UpdateStatus)); return; }
        var active = session.ActiveProfile;
        OverallStatus.Text = session.Busy ? "Выполняется операция с ядром…" : session.ProxyRunning ? $"Ядро запущено: {active?.Name}. Доступность сети проверяется отдельно." : "Прокси-ядро остановлено";
        if (!session.Busy) OverallStatus.Text = RuntimePolicy.Label(session.Mode);
        if (!session.Busy && selected?.Managed == true && !HasChanges())
        {
            ConfigEditor.Text = BuildPreview(selected);
            savedConfig = ConfigEditor.Text;
        }
        ProxyState.Text = session.ProxyRunning ? ReferenceEquals(active, selected) ? "Процесс этого профиля запущен" : "Работает другой профиль: " + active?.Name : "Отключено";
        ZapretState.Text = session.ZapretRunning ? "Процесс запущен" : "Остановлен";
        ConnectButton.IsEnabled = !session.Busy && (session.ProxyRunning || selected != null && selected.Core != "zapret");
        StartZapretButton.IsEnabled = !session.Busy;
        VpnAction.Text = session.ProxyRunning ? "Нажми, чтобы отключить" : "Нажми, чтобы включить";
        ZapretAction.Text = session.ZapretRunning ? "Нажми, чтобы выключить" : "Нажми, чтобы включить";
        foreach (var pair in new[] { (Button: ConnectButton, Running: session.ProxyRunning), (Button: StartZapretButton, Running: session.ZapretRunning) })
        {
            AnimateConnectionState(pair.Button, pair.Running);
            pair.Button.SetResourceReference(Control.BackgroundProperty, pair.Running ? "Selected" : "Surface");
            pair.Button.SetResourceReference(Control.ForegroundProperty, pair.Running ? "Accent" : "Muted");
            pair.Button.SetResourceReference(Control.BorderBrushProperty, pair.Running ? "Accent" : "Line");
        }
        CheckButton.IsEnabled = selected?.Core != "zapret" && !session.Busy;
        HttpsButton.IsEnabled = !session.Busy && session.ProxyRunning && ReferenceEquals(active, selected);
        if (automationBusy || installingUpdate) { ConnectButton.IsEnabled = false; StartZapretButton.IsEnabled = false; CheckButton.IsEnabled = false; HttpsButton.IsEnabled = false; }
        TcpButton.IsEnabled = !automationBusy;
        RefreshUpdateButtons();
    }
    void RefreshProfiles() { RefreshGroupedProfiles(); RefreshStartupProfiles(); }
    string Fields() => string.Join("\n", ProfileName.Text, CoreChoice.SelectedItem, Tun.IsChecked, SystemProxy.IsChecked, Dns.Text, LocalPort.Text, ProxyAddress.Text, TcpHost.Text, TcpPort.Text, AutoCandidate.IsChecked);
    bool HasChanges() => selected != null && (savedFields != Fields() || savedConfig != ConfigEditor.Text);
    bool CanSwitch()
    {
        if (!HasChanges()) return true;
        return MessageBox.Show(Window.GetWindow(this), "Есть несохранённые изменения. Перейти без сохранения?", "RelayDesk", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    }
    void ProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ProfileList.SelectedItem is not Profile profile) return;
        if (automationBusy) { loading = true; ProfileList.SelectedItem = selected; loading = false; return; }
        if (!CanSwitch()) { loading = true; ProfileList.SelectedItem = selected; loading = false; return; }
        diagnostics.Cancel(); diagnostics.Dispose(); diagnostics = new();
        selected = profile; DisplayProfile();
    }
    void DisplayProfile()
    {
        if (selected == null) return;
        var p = selected; loading = true;
        ProfileName.Text = p.Name; CoreChoice.SelectedItem = p.Core;
        Tun.IsChecked = p.Managed ? p.Tun : p.Core == "sing-box" && (JsonTools.Object(p.Config)["inbounds"] as JsonArray)?.Any(n => JsonTools.Text(n?["type"]) == "tun") == true;
        Tun.IsEnabled = p.Managed;
        EditTunJson.Visibility = !p.Managed && p.Core == "sing-box" ? Visibility.Visible : Visibility.Collapsed;
        UpdateTunHint();
        SystemProxy.IsChecked = p.SystemProxy; AutoCandidate.IsChecked = p.AutoCandidate;
        Dns.Text = p.Dns; LocalPort.Text = p.LocalPort.ToString(); ProxyAddress.Text = p.ProxyUri;
        TcpHost.Text = p.Host; TcpPort.Text = p.Port > 0 ? p.Port.ToString() : "";
        ManagedFields.Visibility = p.Managed ? Visibility.Visible : Visibility.Collapsed;
        RawFields.Visibility = p.Managed ? Visibility.Collapsed : Visibility.Visible;
        CoreChoice.IsEnabled = !p.Managed;
        SettingsHint.Text = p.Managed ? "Профиль из ссылки. TUN и DNS применяются к конфигурации sing-box при сохранении." : p.Core == "zapret" ? "Это профиль аргументов winws из первой версии. Для запуска используй панель zapret и вкладку «Ядра»." : "Полная конфигурация сохраняется целиком. TUN и DNS меняются в исходном JSON / YAML; системный прокси — ниже.";
        ConfigEditor.Text = p.Managed ? BuildPreview(p) : p.Config;
        ConfigHint.Text = p.Managed ? "Полный JSON для просмотра. Чтобы редактировать вручную, сохрани текст: профиль станет ручным, а поля TUN и DNS будут заменены настройками в JSON." : "Исходная конфигурация. Проверка JSON проверяет синтаксис; совместимость проверяет выбранное ядро.";
        if (p.QuickRoutes.Count > 0) ConfigHint.Text += " Быстрые правила из «Соединений» хранятся отдельно в профиле и добавляются при запуске ядра.";
        if (!p.Managed && RuntimePolicy.Settings(store.Settings, session.ZapretRunning ? ConnectionMode.Combined : ConnectionMode.VpnOnly).DiscordSplit) ConfigHint.Text += " При совместном запуске добавляются исключения Discord и выбранных EXE; DNS и исходные правила сохраняются.";
        ServerTitle.Text = p.Name; ServerDescription.Text = p.Protocol + " / " + p.Core + (p.Host.Length > 0 ? $"\n{p.Host}:{p.Port}" : "\nПолная конфигурация");
        SidebarProfileName.Text = p.Name; SidebarProfileGroup.Text = p.Core + (p.SubscriptionGroup.Length > 0 ? " / "+p.SubscriptionGroup : "");
        EnableProfileProxy.Visibility = p.Core == "Xray" && !p.SystemProxy ? Visibility.Visible : Visibility.Collapsed;
        if (p.Core == "Xray") ServerDescription.Text += "\nHTTP-прокси. Разделение Discord — по доменам; правила EXE и TUN доступны в sing-box.";
        savedFields = Fields(); savedConfig = ConfigEditor.Text;
        TcpResult.Text = "Не проверено"; HttpsResult.Text = "Не проверено"; ActionMessage.Text = "";
        loading = false; UpdateStatus();
    }
    Profile RequireProfile() => selected ?? throw new InvalidOperationException("Сначала выбери профиль.");
    void EnsureEditable()
    {
        if (ReferenceEquals(session.ActiveProfile, selected) || session.Busy || automationBusy || installingUpdate) throw new InvalidOperationException("Сначала отключи профиль и дождись завершения операции.");
    }
    void SaveFields()
    {
        var p = RequireProfile(); EnsureEditable();
        if (ConfigEditor.Text != savedConfig) throw new InvalidOperationException("Сначала сохрани изменения на вкладке «Конфигурация» или выбери профиль заново.");
        if (string.IsNullOrWhiteSpace(ProfileName.Text)) throw new FormatException("Укажи название профиля.");
        var copy = System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(p))!;
        copy.Name = ProfileName.Text.Trim(); copy.Core = CoreChoice.SelectedItem?.ToString() ?? "sing-box";
        copy.SystemProxy = SystemProxy.IsChecked == true; copy.AutoCandidate = AutoCandidate.IsChecked == true;
        if (p.Managed)
        {
            copy.Tun = Tun.IsChecked == true; copy.Dns = Dns.Text.Trim(); copy.LocalPort = JsonTools.Port(LocalPort.Text);
            copy.Config = JsonTools.Build(copy);
        }
        else
        {
            copy.ProxyUri = ProxyAddress.Text.Trim();
            if (copy.Core != "zapret") _ = JsonTools.LocalProxy(copy);
            copy.Host = TcpHost.Text.Trim(); copy.Port = copy.Host.Length > 0 ? JsonTools.Port(TcpPort.Text) : 0;
            if (copy.Core is "sing-box" or "Xray") _ = JsonTools.Object(copy.Config);
        }
        store.Profiles[store.Profiles.IndexOf(p)] = copy;
        selected = copy; savedFields = Fields(); SaveGlobal();
        loading = true; RefreshProfiles(); ProfileList.SelectedItem = copy; loading = false; DisplayProfile();
    }
    void SaveGlobal()
    {
        store.Settings.WithZapret = WithZapret.IsChecked == true;
        store.Settings.TestUrl = TestUrl.Text.Trim();
        store.Settings.SyncOnStart = SyncImports.IsChecked == true; store.Settings.ImportFolder = ImportFolder.Text.Trim();
        store.Save();
    }
    void SaveEngines()
    {
        store.Settings.Engines["sing-box"] = SingExe.Text.Trim(); store.Settings.Engines["Xray"] = XrayExe.Text.Trim(); store.Settings.Engines["mihomo"] = MihomoExe.Text.Trim();
        store.Settings.ZapretExe = ZapretExe.Text.Trim(); store.Settings.ZapretArgs = ZapretArgs.Text;
        SaveGlobal();
    }
    void SaveClick(object sender, RoutedEventArgs e) => Safe(() => { SaveFields(); ActionMessage.Text = "Настройки сохранены."; Write("Настройки профиля сохранены."); return Task.CompletedTask; });
    void SaveConfigClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var p = RequireProfile(); EnsureEditable();
        if (Fields() != savedFields) throw new InvalidOperationException("Сначала сохрани настройки профиля, чтобы не потерять изменения полей.");
        if (p.Managed && MessageBox.Show("Сохранить как ручную конфигурацию? TUN и DNS после этого настраиваются в JSON.", "RelayDesk", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return Task.CompletedTask;
        var text = ConfigEditor.Text;
        if (p.Core is "sing-box" or "Xray") text = JsonTools.Object(text).ToJsonString(JsonTools.Options);
        p.Config = text; p.Managed = false; p.Tun = false; p.Host = ""; p.Port = 0; p.SourceKey = "";
        var endpoints = JsonTools.Endpoints(p); if (endpoints.Count == 1) { p.Host = endpoints[0].Host; p.Port = endpoints[0].Port; }
        store.Save(); DisplayProfile(); Write("Конфигурация сохранена."); return Task.CompletedTask;
    });
    void NewProfile(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (!CanSwitch()) return Task.CompletedTask;
        var p = new Profile { Config = "{\n  \"inbounds\": [],\n  \"outbounds\": []\n}" };
        AddProfile(p); Tabs.SelectedIndex = 2; return Task.CompletedTask;
    });
    void AddProfile(Profile p)
    {
        p.SubscriptionGroup = CurrentSubscriptionGroup;
        store.Profiles.Add(p); store.Save(); selected = null;
        loading = true; RefreshProfiles(); loading = false; ProfileList.SelectedItem = p;
    }
    void DeleteClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var p = RequireProfile(); EnsureEditable();
        if (MessageBox.Show("Удалить профиль «" + p.Name + "»?", "RelayDesk", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return Task.CompletedTask;
        store.Profiles.Remove(p); selected = null; store.Save(); RefreshProfiles();
        if (store.Profiles.Count > 0) ProfileList.SelectedIndex = 0;
        else { ServerTitle.Text = "Добавь сервер"; ServerDescription.Text = "Вставь ссылку из буфера или импортируй JSON."; ConfigEditor.Clear(); UpdateStatus(); }
        return Task.CompletedTask;
    });
    void Paste(object sender, RoutedEventArgs e) => Safe(async () => { var text = Clipboard.GetText().Trim(); if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("happ://add/", StringComparison.OrdinalIgnoreCase)) await ImportSubscription(text); else ImportText(text); });
    void ImportFile(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Конфигурации и ссылки|*.json;*.yaml;*.yml;*.txt|Все файлы|*.*" };
        if (dialog.ShowDialog() == true) { if (new FileInfo(dialog.FileName).Length > 2_000_000) throw new FormatException("Файл больше 2 МБ."); ImportText(File.ReadAllText(dialog.FileName)); }
        return Task.CompletedTask;
    });
    void ImportText(string text)
    {
        var result = ImportParser.Parse(text);
        var preview = new ImportWindow(result) { Owner = Window.GetWindow(this) };
        if (preview.ShowDialog() != true) return;
        if (!CanSwitch()) return;
        AddProfile(result.Profile); Tabs.SelectedIndex = 0; Write("Импортирован профиль: " + result.Profile.Name);
    }
    void ExportClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var p = RequireProfile(); var dialog = new SaveFileDialog { FileName = p.Core == "mihomo" ? "config.yaml" : p.Core == "zapret" ? "arguments.txt" : "config.json" };
        if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, ConfigEditor.Text);
        return Task.CompletedTask;
    });
    void CheckClick(object sender, RoutedEventArgs e) => Safe(async () => { var p = RequireProfile(); if (HasChanges()) throw new InvalidOperationException("Сначала сохрани изменения профиля."); SaveEngines(); await session.Check(p); ActionMessage.Text = "Конфигурация прошла проверку ядром."; });
    void ConnectClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        CancelWindowsStartup();
        var p = RequireProfile(); if (HasChanges()) throw new InvalidOperationException("Сначала сохрани изменения профиля.");
        if (installingUpdate) throw new InvalidOperationException("Дождись завершения обновления компонента.");
        HttpsResult.Text = "Не проверено"; SaveEngines(); await ConnectAutomatically(p);
    });
    void DisconnectClick(object sender, RoutedEventArgs e) => Safe(async () => { CancelWindowsStartup(); diagnostics.Cancel(); await session.StopProxy(); HttpsResult.Text = "Не проверено"; });
    void StopAllClick(object sender, RoutedEventArgs e) => Safe(async () => { CancelWindowsStartup(); if (automationBusy) { automationCancel.Cancel(); Write("Отмена автоматики запрошена. Тестовые процессы будут остановлены."); return; } if (session.Busy) throw new InvalidOperationException("Дождись завершения запуска или проверки ядра."); diagnostics.Cancel(); await session.StopAll(); HttpsResult.Text = "Не проверено"; });
    void StartZapretClick(object sender, RoutedEventArgs e) => Safe(async () => { SaveEngines(); await session.StartZapret(); });
    void StopZapretClick(object sender, RoutedEventArgs e) => Safe(async () => { if (session.Busy) throw new InvalidOperationException("Дождись завершения текущей операции."); await session.StopZapret(); });
    void TcpClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        var p = RequireProfile(); if (HasChanges()) throw new InvalidOperationException("Сначала сохрани изменения.");
        var endpoints = p.Host.Length > 0 ? new List<(string Host, int Port)> { (p.Host, p.Port) } : JsonTools.Endpoints(p);
        if (endpoints.Count != 1) throw new InvalidOperationException("В профиле нет одного адреса сервера. Укажи адрес и порт TCP на вкладке «Настройки».");
        if (!await diagnosticGate.WaitAsync(0)) return;
        diagnostics.Dispose(); diagnostics = new(); var cancellation = diagnostics.Token;
        TcpButton.IsEnabled = false; TcpResult.Text = "Проверка…";
        try { var ms = await Diagnostics.Tcp(endpoints[0].Host, endpoints[0].Port, cancellation); if (ReferenceEquals(p, selected)) TcpResult.Text = $"{ms} мс / TCP"; }
        catch (Exception ex) { if (ReferenceEquals(p, selected)) TcpResult.Text = ex is OperationCanceledException ? "Тайм-аут / отменено" : "Нет соединения"; throw; }
        finally { diagnosticGate.Release(); TcpButton.IsEnabled = true; }
    });
    void HttpsClick(object sender, RoutedEventArgs e) => Safe(async () =>
    {
        var p = RequireProfile(); if (!ReferenceEquals(session.ActiveProfile, p) || !session.ProxyRunning) throw new InvalidOperationException("Сначала запусти выбранный профиль.");
        if (!await diagnosticGate.WaitAsync(0)) return;
        diagnostics.Dispose(); diagnostics = new(); var cancellation = diagnostics.Token;
        HttpsButton.IsEnabled = false; HttpsResult.Text = "Проверка…";
        try
        {
            var result = await Diagnostics.Https(JsonTools.LocalProxy(p), TestUrl.Text.Trim(), cancellation);
            if (ReferenceEquals(p, selected) && ReferenceEquals(session.ActiveProfile, p)) HttpsResult.Text = $"{result.Ms} мс / HTTP {result.Status}";
            Write($"HTTPS через {p.Core}: HTTP {result.Status}, {result.Ms} мс. Ответ проверяемого URL; маршрут определяется конфигурацией ядра.");
        }
        catch { if (ReferenceEquals(p, selected)) HttpsResult.Text = "Ошибка / см. журнал"; throw; }
        finally { diagnosticGate.Release(); UpdateStatus(); }
    });
    void Browse(TextBox field) { var dialog = new OpenFileDialog { Filter = "Программа Windows|*.exe" }; if (dialog.ShowDialog() == true) field.Text = dialog.FileName; }
    void BrowseSing(object sender, RoutedEventArgs e) => Browse(SingExe);
    void BrowseXray(object sender, RoutedEventArgs e) => Browse(XrayExe);
    void BrowseMihomo(object sender, RoutedEventArgs e) => Browse(MihomoExe);
    void BrowseZapret(object sender, RoutedEventArgs e) => Browse(ZapretExe);
    void SaveEnginesClick(object sender, RoutedEventArgs e) => Safe(() => { SaveEngines(); Write("Пути ядер и стратегия сохранены."); return Task.CompletedTask; });
    void ClearLog(object sender, RoutedEventArgs e) => Log.Clear();
    void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (store == null || ThemeChoice.SelectedItem == null) return;
        store.Settings.Theme = ThemeChoice.SelectedItem.ToString()!; ApplyTheme();
        try { store.Save(); } catch (Exception ex) { Write(ex.Message); }
    }
    void ApplyTheme()
    {
        bool dark = store.Settings.Theme == "Тёмная";
        if (store.Settings.Theme == "Системная")
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        var colors = dark ? new[] { "#191919", "#222222", "#F3F3F3", "#B2B2B2", "#383838", "#82B49A", "#2D3C33" } : new[] { "#F6F6F6", "#FFFFFF", "#202020", "#626262", "#DEDEDE", "#286548", "#E5EFE9" };
        var keys = new[] { "Page", "Surface", "Ink", "Muted", "Line", "Accent", "Selected" };
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        var window = Window.GetWindow(this);
        if (window != null) { var handle = new WindowInteropHelper(window).Handle; if (handle != IntPtr.Zero) { int value = dark ? 1 : 0; DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)); } }
    }
}
