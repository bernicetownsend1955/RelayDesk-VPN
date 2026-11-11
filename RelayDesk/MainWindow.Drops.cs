using System.Windows;
using System.Windows.Interop;

namespace RelayDesk;

public partial class MainWindow
{
    OverlayBridge? windowDropBridge;
    void InitializeWindowDrops()
    {
        AllowDrop=true;
        PreviewDragOver+=(_,e)=>{e.Effects=OverlayDrops.Effect(e.AllowedEffects,!rulesDialogOpen&&OverlayDrops.Supports(e.Data));e.Handled=true;};
        PreviewDrop+=(_,e)=>
        {
            e.Handled=true;
            try{var data=OverlayDrops.Read(e.Data);Dispatcher.BeginInvoke(()=>HandleWindowDrop(data));}
            catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
        };
        Loaded+=(_,_)=>
        {
            // Normal-integrity windows accept OLE drops directly. Elevated windows use the
            // same unelevated, authenticated helper as the traffic overlay.
            if(!EngineSession.IsAdmin()||windowDropBridge!=null)return;
            windowDropBridge=new OverlayBridge((_,_)=>{},()=>{},()=>{},HandleWindowDrop,Toast,()=>{},new WindowInteropHelper(this).Handle.ToInt64());
            windowDropBridge.Show();
        };
        Closed+=(_,_)=>{windowDropBridge?.Close();windowDropBridge=null;};
    }
    async void HandleWindowDrop(OverlayDrop data)
    {
        if(rulesDialogOpen){Toast("Сначала закрой текущее окно настройки.");return;}
        if(!IsVisible||WindowState==WindowState.Minimized)ShowFromTray();else Activate();
        WakeQuietMode();rulesDialogOpen=true;
        try
        {
            if(backend.ModernProfile==null)throw new InvalidOperationException("Сначала добавь VPN-профиль, чтобы сохранить общее правило.");
            if(data.Files.Length>1)throw new FormatException("Перетаскивай по одному приложению или ссылке.");
            var candidate=OverlayDrops.Rule(data.Files.FirstOrDefault()??data.Text);
            LoadProfileRules();
            var existing=state.Rules.FirstOrDefault(r=>r.Kind!="default"&&RuleMatching.SameTarget(r,candidate));
            if(existing!=null){candidate.Id=existing.Id;candidate.Name=existing.Name;candidate.Route=existing.Route;candidate.QuickKey=existing.QuickKey;candidate.OutboundTag=existing.OutboundTag;}
            var dialog=new DropRouteWindow(candidate,RuleMatching.Overlaps(candidate,state.Rules),backend.ModernSession.ProxyRunning&&!backend.ModernBusy){Owner=this};
            if(dialog.ShowDialog()!=true)return;
            candidate.Route=dialog.SelectedRoute;
            if(existing?.Route!=candidate.Route)candidate.OutboundTag="";
            CommitRule(candidate,existing,true);SelectPage(2);
            if(dialog.ApplyNow){await backend.ModernApplyRoutes();pendingRules.Remove(backend.ModernSession.ActiveProfile?.Id??"");Toast("Правило сохранено и применено.");}
            else Toast("Правило сохранено для всех профилей. Нажми «Применить правила» или переподключи VPN.");
        }
        catch(Exception ex){Toast(ProgramJournal.Clean(ex.Message));}
        finally{rulesDialogOpen=false;RegisterActivity();}
    }
}
