using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace CamBar.Core;

/// <summary>Something on the local network that looks like a camera, and why.</summary>
public sealed record DiscoveredDevice
{
    public required string Host { get; init; }
    /// <summary>Port that answered an RTSP OPTIONS request.</summary>
    public int? RtspPort { get; init; }
    /// <summary>Port of the ONVIF service, from the WS-Discovery answer.</summary>
    public int? OnvifPort { get; init; }
    /// <summary>Name and model the camera announces over ONVIF, when it does.</summary>
    public string? Name { get; init; }
    public string? Hardware { get; init; }

    public bool AnsweredOnvif => OnvifPort is not null;

    /// <summary>Short explanation for the list, e.g. "RTSP na porta 554 • ONVIF na porta 5000".</summary>
    public string Evidence => string.Join(" • ", new[]
    {
        RtspPort is { } rtsp ? $"RTSP na porta {rtsp}" : null,
        OnvifPort is { } onvif ? $"ONVIF na porta {onvif}" : null,
    }.Where(s => s is not null));

    public string Title => Name is { Length: > 0 } name ? name : Hardware is { Length: > 0 } hardware ? hardware : "Possível câmera";
}

/// <summary>A local IPv4 network to scan.</summary>
public sealed record LocalNetwork(IPAddress Address, int PrefixLength)
{
    /// <summary>e.g. "192.168.1.0/24": what the user is told before agreeing to the scan.</summary>
    public string Description => $"{NetworkScanner.NetworkAddress(Address, NetworkScanner.ScanPrefix(PrefixLength))}/{NetworkScanner.ScanPrefix(PrefixLength)}";
}

public readonly record struct ScanProgress(int Checked, int Total, int Found);

