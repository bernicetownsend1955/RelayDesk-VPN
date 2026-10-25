using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace RelayDesk;

public partial class Workspace
{
    readonly ConnectionMonitor connectionMonitor = new();
    readonly DispatcherTimer connectionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly CancellationTokenSource connectionCancel = new();
    bool readingConnections;
    List<ConnectionRow> connectionRows = new();
    readonly ConnectionCollection displayedConnections = new();
    void InitializeConnections()
    {
        ConnectionsTable.ItemsSource = displayedConnections;
        connectionTimer.Tick += async (_, _) => await ReadConnections();
        connectionTimer.Start();
    }
    async Task ReadConnections()
    {
        if (readingConnections || closing || ConnectionTab.IsSelected != true && !ModernConnectionsVisible || PauseConnections.IsChecked == true) return;
        readingConnections = true;
        try
        {
            string config;
            bool own = session.ProxyRunning && session.ActiveProfile?.Core is "sing-box" or "Xray" or "mihomo";
            var core=own ? session.ActiveProfile!.Core : selected?.Core ?? "";
            ConnectionsHeading.Text=core=="Xray" ? "Последние маршруты Xray" : "Текущие соединения";
            if (own) config = File.ReadAllText(Path.Combine(store.Folder, session.ActiveProfile!.Id + (core=="mihomo" ? ".runtime.yaml" : ".runtime.json")));
            else
            {
                if (selected?.Core is not ("sing-box" or "mihomo")) throw new InvalidOperationException("Для маршрутов Xray запусти профиль в RelayDesk. Для sing-box / mihomo нужен доступный локальный API.");
                config = selected.Managed ? JsonTools.Build(selected) : selected.Config;
            }
            var rows = core=="Xray" ? session.XrayConnections.Read(config,session.ZapretRunning,session.ActiveZapretArguments)
                : await connectionMonitor.Read(core=="mihomo" ? MihomoConnections.MonitorConfig(config) : config, own ? session.ZapretRunning : null, own ? session.ActiveZapretArguments : [], connectionCancel.Token);
            if(core=="mihomo") rows=rows.Select(r=>r with { CanEdit=false }).ToList();
            if (closing) return;
            connectionRows = rows; FilterConnections();
            var direct = rows.Count(r => r.Route == "Напрямую"); var proxy = rows.Count(r => r.Route == "Через VPN / прокси");
            ConnectionsStatus.Text = $"{DateTime.Now:HH:mm:ss} · Показано: {rows.Count}; напрямую: {direct}; VPN / прокси: {proxy}. " + (own ? "Источник: RelayDesk / "+core+". " : "Источник: локальный API выбранного профиля; принадлежность процесса RelayDesk не подтверждена. ") + (core=="Xray" ? "История за 3 минуты: закрытие соединений, процесс и байты ядро не сообщает." : "Текущие соединения ядра.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { connectionRows.Clear(); FilterConnections(); ConnectionsStatus.Text = "Нет текущих данных: " + ProgramJournal.Clean(ex.Message); }
        finally { readingConnections = false; }
    }
    void FilterConnections()
    {
        if (ConnectionsTable == null) return;
        var filter = ConnectionFilter.Text.Trim();
        var selectedId = (ConnectionsTable.SelectedItem as ConnectionRow)?.Id;
        displayedConnections.Replace(connectionRows.Where(r => filter.Length == 0 || (r.Application + " " + r.Destination + " " + r.Route + " " + r.Rule).Contains(filter, StringComparison.OrdinalIgnoreCase)));
        if (selectedId != null) ConnectionsTable.SelectedItem = displayedConnections.FirstOrDefault(r => r.Id == selectedId);
    }
    void ConnectionFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => FilterConnections();
}
