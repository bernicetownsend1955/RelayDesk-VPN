using System.IO;
using System.Net;

namespace RelayDesk;

public record DemoConnection(string App,string ProcessName,string ProcessPath,string Host,string Ip,string Protocol,int Port,bool CanApplication=true,bool CanEdit=true,string ActualRoute="",string ActualRule="",string Destination="");
public record RuleOverlap(RouteRule Existing,string Reason,bool Exact,bool SameRoute);
public static class RuleMatching
{
    public static string TargetKind(RouteRule rule)=>rule.Kind=="browser_default"?"process_path":rule.Kind;
    public static bool SameTarget(RouteRule a,RouteRule b)=>TargetKind(a)==TargetKind(b)&&a.Value.Equals(b.Value,StringComparison.OrdinalIgnoreCase);
    public static bool DomainIn(string host,string suffix)=>host.Equals(suffix,StringComparison.OrdinalIgnoreCase)||host.EndsWith("."+suffix,StringComparison.OrdinalIgnoreCase);
    public static bool NetworkContains(string network,string address)
    {
        var parts=network.Split('/');
        if(parts.Length!=2||!IPAddress.TryParse(parts[0],out var net)||!IPAddress.TryParse(address.Split('/')[0],out var ip)||!int.TryParse(parts[1],out int prefix))return false;
        var a=net.GetAddressBytes();var b=ip.GetAddressBytes();if(a.Length!=b.Length||prefix<0||prefix>a.Length*8)return false;
        for(int i=0;i<a.Length;i++){int bits=Math.Clamp(prefix-i*8,0,8);int mask=(255<<(8-bits))&255;if((a[i]&mask)!=(b[i]&mask))return false;}
        return true;
    }
    public static bool Matches(RouteRule rule,DemoConnection connection)=>rule.Kind switch
    {
        "default"=>true,
        "process_path" or "browser_default"=>rule.Value.Length>0&&rule.Value.Equals(connection.ProcessPath,StringComparison.OrdinalIgnoreCase),
        "process_name"=>rule.Value.Equals(connection.ProcessName,StringComparison.OrdinalIgnoreCase),
        "domain"=>rule.Value.Equals(connection.Host,StringComparison.OrdinalIgnoreCase),
        "domain_suffix"=>DomainIn(connection.Host,rule.Value),
        "domain_keyword"=>connection.Host.Contains(rule.Value,StringComparison.OrdinalIgnoreCase),
        "ip_cidr"=>NetworkContains(rule.Value,connection.Ip),
        _=>false
    };
    public static RouteRule? Winner(IEnumerable<RouteRule> rules,DemoConnection connection)=>rules.FirstOrDefault(r=>Matches(r,connection));
    public static List<RuleOverlap> Overlaps(RouteRule candidate,IEnumerable<RouteRule> rules)
    {
        var results=new List<RuleOverlap>();
        foreach(var rule in rules.Where(r=>r.Id!=candidate.Id&&r.Kind!="default"))
        {
            bool exact=SameTarget(candidate,rule),overlap=exact;
            string reason="Такое правило уже существует.";
            string a=TargetKind(candidate),b=TargetKind(rule);
            if(!overlap&&a.StartsWith("domain")&&b.StartsWith("domain"))
            {
                overlap=(a,b) switch
                {
                    ("domain","domain_suffix")=>DomainIn(candidate.Value,rule.Value),
                    ("domain_suffix","domain")=>DomainIn(rule.Value,candidate.Value),
                    ("domain_suffix","domain_suffix")=>DomainIn(candidate.Value,rule.Value)||DomainIn(rule.Value,candidate.Value),
                    ("domain_keyword",_)=>rule.Value.Contains(candidate.Value,StringComparison.OrdinalIgnoreCase),
                    (_,"domain_keyword")=>candidate.Value.Contains(rule.Value,StringComparison.OrdinalIgnoreCase),
                    _=>false
                };
                reason=a=="domain"&&b=="domain_suffix"?"Домен "+candidate.Value+" уже входит в *."+rule.Value+".":"Доменные правила пересекаются.";
            }
            if(!overlap&&a=="ip_cidr"&&b=="ip_cidr")
            {
                overlap=NetworkContains(candidate.Value,rule.Value)||NetworkContains(rule.Value,candidate.Value);reason="IP-правила пересекаются.";
            }
            if(!overlap&&(a=="process_path"&&b=="process_name"||a=="process_name"&&b=="process_path"))
            {
                overlap=Path.GetFileName(candidate.Value).Equals(Path.GetFileName(rule.Value),StringComparison.OrdinalIgnoreCase);reason="Имя процесса совпадает с EXE другого правила.";
            }
            if(overlap)results.Add(new(rule,reason,exact,candidate.Route==rule.Route));
        }
        return results;
    }
}
