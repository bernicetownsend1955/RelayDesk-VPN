using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace RelayDesk;

public static class Diagnostics
{
    public static async Task<long> Tcp(string host, int port, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("Не указан адрес сервера для TCP.");
        JsonTools.Port(port.ToString());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient(); var watch = Stopwatch.StartNew();
        await client.ConnectAsync(host, port, timeout.Token);
        return watch.ElapsedMilliseconds;
    }
    public static async Task<(long Ms, int Status)> Https(Uri proxy, string target, CancellationToken cancellation = default)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new FormatException("Адрес проверки должен начинаться с https:// и не содержать логин.");
        // Explicit proxy, no direct fallback, no certificate validation bypass, no redirects.
        using var handler = new SocketsHttpHandler { UseProxy = true, Proxy = new WebProxy(proxy), AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(6), UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        var watch = Stopwatch.StartNew();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
        return (watch.ElapsedMilliseconds, (int)response.StatusCode);
    }
}
