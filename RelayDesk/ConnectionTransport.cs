using System.Text.Json.Nodes;

namespace RelayDesk;

public static class ConnectionTransport
{
    public static string Describe(Profile profile,string config)
    {
        if(profile.Core is not ("sing-box" or "Xray"))return profile.SystemProxy?"Системный прокси":"Режим из конфигурации ядра";
        var inbounds=JsonTools.Object(config)["inbounds"] as JsonArray??[];
        var tun=inbounds.OfType<JsonObject>().Where(i=>JsonTools.Text(i["type"])=="tun").ToArray();
        bool system=profile.SystemProxy||inbounds.Any(i=>JsonTools.Text(i?["set_system_proxy"])=="true");
        if(tun.Any(i=>JsonTools.Text(i["auto_route"])=="true"))return system?"TUN + системный прокси":"TUN";
        if(tun.Length>0)return system?"TUN без автомаршрутов + системный прокси":"TUN без автоматических маршрутов";
        return system?"Системный прокси":"Локальный прокси · TUN и системный прокси выключены";
    }
    public static string Planned(Profile profile,Preferences settings,ConnectionMode mode)
    {
        try{return Describe(profile,RuntimePolicy.Config(profile,settings,mode));}
        catch{return "Проверь параметры профиля";}
    }
}
