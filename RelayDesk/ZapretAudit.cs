using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RelayDesk;

public record Strategy(string Name, string Arguments)
{
    public override string ToString() => Name;
}
public record DiscordReport(string Name, string Api, string Gateway, string Voice, long? Ms, bool Passed, string Arguments = "");

public static class ZapretAudit
{
    public static bool ShouldProbeVoice(Preferences settings) => settings.VoiceDiscoveryEnabled && settings.VoiceEndpoint.Length > 0 && settings.VoiceSsrc != 0;
    public static DiscordReport? Best(IEnumerable<DiscordReport> reports) => reports.Skip(1).Where(r => r.Passed && r.Ms is >= 0).OrderBy(r => r.Ms).FirstOrDefault();
    public const string DefaultStrategies = "Разделение TLS\n--dpi-desync=multisplit\n--dpi-desync-split-pos=1,midsld\n---\nFake и split\n--dpi-desync=fake,multisplit\n--dpi-desync-split-pos=1,midsld\n--dpi-desync-fooling=md5sig\n---\nFake и disorder\n--dpi-desync=fake,multidisorder\n--dpi-desync-split-pos=1,midsld\n--dpi-desync-fooling=badseq,md5sig";
    public static List<Strategy> Parse(string text)
    {
        var result = new List<Strategy>();
        foreach (var block in text.Replace("\r", "").Split("\n---\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2 || lines[0].StartsWith("--")) throw new FormatException("Стратегия: название, затем DPI-аргументы. Разделитель между стратегиями — строка ---.");
            result.Add(new(lines[0], string.Join('\n', lines.Skip(1))));
        }
        if (result.Count is 0 or > 64) throw new FormatException("Укажи от 1 до 64 стратегий.");
        return result;
    }
    public static async Task<DiscordReport> Probe(string name, Preferences settings, CancellationToken cancellation, string args = "")
    {
        string api = "Ошибка", gateway = "Ошибка", voice = settings.VoiceDiscoveryEnabled ? "Не проверен: нет endpoint и SSRC" : "Голос не проверен; UDP-тест отключён (WebRTC не поддерживается)";
        bool apiOk = false, gatewayOk = false, voiceOk = false;
        var watch = Stopwatch.StartNew();
        var network = DirectNetwork.Interface();
        using var handler = DirectNetwork.Handler(network);
        using var client = new HttpClient(handler, false) { Timeout = TimeSpan.FromSeconds(8) };
        try
        {
            using var response = await client.GetAsync("https://discord.com/api/v10/gateway", cancellation);
            var body = await response.Content.ReadAsStringAsync(cancellation);
            api = "HTTP " + (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                try { using var document = JsonDocument.Parse(body); apiOk = document.RootElement.TryGetProperty("url", out var url) && url.GetString()?.StartsWith("wss://gateway.discord.gg", StringComparison.Ordinal) == true; }
                catch (JsonException) { api += "; ответ не является JSON Discord"; }
            }
            if (apiOk) api = "API отвечает";
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested) { api = "API: " + ProgramJournal.Clean(ex.Message); }
        try
        {
            using var socket = new ClientWebSocket();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var invoker = new HttpMessageInvoker(DirectNetwork.Handler(network));
            await socket.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), invoker, timeout.Token);
            var buffer = new byte[16384]; var used = 0;
            ValueWebSocketReceiveResult received;
            do { received = await socket.ReceiveAsync(buffer.AsMemory(used), timeout.Token); used += received.Count; if (used >= buffer.Length) throw new FormatException("Gateway response too large"); } while (!received.EndOfMessage);
            using var doc = JsonDocument.Parse(buffer.AsMemory(0, used)); gatewayOk = doc.RootElement.TryGetProperty("op", out var op) && op.GetInt32() == 10;
            gateway = gatewayOk ? "WebSocket Hello" : "Нет Hello"; socket.Abort();
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested) { gateway = "Gateway: " + ProgramJournal.Clean(ex.Message); }
        if (ShouldProbeVoice(settings))
        {
            try { await VoiceDiscovery(settings.VoiceEndpoint, settings.VoiceSsrc, cancellation); voiceOk = true; voice = "UDP IP discovery отвечает; аудио не проверено"; }
            catch (Exception) when (!cancellation.IsCancellationRequested) { voice = "Нет ответа UDP IP discovery"; }
        }
        cancellation.ThrowIfCancellationRequested();
        var passed = apiOk && gatewayOk && (!ShouldProbeVoice(settings) || voiceOk);
        return new(name, api, gateway, voice, passed ? watch.ElapsedMilliseconds : null, passed, args);
    }
    public static byte[] DiscoveryPacket(uint ssrc)
    {
        var packet = new byte[74]; BinaryPrimitives.WriteUInt16BigEndian(packet, 1); BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), 70); BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), ssrc); return packet;
    }
    public static bool ValidDiscovery(ReadOnlySpan<byte> packet, uint ssrc) => packet.Length == 74 && BinaryPrimitives.ReadUInt16BigEndian(packet) == 2 && BinaryPrimitives.ReadUInt16BigEndian(packet[2..]) == 70 && BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) == ssrc;
    static async Task VoiceDiscovery(string endpoint, uint ssrc, CancellationToken cancellation)
    {
        if (!IPEndPoint.TryParse(endpoint, out var remote) || remote.Port <= 0) throw new FormatException("Голосовой endpoint: IP:порт из текущей голосовой сессии Discord.");
        if (remote.AddressFamily != AddressFamily.InterNetwork) throw new FormatException("Для прямой UDP-проверки нужен IPv4 endpoint.");
        using var socket = new UdpClient(remote.AddressFamily); DirectNetwork.Bind(socket.Client, DirectNetwork.Interface()); socket.Connect(remote);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await socket.SendAsync(DiscoveryPacket(ssrc), timeout.Token);
        var response = await socket.ReceiveAsync(timeout.Token);
        if (!ValidDiscovery(response.Buffer, ssrc)) throw new FormatException("Некорректный UDP IP discovery.");
    }
}
