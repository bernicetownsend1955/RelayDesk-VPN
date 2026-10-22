using System.Windows;

namespace RelayDesk;

public partial class Workspace
{
    bool subscriptionBusy;
    void OpenSubscriptions(object sender, RoutedEventArgs e) => Safe(() => ImportSubscription(""));
    async Task ImportSubscription(string value)
    {
        if (subscriptionBusy || automationBusy || session.Busy || HasChanges()) throw new InvalidOperationException("Сохрани изменения профиля и дождись текущей операции.");
        var dialog = new SubscriptionWindow(store.Settings.Subscriptions, value, CurrentSubscriptionGroup) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Source == null) return;
        var source = dialog.Source;
        subscriptionBusy = true;
        try
        {
            store.Save();
            ActionMessage.Text = "Загружаю подписку…";
            var download = await Subscriptions.Download(source, Subscriptions.InstallationId(store.Folder), connectionCancel.Token);
            if (closing) return;
            if (source.Group.Length == 0) source.Group = download.Title.Length > 0 ? download.Title : "Подписка " + Subscriptions.Address(source.Url).IdnHost;
            if (source.Group is "Все профили" or "Без группы") source.Group = "Подписка " + source.Group;
            var preview = new BatchImportWindow(download.Batch) { Owner = Window.GetWindow(this), Title = "Предпросмотр подписки" };
            if (preview.ShowDialog() != true) { ActionMessage.Text = "Импорт подписки отменён."; return; }
            if (HasChanges()) throw new InvalidOperationException("Сохрани изменения профиля перед импортом подписки.");
            var result = Subscriptions.Apply(store, source, preview.Chosen);
            loading = true; RefreshProfiles();
            ProfileList.SelectedItem = selected == null ? null : store.Profiles.FirstOrDefault(p => p.Id == selected.Id);
            loading = false;
            if (ProfileList.SelectedItem is Profile current) { selected = current; DisplayProfile(); }
            ActionMessage.Text = $"Подписка обновлена. Добавлено: {result.Added}; обновлено: {result.Updated}; пропущено: {download.Batch.Warnings.Count}. Работающее подключение не менялось.";
            Write($"Подписка обновлена: добавлено {result.Added}, обновлено {result.Updated}.");
        }
        finally { subscriptionBusy = false; }
    }
}
