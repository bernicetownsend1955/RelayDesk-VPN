using System.IO;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace RelayDesk;

public static class MihomoConnections
{
    public static YamlMappingNode Root(string yaml)
    {
        var stream=new YamlStream(); stream.Load(new StringReader(yaml));
        return stream.Documents.Count==1 && stream.Documents[0].RootNode is YamlMappingNode root ? root : throw new FormatException("Ожидается конфигурация mihomo YAML.");
    }
    static string Text(YamlMappingNode root,string key) => root.Children.TryGetValue(new YamlScalarNode(key),out var value) && value is YamlScalarNode scalar ? scalar.Value ?? "" : "";
    public static string Configure(string yaml,int port)
    {
        var root=Root(yaml);
        root.Children[new YamlScalarNode("external-controller")]=new YamlScalarNode("127.0.0.1:"+port);
        root.Children[new YamlScalarNode("secret")]=new YamlScalarNode(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        root.Children[new YamlScalarNode("find-process-mode")]=new YamlScalarNode("always");
        var stream=new YamlStream(new YamlDocument(root)); using var writer=new StringWriter(); stream.Save(writer,false); return writer.ToString();
    }
    public static string MonitorConfig(string yaml)
    {
        var root=Root(yaml); var outbounds=new JsonArray();
        if(root.Children.TryGetValue(new YamlScalarNode("proxies"),out var proxies) && proxies is YamlSequenceNode sequence)
            foreach(var proxy in sequence.Children.OfType<YamlMappingNode>())
            {
                var type=Text(proxy,"type") switch { "ss"=>"shadowsocks", "socks5"=>"socks",var other=>other };
                outbounds.Add(new JsonObject { ["tag"]=Text(proxy,"name"),["type"]=type });
            }
        return new JsonObject { ["outbounds"]=outbounds,["experimental"]=new JsonObject { ["clash_api"]=new JsonObject { ["external_controller"]=Text(root,"external-controller"),["secret"]=Text(root,"secret") } } }.ToJsonString();
    }
    public static List<(string Host,int Port)> Endpoints(string yaml)
    {
        var root=Root(yaml); var result=new List<(string,int)>();
        if(root.Children.TryGetValue(new YamlScalarNode("proxies"),out var proxies) && proxies is YamlSequenceNode sequence)
            foreach(var proxy in sequence.Children.OfType<YamlMappingNode>())
                if(Text(proxy,"server") is {Length:>0} host && int.TryParse(Text(proxy,"port"),out var port) && port is >0 and <=65535) result.Add((host,port));
        return result;
    }
}
