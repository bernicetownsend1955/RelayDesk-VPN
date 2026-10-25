using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Principal;

namespace RelayDesk;

public sealed class EngineSession(Store store, Action<string> log)
{
    public XrayConnectionHistory XrayConnections { get; } = new();
    public Process? Proxy { get; private set; }
    public Process? Zapret { get; private set; }
    public Profile? ActiveProfile { get; private set; }
    public event Action? Changed;
    public ConnectionMode Mode { get; private set; }
    public string[] ActiveZapretArguments { get; private set; } = [];
    readonly ProxyLease lease = new(store.Folder);
    bool busy;
    Preferences? activeSettings;
    public bool Busy => busy;
    public bool ProxyRunning => Alive(Proxy);
    public bool ZapretRunning => Alive(Zapret);
    static bool Alive(Process? process) { try { return process != null && !process.HasExited; } catch (InvalidOperationException) { return false; } }
    public void Recover() { var result = lease.Restore(); if (result.Length > 0) log(result); }
    public string Executable(Profile p) => Executable(p, store.Settings);
    static string Executable(Profile p, Preferences settings) => settings.Engines.GetValueOrDefault(p.Core, p.Executable);
    public static bool IsAdmin() { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    static void ValidateExecutable(string executable, string label)
    {
        if (!File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Укажи EXE для {label} на вкладке «Ядра».");
    }
    Process Create(string executable, IEnumerable<string> args, string label)
    {
        ValidateExecutable(executable, label);
        var info = new ProcessStartInfo(Path.GetFullPath(executable)) { WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info };
        void Output(string? data) { if(data == null) return; if(label=="Xray" && (XrayConnections.Record(data) || data.Contains(" accepted ",StringComparison.Ordinal))) return; log($"[{label}] {data}"); }
        process.OutputDataReceived += (_, e) => Output(e.Data);
        process.ErrorDataReceived += (_, e) => Output(e.Data);
        return process;
    }
    static void Start(Process process) { process.Start(); CoreLifetime.Attach(process); process.BeginOutputReadLine(); process.BeginErrorReadLine(); }
    static string[] Arguments(Profile p, string config, bool check) => p.Core switch
    {
        "sing-box" => [check ? "check" : "run", "-c", config],
        "Xray" => check ? ["run", "-test", "-config", config] : ["run", "-config", config],
        "mihomo" => check ? ["-t", "-f", config,"-d",Path.GetDirectoryName(config)!] : ["-f", config,"-d",Path.GetDirectoryName(config)!],
        _ => throw new InvalidOperationException("Для winws используй отдельную панель zapret.")
    };
    string WriteConfig(Profile p, bool check, Preferences settings, ConnectionMode mode)
    {
        var text = RuntimePolicy.Config(p, settings, mode);
        if (p.Core is "sing-box" or "Xray") _ = JsonTools.Object(text);
        var config = Path.Combine(store.Folder, p.Id + (check ? ".check" : ".runtime") + (p.Core == "mihomo" ? ".yaml" : ".json"));
        File.WriteAllText(config, text); return config;
    }
    public async Task Check(Profile p)
    {
        if (busy) throw new InvalidOperationException("Дождись завершения текущей операции.");
        busy = true; Changed?.Invoke(); Process? process = null;
        try
        {
            var mode = ZapretRunning ? ConnectionMode.Combined : ConnectionMode.VpnOnly;
            process = Create(Executable(p), Arguments(p, WriteConfig(p, true, store.Settings, mode), true), p.Core);
            Start(process); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token); process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"Ядро отклонило конфигурацию: код {process.ExitCode}. Подробности в журнале.");
            log("Конфигурация прошла проверку ядром: " + RuntimePolicy.Label(mode));
        }
        finally { try { if (process != null) await Kill(process); } finally { busy = false; Changed?.Invoke(); } }
    }
    public Task Connect(Profile p, bool withZapret)
    {
        if (ProxyRunning) throw new InvalidOperationException("Сначала отключи текущее подключение.");
        return Transition(p, withZapret || ZapretRunning);
    }
    public Task StartZapret() => Transition(ProxyRunning ? ActiveProfile : null, true);
    public Task SwitchProfile(Profile profile) => Transition(profile,ZapretRunning);
    public Task Reconfigure(Profile profile) => ProxyRunning && ActiveProfile?.Id == profile.Id ? Transition(profile, ZapretRunning) : throw new InvalidOperationException("Профиль сейчас не запущен в RelayDesk.");
    public Task StopProxy() => Transition(null, ZapretRunning);
    public Task StopZapret() => Transition(ProxyRunning ? ActiveProfile : null, false);
    public Task StopAll() => Transition(null, false);
    async Task Transition(Profile? profile, bool zapret)
    {
        if (busy) throw new InvalidOperationException("Дождись завершения переключения режима.");
        var target = RuntimePolicy.Mode(profile != null, zapret);
        if (Mode == target && ReferenceEquals(profile, ActiveProfile) && ProxyRunning == (profile != null) && ZapretRunning == zapret) return;
        var previousProfile = ProxyRunning ? ActiveProfile : null; var previousZapret = ZapretRunning;
        var previousSettings = activeSettings ?? store.Settings;
        // Validate and write the target before touching live processes.
        Preflight(profile, zapret, store.Settings, target);
        busy = true; Changed?.Invoke();
        try
        {
            await StopOwned();
            try
            {
                await StartMode(profile, zapret, store.Settings, target);
                if (profile != null)
                {
                    store.Settings.LastConnectedProfileId = profile.Id;
                    try { store.Save(); } catch (Exception ex) { log("Не удалось запомнить сервер: " + ex.Message); }
                }
            }
            catch (Exception failure)
            {
                await StopOwned();
                log("Не удалось переключить режим: " + failure.Message);
                if (previousProfile != null || previousZapret)
                {
                    try { await StartMode(previousProfile, previousZapret, previousSettings, RuntimePolicy.Mode(previousProfile != null, previousZapret)); log("Предыдущий режим восстановлен."); }
                    catch (Exception rollback) { await StopOwned(); log("Восстановление не удалось: " + rollback.Message + ". Оба процесса остановлены."); }
                }
                throw;
            }
        }
        finally { busy = false; Changed?.Invoke(); }
    }
    void Preflight(Profile? p, bool zapret, Preferences settings, ConnectionMode mode)
    {
        var effective = RuntimePolicy.Settings(settings, mode);
        bool rawTun = p is { Managed: false, Core: "sing-box" } && (effective.DiscordSplit || (JsonTools.Object(p.Config)["inbounds"] as System.Text.Json.Nodes.JsonArray)?.Any(n => JsonTools.Text(n?["type"]) == "tun") == true);
        if ((zapret || rawTun || p != null && (p.Managed && p.Tun || effective.DiscordSplit)) && !IsAdmin()) throw new InvalidOperationException("Для zapret / TUN запусти RelayDesk от имени администратора.");
        if (p != null)
        {
            ValidateExecutable(Executable(p, settings), p.Core);
            var proxy = JsonTools.LocalProxy(p); if (p.SystemProxy && proxy.Scheme != "http") throw new InvalidOperationException("Для системного прокси нужен HTTP / mixed-вход.");
            if (HasLocalInput(p) && (!ProxyRunning || ActiveProfile == null || JsonTools.LocalProxy(ActiveProfile).Port != proxy.Port) && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == proxy.Port))
            {
                log($"Порт {proxy.Port} занят. Проверяю оставшиеся процессы RelayDesk перед подключением.");
                CoreRecovery.Recover(store, log);
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == proxy.Port))
                {
                    var owner = CoreRecovery.PortOwner(proxy.Port);
                    throw new InvalidOperationException($"Порт {proxy.Port} занят: {(owner.Length > 0 ? owner : "владелец не определён")}. Зависший процесс RelayDesk не найден или не удалось его завершить. Отключи подключение в приложении-владельце либо измени порт входа.");
                }
                log($"Порт {proxy.Port} освобождён. Продолжаю подключение VPN.");
            }
            _ = WriteConfig(p, false, settings, mode);
            if (rawTun && !ProxyRunning && NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && n.Description.Contains("sing-tun", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Уже работает TUN другого приложения. Отключи его перед запуском RelayDesk.");
        }
        if (zapret) { ValidateExecutable(settings.ZapretExe, "zapret"); _ = ZapretQuickRoutes.Apply(RuntimePolicy.ZapretArguments(settings, mode), p?.QuickRoutes ?? [], settings.ZapretExe, settings.VoiceUdpDesync); }
    }
    static bool HasLocalInput(Profile p) => p.Managed || p.SystemProxy || p.Core is "sing-box" or "Xray" && (JsonTools.Object(p.Config)["inbounds"] as System.Text.Json.Nodes.JsonArray)?.Any(n => JsonTools.Text(n?[p.Core == "sing-box" ? "type" : "protocol"]) is "mixed" or "socks" or "http") == true;
    async Task StartMode(Profile? p, bool zapret, Preferences settings, ConnectionMode mode)
    {
        if (mode == ConnectionMode.ZapretOnly)
        {
            var endpoints = new List<Uri>();
            foreach (var profile in store.Profiles.Where(profile => profile.Core != "zapret"))
                try { endpoints.Add(JsonTools.LocalProxy(profile)); } catch (FormatException) { }
            var restored = ProxyLease.DisableUnavailableProfileProxy(endpoints);
            if (restored.Length > 0) log(restored);
        }
        if (zapret) await StartZapretCore(settings, mode, routes: p?.QuickRoutes);
        if (p != null) await StartProxyCore(p, settings, mode);
        if (p?.Core == "Xray" && RuntimePolicy.Settings(settings,mode).DiscordSplit)
            log("Xray: Discord направлен напрямую по доменам и указанным IP голоса. Правила EXE требуют профиля sing-box с TUN и к Xray не применяются.");
        Mode = mode;
        activeSettings = System.Text.Json.JsonSerializer.Deserialize<Preferences>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        log("Режим: " + RuntimePolicy.Label(mode)); Changed?.Invoke();
    }
    async Task StartProxyCore(Profile p, Preferences settings, ConnectionMode mode)
    {
        var proxy = JsonTools.LocalProxy(p);
        if (HasLocalInput(p) && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == proxy.Port)) throw new InvalidOperationException($"Локальный порт {proxy.Port} уже занят.");
        var runtime = WriteConfig(p, false, settings, mode);
        XrayConnections.Clear();
        if(p.Core=="Xray") File.WriteAllText(runtime,XrayConnectionHistory.Configure(File.ReadAllText(runtime)));
        if (p.Core == "sing-box")
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            File.WriteAllText(runtime, ConnectionMonitor.Configure(File.ReadAllText(runtime), port));
        }
        if(p.Core=="mihomo")
        {
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); listener.Start(); var port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            File.WriteAllText(runtime,MihomoConnections.Configure(File.ReadAllText(runtime),port));
        }
        var process = Create(Executable(p, settings), Arguments(p, runtime, false), p.Core);
        Proxy = process; ActiveProfile = p; Start(process);
        if (HasLocalInput(p))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                if (process.HasExited) throw new InvalidOperationException($"Ядро завершилось: код {process.ExitCode}.");
                try { await Diagnostics.Tcp(proxy.Host, proxy.Port, timeout.Token); break; }
                catch (System.Net.Sockets.SocketException) { await Task.Delay(100, timeout.Token); }
            }
        }
        else { await Task.Delay(500); if (process.HasExited) throw new InvalidOperationException($"Ядро завершилось: код {process.ExitCode}."); }
        if (p.SystemProxy) { lease.Apply(proxy); log("Системный прокси включён до отключения VPN."); }
        log($"[{p.Core}] PID {process.Id}"); _ = Observe(process, false);
    }
    async Task StartZapretCore(Preferences settings, ConnectionMode mode, string? strategy = null, IEnumerable<QuickRoute>? routes = null)
    {
        var arguments = ZapretQuickRoutes.Apply(RuntimePolicy.ZapretArguments(settings, mode, strategy), routes ?? [], settings.ZapretExe, settings.VoiceUdpDesync);
        var process = Create(settings.ZapretExe, arguments, "zapret");
        Zapret = process; Start(process); await Task.Delay(500);
        if (process.HasExited) throw new InvalidOperationException($"zapret завершился: код {process.ExitCode}.");
        ActiveZapretArguments = arguments;
        if (!settings.VoiceUdpDesync) log("[zapret] Голосовые UDP-профили отключены; маршруты сохранены.");
        log($"[zapret] PID {process.Id}"); _ = Observe(process, true);
    }
    async Task Observe(Process process, bool zapret)
    {
        try
        {
            await process.WaitForExitAsync();
            if ((zapret ? Zapret : Proxy) != process) return;
            log($"{(zapret ? "zapret" : "VPN")} завершился: код {process.ExitCode}.");
            if (zapret) { Zapret = null; ActiveZapretArguments = []; } else { Proxy = null; ActiveProfile = null; RestoreProxy(); }
            process.Dispose();
            while (busy) await Task.Delay(50);
            // If a transition or shutdown already replaced the sibling, it owns the final state.
            var target = RuntimePolicy.Mode(ProxyRunning, ZapretRunning);
            if (target != Mode) await Transition(ProxyRunning ? ActiveProfile : null, ZapretRunning);
        }
        catch (Exception ex) { log("Пересборка режима после завершения процесса: " + ex.Message); }
        finally { Changed?.Invoke(); }
    }
    void RestoreProxy() { try { var result = lease.Restore(); if (result.Length > 0) log(result); } catch (Exception ex) { log("Не удалось восстановить прокси: " + ex.Message + ". Файл восстановления сохранён."); } }
    async Task StopOwned()
    {
        var proxy = Proxy; var zapret = Zapret; Proxy = null; Zapret = null; ActiveProfile = null;
        ActiveZapretArguments = [];
        RestoreProxy();
        try { if (proxy != null) await Kill(proxy); }
        finally { if (zapret != null) await Kill(zapret); Mode = ConnectionMode.Off; Changed?.Invoke(); }
    }
    async Task StopAuditZapret()
    {
        var process = Zapret; Zapret = null; ActiveZapretArguments = []; if (process != null) await Kill(process); Changed?.Invoke();
    }
    public async Task<List<DiscordReport>> AuditZapret(CancellationToken cancellation)
    {
        if (busy || Zapret != null) throw new InvalidOperationException("Перед проверкой Discord останови только zapret и дождись текущей операции. VPN можно оставить включённым.");
        foreach (var name in new[] { "winws", "winws2" }) foreach (var process in Process.GetProcessesByName(name)) { process.Dispose(); throw new InvalidOperationException("Останови чужой zapret перед сравнением стратегий."); }
        if (!IsAdmin()) throw new InvalidOperationException("Для сравнения стратегий нужны права администратора.");
        var strategies = FlowsealStrategies.Resolve(store.Settings.ZapretExe, store.Settings.StrategyList);
        var auditSettings = RuntimePolicy.Settings(store.Settings, ConnectionMode.Combined); auditSettings.ConditionalRouting = false;
        foreach (var strategy in strategies) _ = RuntimePolicy.ZapretArguments(auditSettings, ConnectionMode.Combined, strategy.Arguments);
        busy = true; Changed?.Invoke(); var reports = new List<DiscordReport>();
        try
        {
            log($"Стратегий для проверки: {strategies.Count}. Первая: {strategies[0].Name}.");
            log("Проверка Discord: TCP через " + DirectNetwork.Interface().Name + "; основной VPN и таблица маршрутов не меняются. DNS использует системные ответы.");
            reports.Add(await ZapretAudit.Probe("Без zapret", store.Settings, cancellation));
            foreach (var strategy in strategies)
            {
                cancellation.ThrowIfCancellationRequested();
                try { await StartZapretCore(auditSettings, ConnectionMode.Combined, strategy.Arguments); reports.Add(await ZapretAudit.Probe(strategy.Name, store.Settings, cancellation, strategy.Arguments)); }
                catch (Exception ex) when (!cancellation.IsCancellationRequested) { reports.Add(new(strategy.Name, "Ошибка запуска", ex.Message, "Не проверен", null, false, strategy.Arguments)); }
                finally { await StopAuditZapret(); }
                var result = reports[^1]; log($"Discord / {result.Name}: {result.Api}; {result.Gateway}; {result.Voice}" + (result.Ms.HasValue ? $"; {result.Ms} мс" : ""));
            }
            return reports;
        }
        finally { try { await StopAuditZapret(); } finally { busy = false; Changed?.Invoke(); } }
    }
    static async Task Kill(Process process)
    {
        try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); }
    }
}
