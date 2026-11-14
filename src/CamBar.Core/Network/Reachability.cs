using System.Net.Sockets;

namespace CamBar.Core;

/// <summary>Answers "does something accept connections at host:port?".</summary>
public interface IReachabilityChecker
{
    Task<bool> IsReachableAsync(string host, int port, TimeSpan timeout);
}

/// <summary>A plain TCP connect. It only says the camera is on the network, not that the stream works.</summary>
public sealed class TcpReachability : IReachabilityChecker
{
    public async Task<bool> IsReachableAsync(string host, int port, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) return false;
        using var cts = new CancellationTokenSource(timeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException) { return false; }
    }
}
