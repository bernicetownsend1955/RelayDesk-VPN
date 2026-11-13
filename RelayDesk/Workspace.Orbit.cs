namespace RelayDesk;
public partial class Workspace
{
    public async Task ModernSetVpnMode(string mode)
    {
        if(mode is not ("off" or "proxy" or "tun"))throw new ArgumentException("Неизвестный режим VPN.");
        if(ModernBusy||HasChanges())throw new InvalidOperationException("Сохрани изменения и дождись текущей операции.");
        CancelWindowsStartup();
        if(mode=="off"){diagnostics.Cancel();await session.StopProxy();HttpsResult.Text="Не проверено";return;}
        var profile=RequireProfile();if(mode=="tun"&&profile.Core!="sing-box")throw new InvalidOperationException("Для TUN выбери профиль sing-box. Xray работает через системный прокси.");
        var previous=store.Settings.Connection;var common=previous??SharedConnection.Read(profile);
        try
        {
            if(profile.Core is "sing-box" or "Xray")ModernSaveConnection(mode=="tun",true,common.LocalPort);
            SaveEngines();HttpsResult.Text="Не проверено";
            if(session.ProxyRunning)await session.SwitchProfile(profile);else await ConnectAutomatically(profile,false);
        }
        catch{store.Settings.Connection=previous;store.Save();DisplayProfile();throw;}
    }
}
