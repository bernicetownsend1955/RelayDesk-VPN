using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace RelayDesk;

public static class TunRuntime
{
    static readonly string instance=Guid.NewGuid().ToString("N")[..8];
    public static string SelectAddress(IEnumerable<string> occupied)
    {
        var networks=occupied.ToArray();
        for(int second=30;second>=16;second--)
            for(int third=240;third>=16;third--)
            {
                var candidate=$"172.{second}.{third}.1/30";
                if(!networks.Any(n=>RuleMatching.NetworkContains(n,candidate)||RuleMatching.NetworkContains(candidate,n)))return candidate;
            }
        throw new InvalidOperationException("Не найдена свободная подсеть для TUN RelayDesk.");
    }
    public static string Configure(string config,IEnumerable<string>? occupied=null,string? interfacePrefix=null)
    {
        var root=JsonTools.Object(config);
        var tuns=(root["inbounds"] as JsonArray??[]).OfType<JsonObject>().Where(i=>JsonTools.Text(i["type"])=="tun").ToArray();
        if(tuns.Length==0)return config;
        var used=(occupied??OccupiedNetworks()).ToList();
        int index=0;
        foreach(var tun in tuns)
        {
            var addresses=(tun["address"] as JsonArray??[]).Select(a=>JsonTools.Text(a)).ToArray();
            var ipv6=addresses.Where(a=>IPAddress.TryParse(a.Split('/')[0],out var ip)&&ip.AddressFamily==AddressFamily.InterNetworkV6).ToArray();
            var address=SelectAddress(used);used.Add(address);
            tun["address"]=new JsonArray(new[]{address}.Concat(ipv6).Select(a=>(JsonNode?)JsonValue.Create(a)).ToArray());
            tun["interface_name"]=(interfacePrefix??"RelayDesk-"+instance)+"-"+index++;
        }
        return root.ToJsonString(JsonTools.Options);
    }
    public static IReadOnlyList<string> OccupiedNetworks()
    {
        var result=new List<string>();
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())
            foreach(var address in nic.GetIPProperties().UnicastAddresses.Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork))
                result.Add(address.Address+"/"+address.PrefixLength);
        if(!OperatingSystem.IsWindows())return result;
        int size=0;
        if(GetIpForwardTable(IntPtr.Zero,ref size,false)!=122||size<=0)return result;
        var memory=Marshal.AllocHGlobal(size);
        try
        {
            if(GetIpForwardTable(memory,ref size,false)!=0)return result;
            int count=Marshal.ReadInt32(memory);
            for(int i=0;i<count&&4+(i+1)*56<=size;i++)
            {
                var row=IntPtr.Add(memory,4+i*56);var network=new byte[4];var mask=new byte[4];Marshal.Copy(row,network,0,4);Marshal.Copy(IntPtr.Add(row,4),mask,0,4);
                int prefix=mask.Sum(b=>System.Numerics.BitOperations.PopCount((uint)b));
                // Default and split-default routes belong to the upstream VPN, not occupied local subnets.
                if(prefix>=8)result.Add(new IPAddress(network)+"/"+prefix);
            }
        }
        finally{Marshal.FreeHGlobal(memory);}
        return result;
    }
    [DllImport("iphlpapi.dll")]static extern uint GetIpForwardTable(IntPtr table,ref int size,[MarshalAs(UnmanagedType.Bool)]bool order);
}
