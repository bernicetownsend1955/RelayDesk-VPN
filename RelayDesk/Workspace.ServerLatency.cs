using System.Collections.ObjectModel;
using System.Windows;

namespace RelayDesk;

public partial class Workspace
{
    bool pingBusy;
    CancellationTokenSource? pingCancellation;
    readonly ObservableCollection<ServerLatency> serverLatencies=new();
    void CancelServerPing(object sender,RoutedEventArgs e) => pingCancellation?.Cancel();
    void CheckServerLatency(object sender,RoutedEventArgs e) => Safe(async ()=>
    {
        if(pingBusy) return;
        var profiles=store.Profiles.Where(p=>p.Core!="zapret" && (SubscriptionGroupFilter.SelectedIndex==0 || p.SubscriptionGroup==CurrentSubscriptionGroup)).ToArray();
        pingBusy=true; StartServerPing.IsEnabled=false; StopServerPing.IsEnabled=true;
        pingCancellation=CancellationTokenSource.CreateLinkedTokenSource(connectionCancel.Token);
        serverLatencies.Clear(); ServerPingsTable.ItemsSource=serverLatencies;
        ServerPingsStatus.Text="Проверка TCP по текущим маршрутам Windows…";
        try
        {
            await ServerLatencyProbe.Run(profiles,row=>Dispatcher.Invoke(()=>{ serverLatencies.Add(row); ServerPingsStatus.Text="Получено результатов: "+serverLatencies.Count; }),pingCancellation.Token);
            ServerPingsStatus.Text="Проверено адресов: "+serverLatencies.Count+". TCP включает разрешение DNS; доступность HTTPS проверяется отдельно.";
        }
        catch(OperationCanceledException) { ServerPingsStatus.Text="Проверка отменена. Получено результатов: "+serverLatencies.Count; }
        finally { pingCancellation.Dispose(); pingCancellation=null; pingBusy=false; StartServerPing.IsEnabled=true; StopServerPing.IsEnabled=false; }
    });
}
