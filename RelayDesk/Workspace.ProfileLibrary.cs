using System.Windows;
using System.Windows.Controls;

namespace RelayDesk;

public partial class Workspace
{
    void OpenProfileAddMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Item(string name, RoutedEventHandler action) { var item = new MenuItem { Header=name }; item.Click += action; menu.Items.Add(item); }
        Item("Вставить из буфера",Paste);
        Item("Импортировать файл",ImportFile);
        Item("Добавить подписку",OpenSubscriptions);
        menu.Items.Add(new Separator());
        Item("Создать пустой профиль",NewProfile);
        menu.PlacementTarget=(UIElement)sender; menu.IsOpen=true;
    }
    void OpenProfileLibrary(object sender, RoutedEventArgs e) => Tabs.SelectedItem=ProfilesTab;
    void OpenProfilesConnection(object sender, RoutedEventArgs e) => Tabs.SelectedIndex=0;
    void EnableProfileProxyClick(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (HasChanges()) throw new InvalidOperationException("Сначала сохрани изменения профиля.");
        SystemProxy.IsChecked=true; SaveFields();
        ActionMessage.Text="Системный прокси сохранён для профиля. Нажми VPN, чтобы подключиться.";
        return Task.CompletedTask;
    });
    void ProfileSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (store == null || refreshingGroups) return;
        loading=true; RefreshGroupedProfiles(); ProfileList.SelectedItem=selected != null && ProfileList.Items.Contains(selected) ? selected : null; loading=false;
    }
    void ProfileContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(ProfileList,origin) is ListBoxItem row)
            ProfileList.SelectedItem=row.Content;
        if (ProfileList.SelectedItem is not Profile profile) { e.Handled=true; return; }
        var menu = new ContextMenu();
        var open = new MenuItem { Header="Открыть подключение" }; open.Click += OpenProfilesConnection; menu.Items.Add(open);
        var edit = new MenuItem { Header="Настройки профиля" }; edit.Click += OpenVpnSettings; menu.Items.Add(edit);
        var move = new MenuItem { Header="Переместить в группу" }; menu.Items.Add(move);
        foreach (var group in new[] { "" }.Concat(store.Settings.SubscriptionGroups.Concat(store.Profiles.Select(p => p.SubscriptionGroup)).Where(g => g.Length > 0).Distinct().OrderBy(g => g)))
        {
            var target=group;
            var item=new MenuItem { Header=target.Length > 0 ? target : "Без группы",IsCheckable=true,IsChecked=profile.SubscriptionGroup==target };
            item.Click += (_,_) => Safe(() =>
            {
                if (session.Busy || automationBusy || HasChanges()) throw new InvalidOperationException("Сохрани изменения и дождись текущей операции.");
                var previous=profile.SubscriptionGroup; profile.SubscriptionGroup=target;
                try { store.Save(); } catch { profile.SubscriptionGroup=previous; throw; }
                loading=true; RefreshProfiles(); ProfileList.SelectedItem=ProfileList.Items.Contains(profile) ? profile : null; loading=false;
                SidebarProfileGroup.Text=profile.Core+(target.Length>0 ? " / "+target : "");
                ActionMessage.Text="Группа профиля изменена.";
                return Task.CompletedTask;
            });
            move.Items.Add(item);
        }
        ProfileList.ContextMenu=menu;
    }
}
