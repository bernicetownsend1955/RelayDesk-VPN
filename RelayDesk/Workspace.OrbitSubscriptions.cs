using System.Text.Json;
using System.Windows;

namespace RelayDesk;
public partial class Workspace
{
    async Task ImportOrbitSubscription(string text)
    {
        if(subscriptionBusy||ModernBusy||HasChanges())throw new InvalidOperationException("Сохрани изменения и дождись текущей операции.");
        var address=Subscriptions.Address(text);
        var existing=store.Settings.Subscriptions.FirstOrDefault(s=>Subscriptions.Address(s.Url)==address);
        var source=existing==null?new SubscriptionSource{Url=address.AbsoluteUri}:JsonSerializer.Deserialize<SubscriptionSource>(JsonSerializer.Serialize(existing))!;
        subscriptionBusy=true;
        try
        {
            ActionMessage.Text="Загружаю серверы подписки…";
            var download=await Subscriptions.Download(source,Subscriptions.InstallationId(store.Folder),connectionCancel.Token);if(closing)return;
            string group=source.Group.Length>0?source.Group:download.Title.Length>0?download.Title:"Подписка "+address.IdnHost;
            var preview=new BatchImportWindow(download.Batch,group){Owner=Window.GetWindow(this),Title="Выбрать серверы подписки"};
            if(preview.ShowDialog()!=true){ActionMessage.Text="Добавление подписки отменено.";return;}
            if(HasChanges())throw new InvalidOperationException("Сохрани изменения профиля перед добавлением подписки.");
            source.Group=preview.Group;var result=Subscriptions.Apply(store,source,preview.Chosen);
            var previousId=selected?.Id;loading=true;RefreshProfiles();ProfileList.SelectedItem=store.Profiles.FirstOrDefault(p=>p.Id==previousId)??store.Profiles.FirstOrDefault(p=>p.SubscriptionId==source.Id);loading=false;
            if(ProfileList.SelectedItem is Profile current){selected=current;DisplayProfile();}
            ActionMessage.Text=$"Добавлено: {result.Added}; обновлено: {result.Updated}. Работающее подключение сохранено.";Write(ActionMessage.Text);
        }
        finally{subscriptionBusy=false;}
    }
}
