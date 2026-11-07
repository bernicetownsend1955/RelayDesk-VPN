using System.Text.Json.Nodes;

namespace RelayDesk;

public sealed record ConnectionSettings(bool Tun,bool SystemProxy,int LocalPort);
public static class SharedConnection
{
    public static void Migrate(Store store)
    {
        if(store.Settings.Connection!=null)return;
        var source=store.Profiles.FirstOrDefault(p=>p.Name.Equals("Мой VPN",StringComparison.OrdinalIgnoreCase))??store.Profiles.FirstOrDefault(p=>p.Id==store.Settings.LastConnectedProfileId)??store.Profiles.FirstOrDefault(p=>p.Core is "sing-box" or "Xray");
        store.Settings.Connection=source==null?new(false,true,2080):Read(source);
    }
    public static ConnectionSettings Read(Profile profile)
    {
        bool tun=profile.Tun,proxy=profile.SystemProxy;
        int port=profile.LocalPort;
        try{port=JsonTools.LocalProxy(profile).Port;}catch(FormatException){}
        if(profile.Core is "sing-box" or "Xray" && !profile.Managed)
        {
            var inputs=JsonTools.Object(profile.Config)["inbounds"] as JsonArray??[];
            tun=inputs.Any(i=>JsonTools.Text(i?["type"])=="tun"&&JsonTools.Text(i?["auto_route"])=="true");
            proxy|=inputs.Any(i=>JsonTools.Text(i?["set_system_proxy"])=="true");
            var input=inputs.FirstOrDefault(i=>profile.Core=="sing-box"?JsonTools.Text(i?["type"]) is "mixed" or "http":JsonTools.Text(i?["protocol"])=="http");
            if(int.TryParse(JsonTools.Text(input?[profile.Core=="sing-box"?"listen_port":"port"]),out var inputPort))port=inputPort;
        }
        return new(tun,proxy,port);
    }
    public static Profile Effective(Profile source,Preferences settings)
    {
        var common=settings.Connection;
        if(common==null||source.Core is not ("sing-box" or "Xray"))return source;
        var profile=SharedRouting.Copy(source);
        profile.Tun=common.Tun&&profile.Core=="sing-box";profile.SystemProxy=common.SystemProxy;profile.LocalPort=common.LocalPort;
        profile.ProxyUri="http://127.0.0.1:"+common.LocalPort;
        if(!profile.Managed)
        {
            var root=JsonTools.Object(profile.Config);
            var inbounds=root["inbounds"] as JsonArray??[];root["inbounds"]??=inbounds;
            var input=inbounds.OfType<JsonObject>().FirstOrDefault(i=>profile.Core=="sing-box"?JsonTools.Text(i["type"]) is "mixed" or "http":JsonTools.Text(i["protocol"])=="http");
            if(input==null&&profile.Core=="sing-box"){input=inbounds.OfType<JsonObject>().FirstOrDefault(i=>JsonTools.Text(i["type"])=="socks");if(input!=null)input["type"]="mixed";}
            if(input==null)
            {
                input=profile.Core=="sing-box"?new JsonObject{["type"]="mixed",["tag"]="relay-shared-mixed"}:new JsonObject{["protocol"]="http",["tag"]="relay-shared-http",["settings"]=new JsonObject()};
                inbounds.Add(input);
            }
            input["listen"]="127.0.0.1";input[profile.Core=="sing-box"?"listen_port":"port"]=common.LocalPort;
            foreach(var i in inbounds.OfType<JsonObject>())i.Remove("set_system_proxy");
            if(profile.Core=="sing-box")
            {
                if(!profile.Tun)foreach(var i in inbounds.Where(i=>JsonTools.Text(i?["type"])=="tun").ToArray())inbounds.Remove(i);
                profile.Config=root.ToJsonString(JsonTools.Options);
                if(profile.Tun)profile.Config=ApplicationRoutes.PrepareTun(profile.Config);
            }
            else profile.Config=root.ToJsonString(JsonTools.Options);
        }
        return profile;
    }
}