/// <summary>
/// Finds possible cameras on the local network, only when the user asks: an ONVIF WS-Discovery probe (multicast)
/// plus a TCP check of the RTSP ports on every address of the local subnet, confirmed with an RTSP OPTIONS request.
/// Nothing leaves the local network and no credentials are sent.
/// </summary>
public static class NetworkScanner
{
    public static readonly int[] RtspPorts = [554, 8554];
    private static readonly IPEndPoint DiscoveryEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);

    // MARK: Subnets

    /// <summary>Larger networks are narrowed to the /24 around this computer, so a scan stays at 254 addresses.</summary>
    public static int ScanPrefix(int prefixLength) => Math.Clamp(prefixLength, 24, 30);

    public static IPAddress NetworkAddress(IPAddress address, int prefix)
    {
        var value = ToUInt(address) & Mask(prefix);
        return FromUInt(value);
    }

    /// <summary>Every host address of the (narrowed) subnet, except this computer, the network and the broadcast address.</summary>
    public static List<IPAddress> HostsInSubnet(IPAddress address, int prefixLength)
    {
        var prefix = ScanPrefix(prefixLength);
        var self = ToUInt(address);
        var network = self & Mask(prefix);
        var broadcast = network | ~Mask(prefix);
        var hosts = new List<IPAddress>();
        for (var ip = network + 1; ip < broadcast; ip++)
            if (ip != self) hosts.Add(FromUInt(ip));
        return hosts;
    }

    /// <summary>Only private ranges are scanned (10/8, 172.16/12, 192.168/16): never a public network.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>Private IPv4 networks of the active adapters (skipping loopback and tunnels), one per subnet.</summary>
    public static List<LocalNetwork> LocalNetworks()
    {
        var networks = new List<LocalNetwork>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (!IsPrivate(unicast.Address) || unicast.PrefixLength is < 8 or > 30) continue;
                    var candidate = new LocalNetwork(unicast.Address, unicast.PrefixLength);
                    if (networks.All(n => n.Description != candidate.Description)) networks.Add(candidate);
                }
            }
        }
        catch (NetworkInformationException) { }
        return networks;
    }

    // MARK: WS-Discovery

    public static string ProbeMessage(Guid id) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" "
        + "xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">"
        + $"<e:Header><w:MessageID>uuid:{id}</w:MessageID><w:To e:mustUnderstand=\"true\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>"
        + "<w:Action e:mustUnderstand=\"true\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action></e:Header>"
        + "<e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body></e:Envelope>";

    private static readonly Regex XAddrsRegex = new(@"<[\w]*:?XAddrs>([^<]+)</", RegexOptions.Compiled);
    private static readonly Regex ScopesRegex = new(@"<[\w]*:?Scopes[^>]*>([^<]+)</", RegexOptions.Compiled);

    /// <summary>Devices in a ProbeMatches answer: host and ONVIF port from XAddrs, name and model from the scopes.</summary>
    public static List<DiscoveredDevice> ParseProbeMatches(string xml)
    {
        var devices = new List<DiscoveredDevice>();
        var scopes = ScopesRegex.Match(xml) is { Success: true } s ? s.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : [];
        string? Scope(string kind) => scopes
            .Select(x => x.Trim())
            .Where(x => x.StartsWith($"onvif://www.onvif.org/{kind}/", StringComparison.OrdinalIgnoreCase))
            .Select(x => Uri.UnescapeDataString(x[$"onvif://www.onvif.org/{kind}/".Length..]).Replace('_', ' ').Trim())
            .FirstOrDefault(x => x.Length > 0);

        foreach (Match match in XAddrsRegex.Matches(xml))
        {
            foreach (var address in match.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                // cameras with several interfaces list every address; IPv6 ones are skipped
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.HostNameType != UriHostNameType.IPv4) continue;
                if (devices.Any(d => d.Host == uri.Host)) continue;
                devices.Add(new DiscoveredDevice { Host = uri.Host, OnvifPort = uri.Port, Name = Scope("name"), Hardware = Scope("hardware") });
            }
        }
        return devices;
    }

    // MARK: RTSP

    public static string OptionsRequest(string host, int port) =>
        $"OPTIONS rtsp://{host}:{port}/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: CamBar\r\n\r\n";

    /// <summary>Any RTSP status line counts, including 401: the port speaks RTSP even if it wants a password.</summary>
    public static bool IsRtspResponse(string text) => text.StartsWith("RTSP/1.", StringComparison.Ordinal);

    /// <summary>Combines what each method found about the same address.</summary>
    public static DiscoveredDevice Merge(DiscoveredDevice a, DiscoveredDevice b) => a with
    {
        RtspPort = a.RtspPort ?? b.RtspPort,
        OnvifPort = a.OnvifPort ?? b.OnvifPort,
        Name = a.Name ?? b.Name,
        Hardware = a.Hardware ?? b.Hardware,
    };

    public static int CompareHosts(string a, string b)
    {
        static uint Key(string host) => IPAddress.TryParse(host, out var ip) ? ToUInt(ip) : uint.MaxValue;
        return Key(a).CompareTo(Key(b));
    }

    // MARK: Scan

    /// <summary>Runs both discovery methods on the given networks. <paramref name="found"/> is called as devices appear.</summary>
    public static async Task<List<DiscoveredDevice>> ScanAsync(IReadOnlyList<LocalNetwork> networks, IProgress<ScanProgress>? progress,
                                                              Action<DiscoveredDevice>? found, CancellationToken cancellation)
    {
        var results = new ConcurrentDictionary<string, DiscoveredDevice>();
        void Add(DiscoveredDevice device)
        {
            var merged = results.AddOrUpdate(device.Host, device, (_, existing) => Merge(existing, device));
            found?.Invoke(merged);
        }

        var hosts = networks.SelectMany(n => HostsInSubnet(n.Address, n.PrefixLength)).Distinct().ToList();
        var total = hosts.Count * RtspPorts.Length;
        var checkedCount = 0;
        progress?.Report(new ScanProgress(0, total, 0));

        var discovery = Task.WhenAll(networks.Select(n => DiscoverOnvifAsync(n.Address, Add, cancellation)));

        using var gate = new SemaphoreSlim(64);
        var probes = hosts.SelectMany(host => RtspPorts.Select(async port =>
        {
            await gate.WaitAsync(cancellation);
            try
            {
                if (await ProbeRtspAsync(host, port, cancellation))
                    Add(new DiscoveredDevice { Host = host.ToString(), RtspPort = port });
            }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref checkedCount);
                progress?.Report(new ScanProgress(done, total, results.Count));
            }
        }));

        await Task.WhenAll(probes);
        await discovery;
        return results.Values.OrderBy(d => d.Host, Comparer<string>.Create(CompareHosts)).ToList();
    }

    private static async Task DiscoverOnvifAsync(IPAddress localAddress, Action<DiscoveredDevice> add, CancellationToken cancellation)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(localAddress, 0));   // bound to the adapter, so the probe leaves through it
            var probe = Encoding.UTF8.GetBytes(ProbeMessage(Guid.NewGuid()));
            await udp.SendAsync(probe, DiscoveryEndpoint, cancellation);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            window.CancelAfter(TimeSpan.FromSeconds(4));
            while (true)
            {
                var answer = await udp.ReceiveAsync(window.Token);
                foreach (var device in ParseProbeMatches(Encoding.UTF8.GetString(answer.Buffer))) add(device);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private static async Task<bool> ProbeRtspAsync(IPAddress host, int port, CancellationToken cancellation)
    {
        using var client = new TcpClient();
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                connect.CancelAfter(TimeSpan.FromMilliseconds(600));
                await client.ConnectAsync(host, port, connect.Token);
            }
            using var answer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            answer.CancelAfter(TimeSpan.FromSeconds(2));
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(OptionsRequest(host.ToString(), port)), answer.Token);
            var buffer = new byte[64];
            var read = await stream.ReadAsync(buffer, answer.Token);
            return read > 0 && IsRtspResponse(Encoding.ASCII.GetString(buffer, 0, read));
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return false; }
        catch (Exception ex) when (ex is SocketException or IOException) { return false; }
    }

    // MARK: Helpers

    private static uint Mask(int prefix) => prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);

    private static uint ToUInt(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static IPAddress FromUInt(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
