using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace RelayDesk;

public readonly record struct TrafficBytes(long Download,long Upload);
public readonly record struct TrafficRate(double Download,double Upload);
public sealed class TrafficMeter
{
    Dictionary<string,TrafficBytes>? previous;
    double time;
    public void Reset(){previous=null;time=0;}
    public TrafficRate? Sample(Dictionary<string,TrafficBytes> counters,double seconds)
    {
        var old=previous;var elapsed=seconds-time;previous=counters;time=seconds;
        if(old==null||elapsed<=0)return null;
        double down=0,up=0;
        foreach(var (id,current) in counters)
        {
            old.TryGetValue(id,out var before);
            down+=Math.Max(0,current.Download-before.Download);up+=Math.Max(0,current.Upload-before.Upload);
        }
        return new(down*8/1_000_000/elapsed,up*8/1_000_000/elapsed);
    }
}

public sealed class VpnTrafficReader:IDisposable
{
    readonly HttpClient http=new(new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(2),MaxResponseContentBufferSize=16*1024*1024};
    public static string ConfigureXray(string config,int port)
    {
        var root=JsonTools.Object(config);
        root["stats"]??=new JsonObject();root["policy"]??=new JsonObject();root["policy"]!["system"]??=new JsonObject();
        root["policy"]!["system"]!["statsOutboundUplink"]=true;root["policy"]!["system"]!["statsOutboundDownlink"]=true;
        root["metrics"]??=new JsonObject();
        root["metrics"]!["listen"]="127.0.0.1:"+port;
        if(JsonTools.Text(root["metrics"]!["tag"]).Length==0)
        {
            string tag="relay-traffic-metrics";
            while((root["outbounds"] as JsonArray??[]).Any(o=>JsonTools.Text(o?["tag"])==tag))tag+="-1";
            root["metrics"]!["tag"]=tag;
        }
        return root.ToJsonString(JsonTools.Options);
    }
    public async Task<Dictionary<string,TrafficBytes>> Read(string core,string config,CancellationToken cancellation)
    {
        if(core=="Xray")
        {
            var root=JsonTools.Object(config);var listen=JsonTools.Text(root["metrics"]?["listen"]);
            if(!Uri.TryCreate("http://"+listen,UriKind.Absolute,out var uri)||!IPAddress.TryParse(uri.Host,out var ip)||!IPAddress.IsLoopback(ip)||uri.AbsolutePath!="/"||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)throw new InvalidOperationException("Переподключи Xray для включения счётчиков.");
            using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(uri,"/debug/vars"));
            return ParseXray(await Get(request,cancellation),root);
        }
        if(core is not ("sing-box" or "mihomo"))throw new InvalidOperationException("Ядро не поддерживает счётчики VPN.");
        var monitor=core=="mihomo"?MihomoConnections.MonitorConfig(config):config;
        var endpoint=ConnectionMonitor.Endpoint(monitor);
        using var query=new HttpRequestMessage(HttpMethod.Get,new Uri(endpoint.Address,"/connections"));
        if(endpoint.Secret.Length>0)query.Headers.Authorization=new AuthenticationHeaderValue("Bearer",endpoint.Secret);
        return ParseConnections(await Get(query,cancellation),JsonTools.Object(monitor));
    }
    async Task<JsonObject> Get(HttpRequestMessage request,CancellationToken cancellation)
    {
        using var response=await http.SendAsync(request,cancellation);response.EnsureSuccessStatusCode();
        return JsonTools.Object(await response.Content.ReadAsStringAsync(cancellation));
    }
    static long Number(JsonNode? value)=>long.TryParse(JsonTools.Text(value),out var result)?Math.Max(0,result):0;
    public static Dictionary<string,TrafficBytes> ParseConnections(JsonObject data,JsonObject config)
    {
        if(data["connections"] is not JsonArray connections)throw new FormatException("Ядро не вернуло счётчики соединений.");
        var types=(config["outbounds"] as JsonArray??[]).OfType<JsonObject>().Where(o=>JsonTools.Text(o["tag"]).Length>0).ToDictionary(o=>JsonTools.Text(o["tag"]),o=>JsonTools.Text(o["type"]));
        var result=new Dictionary<string,TrafficBytes>(StringComparer.Ordinal);
        foreach(var row in connections.OfType<JsonObject>())
        {
            var chain=(row["chains"] as JsonArray??[]).Select(JsonTools.Text).ToArray();
            if(ConnectionMonitor.Classify(chain,types)!="Через VPN / прокси")continue;
            var id=JsonTools.Text(row["id"]);if(id.Length==0)continue;
            result[id]=new(Number(row["download"]),Number(row["upload"]));
        }
        return result;
    }
    public static Dictionary<string,TrafficBytes> ParseXray(JsonObject data,JsonObject config)
    {
        if(data["stats"]?["outbound"] is not JsonObject counters)throw new FormatException("Xray не вернул счётчики выходов.");
        var outputs=(config["outbounds"] as JsonArray??[]).OfType<JsonObject>().ToArray();
        var nested=outputs.SelectMany(o=>new[]{JsonTools.Text(o["proxySettings"]?["tag"]),JsonTools.Text(o["streamSettings"]?["sockopt"]?["dialerProxy"])}).Where(t=>t.Length>0).ToHashSet(StringComparer.Ordinal);
        var result=new Dictionary<string,TrafficBytes>(StringComparer.Ordinal);
        foreach(var output in outputs.Where(o=>JsonTools.Text(o["protocol"]) is not ("freedom" or "blackhole" or "dns" or "loopback")))
        {
            var tag=JsonTools.Text(output["tag"]);if(nested.Contains(tag)||counters[tag] is not JsonObject count)continue;
            result[tag]=new(Number(count["downlink"]),Number(count["uplink"]));
        }
        return result;
    }
    public void Dispose()=>http.Dispose();
}
