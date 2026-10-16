using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public partial class Workspace
{
    void ConnectionAppClick(object sender, RoutedEventArgs e) => EditConnectionRoute(sender, true);
    void ConnectionHostClick(object sender, RoutedEventArgs e) => EditConnectionRoute(sender, false);
    void EditConnectionRoute(object sender, bool application) => Safe(async () =>
    {
        if (sender is not FrameworkElement element || element.DataContext is not ConnectionRow row) return;
        if (session.Busy || automationBusy || installingUpdate || HasChanges()) throw new InvalidOperationException("Сохрани изменения профиля и дождись завершения текущей операции.");
        var profileId = session.ProxyRunning ? session.ActiveProfile?.Id : selected?.Id;
        var profile = store.Profiles.FirstOrDefault(p => p.Id == profileId && p.Core == "sing-box") ?? throw new InvalidOperationException("Выбери профиль sing-box.");
        string kind, value;
        if (application)
        {
            kind = row.ProcessPath.Length > 0 ? "process_path" : "process_name";
            value = row.ProcessPath.Length > 0 ? row.ProcessPath : row.Application;
        }
        else
        {
            value = row.Host.Length > 0 ? row.Host : row.Address;
            if (IPAddress.TryParse(value, out var address)) { kind = "ip_cidr"; value = address + (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "/32" : "/128"); }
            else kind = "domain";
        }
        QuickRouting.Validate(new(kind, value, "@direct", true));
        var outbounds = JsonTools.Object(profile.Config)["outbounds"] as JsonArray ?? new JsonArray();
        var tags = outbounds.Where(o => JsonTools.Text(o?["type"]) is not ("direct" or "block" or "dns") && JsonTools.Text(o?["tag"]).Length > 0).Select(o => JsonTools.Text(o?["tag"])).Distinct().ToArray();
        var dialog = new ConnectionRouteWindow(profile, kind, value, tags, session.ProxyRunning && session.ActiveProfile?.Id == profile.Id) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile))!;
        copy.QuickRoutes.RemoveAll(r => r.Value == value && (r.Kind == kind || kind == "domain" && r.Kind == "domain_suffix"));
        if (!dialog.Remove && dialog.Rule != null) copy.QuickRoutes.Insert(0, dialog.Rule);
        // Validate the resulting runtime policy before saving or touching the live core.
        _ = RuntimePolicy.Config(copy, store.Settings, session.ZapretRunning ? ConnectionMode.Combined : ConnectionMode.VpnOnly);
        var index = store.Profiles.IndexOf(profile); store.Profiles[index] = copy;
        try { store.Save(); } catch { store.Profiles[index] = profile; throw; }
        if (selected?.Id == copy.Id) { selected = copy; loading = true; RefreshProfiles(); ProfileList.SelectedItem = copy; loading = false; DisplayProfile(); }
        ConnectionActionStatus.Text = dialog.Remove ? "Быстрое правило удалено. Применится при следующем подключении." : "Быстрое правило сохранено. Применится при следующем подключении.";
        if (dialog.ApplyNow) { await session.Reconfigure(copy); ConnectionActionStatus.Text = "Правило применено. Уже открытые соединения были закрыты при переподключении."; }
    });
}
