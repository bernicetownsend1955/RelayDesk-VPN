using System.Text.Json;
using System.Text.Json.Nodes;

namespace RelayDesk;

public static class SharedRouting
{
    public static IReadOnlyList<QuickRoute> Rules(Profile? profile, Preferences settings) => settings.SharedQuickRoutes ?? profile?.QuickRoutes ?? [];
    public static bool NeedsTun(Profile profile, Preferences settings) => profile.Core=="sing-box" && settings.SharedQuickRoutes?.Any(r=>r.Kind is "process_name" or "process_path")==true;
    public static List<QuickRoute> Normalize(IEnumerable<QuickRoute> routes) => routes.Select(r=>r with {Outbound=r.Direct?"@direct":r.Zapret?"@zapret":"@proxy"}).ToList();
    public static IReadOnlyList<QuickRoute> ForProfile(Profile profile, Preferences settings)
    {
        var rules=Rules(profile,settings);
        if(settings.SharedQuickRoutes==null)return rules;
        if(profile.Core=="Xray")rules=rules.Where(r=>r.Kind is not ("process_path" or "process_name")).ToArray();
        if(profile.Core is not ("sing-box" or "Xray"))return rules;
        var root=JsonTools.Object(profile.Config);
        var outbounds=(root["outbounds"] as JsonArray??[]).OfType<JsonObject>().Where(o=>JsonTools.Text(o[profile.Core=="Xray"?"protocol":"type"]) is not ("direct" or "freedom" or "block" or "blackhole" or "dns")).ToArray();
        string final=JsonTools.Text(root["route"]?["final"]);
        string tag=outbounds.Select(o=>JsonTools.Text(o["tag"])).FirstOrDefault(t=>t==final&&t.Length>0)??outbounds.Select(o=>JsonTools.Text(o["tag"])).FirstOrDefault(t=>t.Length>0)??"proxy";
        return rules.Select(r=>r.Direct||r.Zapret?r:r with {Outbound=tag}).ToArray();
    }
    public static void Migrate(Store store)
    {
        if(store.Settings.SharedQuickRoutes!=null)return;
        var primary=store.Profiles.FirstOrDefault(p=>p.Name.Equals("Мой VPN",StringComparison.OrdinalIgnoreCase))??store.Profiles.FirstOrDefault(p=>p.Id==store.Settings.LastConnectedProfileId)??store.Profiles.FirstOrDefault();
        var ordered=(primary==null?Enumerable.Empty<Profile>():new[]{primary}).Concat(store.Profiles.Where(p=>p!=primary));
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        store.Settings.SharedQuickRoutes=Normalize(ordered.SelectMany(p=>p.QuickRoutes).Where(r=>seen.Add(r.Kind+"\n"+r.Value)));
        foreach(var profile in store.Profiles)profile.QuickRoutes=store.Settings.SharedQuickRoutes.ToList();
    }
    public static Profile Copy(Profile profile) => JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile))!;
}
