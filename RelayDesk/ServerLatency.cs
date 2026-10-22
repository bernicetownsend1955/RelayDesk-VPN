namespace RelayDesk;

public record ServerLatency(string Profile,string Core,string Server,string Result,long? Milliseconds);

public static class ServerLatencyProbe
{
    public static async Task Run(IEnumerable<Profile> profiles,Action<ServerLatency> completed,CancellationToken cancellation)
    {
        var targets=new List<(Profile Profile,string Host,int Port)>();
        foreach(var profile in profiles)
        {
            try
            {
                var endpoints=profile.Core=="mihomo" ? MihomoConnections.Endpoints(profile.Config) : JsonTools.Endpoints(profile);
                if(endpoints.Count==0) completed(new(profile.Name,profile.Core,"—","Адрес сервера не задан",null));
                foreach(var endpoint in endpoints.Distinct()) targets.Add((profile,endpoint.Host,endpoint.Port));
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or YamlDotNet.Core.YamlException)
            { completed(new(profile.Name,profile.Core,"—","Не удалось прочитать адрес сервера",null)); }
        }
        if(targets.Count>256) throw new InvalidOperationException("Для одной проверки выбери группу с количеством адресов не больше 256.");
        using var gate=new SemaphoreSlim(4);
        await Task.WhenAll(targets.Select(async target=>
        {
            await gate.WaitAsync(cancellation);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if(target.Profile.Protocol.StartsWith("HYSTERIA",StringComparison.OrdinalIgnoreCase) || target.Profile.Protocol.Equals("TUIC",StringComparison.OrdinalIgnoreCase))
                { completed(new(target.Profile.Name,target.Profile.Core,target.Host+":"+target.Port,"UDP-профиль: TCP не проверяется",null)); return; }
                try { var ms=await Diagnostics.Tcp(target.Host,target.Port,cancellation); completed(new(target.Profile.Name,target.Profile.Core,target.Host+":"+target.Port,ms+" мс",ms)); }
                catch(Exception ex) when ((ex is System.Net.Sockets.SocketException or OperationCanceledException) && !cancellation.IsCancellationRequested)
                { completed(new(target.Profile.Name,target.Profile.Core,target.Host+":"+target.Port,"Нет ответа TCP",null)); }
            }
            finally { gate.Release(); }
        }));
    }
}
