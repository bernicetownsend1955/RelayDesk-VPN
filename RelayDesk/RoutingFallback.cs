using System.Text.Json.Nodes;

namespace RelayDesk;
public static class RoutingFallback
{
    public static readonly string[] Labels=["Исходный профиль","Через VPN","Напрямую","Через zapret"];
    public static string Value(string label)=>label switch{"Через VPN"=>"proxy","Напрямую"=>"direct","Через zapret"=>"zapret",_=>"source"};
    public static string Label(string value)=>value switch{"proxy"=>Labels[1],"direct"=>Labels[2],"zapret"=>Labels[3],_=>Labels[0]};
    public static bool Valid(string value)=>value is "source" or "proxy" or "direct" or "zapret";
    public static string Apply(string config,string core,string fallback,bool zapretEnabled)
    {
        if(fallback=="source"||fallback=="zapret"&&!zapretEnabled)return config;
        if(!Valid(fallback))throw new InvalidOperationException("Неизвестный маршрут по умолчанию.");
        var root=JsonTools.Object(config);
        var projected=JsonTools.Object(RoutingPresets.Apply(config,core,fallback=="proxy"?"proxy":"direct"));
        string tag=core=="sing-box"?JsonTools.Text(projected["route"]?["final"]):JsonTools.Text(projected["routing"]?["rules"]?[0]?["outboundTag"]);
        var outputs=root["outbounds"]!.AsArray();
        if(!outputs.Any(o=>JsonTools.Text(o?["tag"])==tag))outputs.Add(projected["outbounds"]!.AsArray().First(o=>JsonTools.Text(o?["tag"])==tag)!.DeepClone());
        if(core=="sing-box")
        {
            var route=root["route"] as JsonObject??new JsonObject();root["route"]??=route;
            if(route["rules"] is JsonArray rules)
                foreach(var rule in rules.OfType<JsonObject>().Where(r=>r.ContainsKey("outbound")&&r.All(p=>p.Key is "outbound" or "action")&&JsonTools.Text(r["action"]) is "" or "route").ToArray())rules.Remove(rule);
            route["final"]=tag;
        }
        else
        {
            var routing=root["routing"] as JsonObject??new JsonObject();root["routing"]??=routing;
            var rules=routing["rules"] as JsonArray??new JsonArray();routing["rules"]??=rules;
            foreach(var rule in rules.OfType<JsonObject>().Where(r=>r.ContainsKey("outboundTag")&&r.All(p=>p.Key is "type" or "network" or "outboundTag")&&JsonTools.Text(r["network"]) is "" or "tcp,udp" or "udp,tcp").ToArray())rules.Remove(rule);
            rules.Add(new JsonObject{["type"]="field",["network"]="tcp,udp",["outboundTag"]=tag});
        }
        return root.ToJsonString(JsonTools.Options);
    }
}
