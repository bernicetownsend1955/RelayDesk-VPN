using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public partial class Workspace
{
    bool refreshingGroups;
    string CurrentSubscriptionGroup => SubscriptionGroupFilter.SelectedIndex > 1 ? SubscriptionGroupFilter.SelectedItem?.ToString() ?? "" : "";

    void RefreshGroupedProfiles()
    {
        var index = SubscriptionGroupFilter.SelectedIndex;
        var group = CurrentSubscriptionGroup;
        refreshingGroups = true;
        var groups = store.Settings.SubscriptionGroups.Concat(store.Profiles.Select(p => p.SubscriptionGroup)).Where(g => g.Length > 0).Distinct().OrderBy(g => g).ToArray();
        SubscriptionGroupFilter.ItemsSource = new[] { "Все профили", "Без группы" }.Concat(groups).ToArray();
        SubscriptionGroupFilter.SelectedIndex = group.Length > 0 && Array.IndexOf(groups, group) >= 0 ? Array.IndexOf(groups, group) + 2 : index == 1 ? 1 : 0;
        var search = ProfileSearch.Text.Trim();
        ProfileList.ItemsSource = store.Profiles.Where(p => (SubscriptionGroupFilter.SelectedIndex == 0 || p.SubscriptionGroup == CurrentSubscriptionGroup) && (search.Length == 0 || (p.Name+" "+p.Host+" "+p.Core+" "+p.SubscriptionGroup).Contains(search,StringComparison.OrdinalIgnoreCase))).ToList();
        ProfileCount.Text = $"Показано: {ProfileList.Items.Count} из {store.Profiles.Count}. Выбери профиль, затем открой подключение.";
        refreshingGroups = false;
    }

    void SubscriptionGroupChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingGroups || store == null) return;
        if (!CanSwitch())
        {
            refreshingGroups = true;
            SubscriptionGroupFilter.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
            refreshingGroups = false; return;
        }
        var previous = selected;
        loading = true; RefreshGroupedProfiles();
        ProfileList.SelectedItem = ProfileList.Items.Contains(previous) ? previous : null;
        loading = false;
        if (ProfileList.SelectedItem == null && ProfileList.Items.Count > 0) ProfileList.SelectedIndex = 0;
    }

    void EditSubscriptionGroups(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.Busy || automationBusy || !CanSwitch()) return Task.CompletedTask;
        var dialog = new SubscriptionGroupsWindow(store.Profiles, store.Settings.SubscriptionGroups, CurrentSubscriptionGroup) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        var previousGroups = store.Settings.SubscriptionGroups;
        var previousSources = store.Settings.Subscriptions.ToDictionary(s => s.Id,s => s.Group);
        var previous = store.Profiles.ToDictionary(p => p.Id, p => p.SubscriptionGroup);
        store.Settings.SubscriptionGroups = dialog.Groups;
        foreach (var s in store.Settings.Subscriptions) if (dialog.GroupChanges.TryGetValue(s.Group,out var renamed)) s.Group = renamed;
        foreach (var p in store.Profiles) p.SubscriptionGroup = dialog.Assignments[p.Id];
        try { store.Save(); }
        catch { store.Settings.SubscriptionGroups = previousGroups; foreach (var s in store.Settings.Subscriptions) s.Group = previousSources[s.Id]; foreach (var p in store.Profiles) p.SubscriptionGroup = previous[p.Id]; throw; }
        loading = true; RefreshProfiles();
        ProfileList.SelectedItem = selected != null && ProfileList.Items.Contains(selected) ? selected : null;
        loading = false;
        if (ProfileList.SelectedItem == null && ProfileList.Items.Count > 0) ProfileList.SelectedIndex = 0;
        ActionMessage.Text = "Группы подписок сохранены.";
        return Task.CompletedTask;
    });
}
