using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RelayDesk;

public static class DirectNetwork
{
    public static NetworkInterface Interface()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 && !n.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase) && !n.Description.Contains("tun", StringComparison.OrdinalIgnoreCase) && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork) && n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork)).ToArray();
        return interfaces.Length == 1 ? interfaces[0] : throw new InvalidOperationException(interfaces.Length == 0 ? "Не найден обычный IPv4-интерфейс для проверки Discord без VPN." : "Найдено несколько сетевых интерфейсов. Нельзя однозначно выбрать путь для проверки без VPN.");
    }
    public static void Bind(Socket socket, NetworkInterface network)
    {
        var properties = network.GetIPProperties();
        socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(properties.GetIPv4Properties().Index)); // Windows IP_UNICAST_IF
        socket.Bind(new IPEndPoint(properties.UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Address, 0));
    }
    public static SocketsHttpHandler Handler(NetworkInterface network) => new()
    {
        UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(6),
        ConnectCallback = async (context, cancellation) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, AddressFamily.InterNetwork, cancellation);
            Exception? last = null;
            foreach (var address in addresses)
            {
                // Do not report a fake-IP path through another VPN as a direct test.
                var bytes = address.GetAddressBytes();
                if (bytes[0] == 198 && bytes[1] is 18 or 19) continue;
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { Bind(socket, network); await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation); return new NetworkStream(socket, true); }
                catch (Exception ex) { socket.Dispose(); if (cancellation.IsCancellationRequested) throw; last = ex; }
            }
            throw new HttpRequestException("Прямое подключение через " + network.Name + " не удалось. Проверь DNS и доступность IPv4.", last);
        }
    };
}
