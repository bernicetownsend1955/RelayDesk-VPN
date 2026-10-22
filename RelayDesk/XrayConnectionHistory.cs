using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RelayDesk;

public sealed class XrayConnectionHistory
{
    readonly ConcurrentQueue<(long Id,DateTimeOffset Time,string Host,string Port,string Network,string Outbound)> events=new();
    long sequence;
    static readonly Regex Access = new(@"\baccepted\s+(?<target>\S+)\s+\[(?<chain>[^\]]+)\]",RegexOptions.Compiled);
    public static string Configure(string config)
    {
        var root=JsonTools.Object(config); root["log"] ??= new JsonObject(); root["log"]!["access"]="";
        return root.ToJsonString(JsonTools.Options);
    }
    public bool Record(string line)
    {
        var match=Access.Match(line); if(!match.Success) return false;
        var chain=Regex.Split(match.Groups["chain"].Value,@"\s*(?:->|>>)\s*"); if(chain.Length<2) return false;
        var target=match.Groups["target"].Value; var network=target.StartsWith("udp:",StringComparison.Ordinal) ? "UDP" : "TCP";
        if(target.StartsWith("tcp:") || target.StartsWith("udp:")) target="tcp://"+target[4..];
        else if(target.StartsWith("//")) target="http:"+target;
        if(!Uri.TryCreate(target,UriKind.Absolute,out var destination) || destination.Host.Length==0 || destination.Port<1) return false;
        events.Enqueue((Interlocked.Increment(ref sequence),DateTimeOffset.UtcNow,destination.IdnHost.Trim('[',']'),destination.Port.ToString(),network,chain[^1].Trim()));
        while(events.Count>3000) events.TryDequeue(out _); return true;
    }
    public void Clear() { while(events.TryDequeue(out _)) { } }
    public List<ConnectionRow> Read(string config,bool zapret,string[] arguments)
    {
        var types=(JsonTools.Object(config)["outbounds"] as JsonArray ?? []).OfType<JsonObject>().Where(o=>JsonTools.Text(o["tag"]).Length>0).GroupBy(o=>JsonTools.Text(o["tag"])).ToDictionary(g=>g.Key,g=>JsonTools.Text(g.First()["protocol"]) switch { "freedom"=>"direct","blackhole"=>"block",var other=>other });
        return events.ToArray().Where(e=>e.Time>DateTimeOffset.UtcNow.AddMinutes(-3)).TakeLast(1000).Select(e=>
        {
            var route=ConnectionMonitor.Classify([e.Outbound],types);
            var scope=!zapret ? "В RelayDesk выключен" : route=="Напрямую" ? ConnectionMonitor.MatchZapret(e.Network,e.Port,e.Host,arguments) : "Внешний туннель";
            return new ConnectionRow("xray-"+e.Id,"Не определено ядром",e.Host+":"+e.Port,e.Network,route,e.Outbound,"Журнал Xray / "+e.Time.ToLocalTime().ToString("HH:mm:ss"),scope,"Не сообщает ядро") { Host=IPAddress.TryParse(e.Host,out _) ? "" : e.Host,Address=IPAddress.TryParse(e.Host,out _) ? e.Host : "",CanEdit=true };
        }).ToList();
    }
}
