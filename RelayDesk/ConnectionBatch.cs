using System.Net;

namespace RelayDesk;

public static class ConnectionBatch
{
    public static string Cell(DemoConnection c,int column)=>column switch{0=>c.App.Equals(c.ProcessName,StringComparison.OrdinalIgnoreCase)?c.App:c.App+" · "+c.ProcessName,1=>c.Destination,2=>c.Protocol,3=>c.ActualRoute,4=>c.ActualRule,_=>""};
    public static bool Matches(DemoConnection c,string search,IReadOnlyList<string> filters)
    {
        if(!string.Join(' ',Enumerable.Range(0,5).Select(i=>Cell(c,i))).Contains(search.Trim(),StringComparison.OrdinalIgnoreCase))return false;
        for(int i=0;i<5;i++)if(!Cell(c,i).Contains(filters[i].Trim(),StringComparison.OrdinalIgnoreCase))return false;
        return true;
    }
    public static List<RouteRule> Create(IEnumerable<DemoConnection> connections,int scope,string route,IReadOnlyList<RouteRule> existing)
    {
        var result=new List<RouteRule>();
        foreach(var c in connections)
        {
            if(!c.CanEdit)throw new InvalidOperationException("Для части выбранных соединений ядро не поддерживает быстрые правила. Сними их выделение.");
            string kind,value;
            if(scope==1)
            {
                if(!c.CanApplication)throw new InvalidOperationException("Ядро не сообщает приложение для части выбранных соединений. Выбери домены / IP или измени выделение.");
                kind=c.ProcessPath.Length>0?"process_path":"process_name";value=c.ProcessPath.Length>0?c.ProcessPath:c.ProcessName;
            }
            else if(c.Host.Length>0){kind=scope==2?"domain_suffix":"domain";value=c.Host;}
            else if(IPAddress.TryParse(c.Ip,out _)){kind="ip_cidr";value=c.Ip;}
            else throw new InvalidOperationException("У соединения отсутствуют домен и IP. Измени выделение.");
            value=RouteRules.Normalize(kind,value);
            var rule=new RouteRule{Kind=kind,Value=value,Name=scope==1?c.App:value,Route=route};
            var old=existing.FirstOrDefault(r=>r.Kind!="default"&&RuleMatching.SameTarget(r,rule));
            if(old!=null){rule.Id=old.Id;rule.QuickKey=old.QuickKey;}
            if(!result.Any(r=>RuleMatching.SameTarget(r,rule)))result.Add(rule);
        }
        return result;
    }
    public static List<RouteRule> Merge(IReadOnlyList<RouteRule> existing,IReadOnlyList<RouteRule> candidates)
        =>candidates.Concat(existing.Where(old=>!candidates.Any(r=>RuleMatching.SameTarget(old,r)))).ToList();
}
