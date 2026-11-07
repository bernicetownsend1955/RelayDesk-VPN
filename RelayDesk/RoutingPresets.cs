using System.Net;
using System.Text.Json.Nodes;

namespace RelayDesk;

public static class RoutingPresets
{
    public static bool Valid(string preset)=>preset is "rules" or "direct" or "proxy";
    public static string Label(string preset)=>preset switch{"direct"=>"Всё напрямую","proxy"=>"Всё через VPN",_=>"По правилам"};
    public static string Apply(string config,string core,string preset)
    {
        if(preset=="rules")return config;
        if(!Valid(preset))throw new InvalidOperationException("Неизвестный пресет маршрутизации.");
        if(core is not ("sing-box" or "Xray"))throw new InvalidOperationException("Быстрые пресеты поддерживают sing-box и Xray.");
        var root=JsonTools.Object(config);
        var outbounds=root["outbounds"] as JsonArray??throw new FormatException("В конфигурации нет выходов.");
        bool direct=preset=="direct";
        string kind=core=="sing-box"?"type":"protocol";
        var candidates=outbounds.OfType<JsonObject>().Where(o=>JsonTools.Text(o[kind]) is not ("direct" or "freedom" or "block" or "blackhole" or "dns")).ToArray();
        string final=JsonTools.Text(root["route"]?["final"]);
        string tag;
        if(direct)
        {
            var existing=outbounds.OfType<JsonObject>().FirstOrDefault(o=>JsonTools.Text(o[kind])==(core=="sing-box"?"direct":"freedom")&&o["detour"]==null&&o["proxySettings"]==null&&o["streamSettings"]?["sockopt"]?["dialerProxy"]==null&&JsonTools.Text(o["tag"]).Length>0);
            tag=existing==null?Unique(outbounds,"relay-preset-direct"):JsonTools.Text(existing["tag"]);
            if(existing==null)outbounds.Add(new JsonObject{[kind]=core=="sing-box"?"direct":"freedom",["tag"]=tag});
        }
        else tag=candidates.Select(o=>JsonTools.Text(o["tag"])).FirstOrDefault(t=>t.Length>0&&t==final)??candidates.Select(o=>JsonTools.Text(o["tag"])).FirstOrDefault(t=>t.Length>0)??throw new InvalidOperationException("В профиле нет выхода VPN. Выбери сервер.");
        if(core=="Xray")
        {
            var routing=root["routing"] as JsonObject??new JsonObject();root["routing"]??=routing;
            routing["rules"]=new JsonArray(new JsonObject{["type"]="field",["network"]="tcp,udp",["outboundTag"]=tag});
        }
        else
        {
            var route=root["route"] as JsonObject??new JsonObject();root["route"]??=route;
            var rules=new JsonArray();
            foreach(var service in (route["rules"] as JsonArray??[]).Where(r=>JsonTools.Text(r?["action"]) is "sniff" or "hijack-dns" or "resolve"))rules.Add(service?.DeepClone());
            rules.Add(new JsonObject{["action"]="route",["outbound"]=tag});route["rules"]=rules;route["final"]=tag;
            ConfigureDns(root,outbounds,route,tag,direct);
        }
        return root.ToJsonString(JsonTools.Options);
    }
    static string Unique(JsonArray nodes,string name){while(nodes.Any(n=>JsonTools.Text(n?["tag"])==name))name+="-1";return name;}
    static void ConfigureDns(JsonObject root,JsonArray outbounds,JsonObject route,string tag,bool direct)
    {
        var dns=root["dns"] as JsonObject??new JsonObject();root["dns"]??=dns;
        var servers=dns["servers"] as JsonArray??new JsonArray();dns["servers"]??=servers;
        string bootstrap="";
        if(!direct)
        {
            bootstrap=Unique(servers,"relay-preset-bootstrap");
            foreach(var outbound in outbounds.OfType<JsonObject>().Where(o=>o["server"]!=null&&!IPAddress.TryParse(JsonTools.Text(o["server"]),out _)))outbound["domain_resolver"]=bootstrap;
            route["default_domain_resolver"]=bootstrap;
        }
        for(int i=0;i<servers.Count;i++)
        {
            if(servers[i] is not JsonObject server)continue;
            var type=JsonTools.Text(server["type"]);
            if(!direct&&type=="local")
            {
                server=new JsonObject{["type"]="https",["tag"]=JsonTools.Text(server["tag"]),["server"]="1.1.1.1",["path"]="/dns-query"};servers[i]=server;type="https";
            }
            if(type is "udp" or "tcp" or "tls" or "https" or "quic" or "http3"||server.ContainsKey("detour"))
            {
                server["detour"]=tag;
                if(!direct)server["domain_resolver"]=bootstrap;
            }
        }
        if(!direct)
        {
            if(servers.Count==0){servers.Add(new JsonObject{["type"]="https",["tag"]="relay-preset-dns",["server"]="1.1.1.1",["path"]="/dns-query",["detour"]=tag});dns["final"]="relay-preset-dns";}
            servers.Add(new JsonObject{["type"]="local",["tag"]=bootstrap});
        }
    }
}
