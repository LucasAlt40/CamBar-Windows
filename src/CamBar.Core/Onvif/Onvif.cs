using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CamBar.Core;

/// <summary>Where to send PTZ commands for one camera.</summary>
public sealed record PtzEndpoint(Uri Url, string ProfileToken);

/// <summary>SOAP messages, WS-Security and response parsing for the few ONVIF calls CamBar needs.</summary>
public static class Onvif
{
    // MARK: Request bodies

    public const string GetCapabilities = "<tds:GetCapabilities><tds:Category>All</tds:Category></tds:GetCapabilities>";
    public const string GetProfiles = "<trt:GetProfiles/>";

    public static string ContinuousMove(string token, double pan, double tilt, double zoom)
    {
        static string N(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"<tptz:ContinuousMove><tptz:ProfileToken>{token.XmlEscaped()}</tptz:ProfileToken><tptz:Velocity>"
             + $"<tt:PanTilt x=\"{N(pan)}\" y=\"{N(tilt)}\"/><tt:Zoom x=\"{N(zoom)}\"/></tptz:Velocity></tptz:ContinuousMove>";
    }

    public static string Stop(string token) =>
        $"<tptz:Stop><tptz:ProfileToken>{token.XmlEscaped()}</tptz:ProfileToken>"
        + "<tptz:PanTilt>true</tptz:PanTilt><tptz:Zoom>true</tptz:Zoom></tptz:Stop>";

    public static string GotoHome(string token) =>
        $"<tptz:GotoHomePosition><tptz:ProfileToken>{token.XmlEscaped()}</tptz:ProfileToken></tptz:GotoHomePosition>";

    // MARK: Envelope and WS-Security

    public static string Envelope(string body, string header = "") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" "
        + "xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\" "
        + "xmlns:tptz=\"http://www.onvif.org/ver20/ptz/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\">"
        + $"{header}<s:Body>{body}</s:Body></s:Envelope>";

    /// <summary>WS-Security digest: Base64(SHA1(nonce + created + password)).</summary>
    public static string PasswordDigest(byte[] nonce, string created, string password)
    {
        var data = nonce.Concat(Encoding.UTF8.GetBytes(created)).Concat(Encoding.UTF8.GetBytes(password)).ToArray();
        return Convert.ToBase64String(SHA1.HashData(data));
    }

    public static string SecurityHeader(string username, string password, byte[] nonce, string created)
    {
        var digest = PasswordDigest(nonce, created, password);
        return "<s:Header><wsse:Security s:mustUnderstand=\"1\" "
             + "xmlns:wsse=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\" "
             + "xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\">"
             + $"<wsse:UsernameToken><wsse:Username>{username.XmlEscaped()}</wsse:Username>"
             + $"<wsse:Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">{digest}</wsse:Password>"
             + $"<wsse:Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">{Convert.ToBase64String(nonce)}</wsse:Nonce>"
             + $"<wsu:Created>{created}</wsu:Created></wsse:UsernameToken></wsse:Security></s:Header>";
    }

    // MARK: Response parsing

    /// <summary>The XAddr of a service in a GetCapabilities response (Media, PTZ, …).</summary>
    public static string? ServiceAddress(string service, string xml)
    {
        var match = Regex.Match(xml, $@"<[\w]*:?{Regex.Escape(service)}>\s*<[\w]*:?XAddr>([^<]+)</");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static readonly Regex ProfileRegex = new(
        "<[\\w]*:?Profiles[^>]*token=\"([^\"]+)\"[^>]*>(.*?)</[\\w]*:?Profiles>", RegexOptions.Singleline);

    /// <summary>Token of the first media profile that has a PTZ configuration (null means the camera is fixed).</summary>
    public static string? PtzProfileToken(string xml)
    {
        foreach (Match match in ProfileRegex.Matches(xml))
            if (match.Groups[2].Value.Contains("PTZConfiguration")) return match.Groups[1].Value;
        return null;
    }

    /// <summary>Cameras often advertise an internal address; keep the path but talk to the host the user configured.</summary>
    public static Uri? Rewrite(string address, string host)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return null;
        try { return new UriBuilder(uri) { Host = host }.Uri; }
        catch (UriFormatException) { return null; }
    }

    public static string XmlEscaped(this string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
}

/// <summary>Minimal ONVIF client: discovery of the PTZ service and continuous moves.</summary>
public sealed class OnvifClient
{
    /// <summary>Ports tried when the camera has none configured (5000 is common on Yoosee-style cameras).</summary>
    public static readonly int[] DefaultPorts = [5000, 80, 8080];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly string _username;
    private readonly string _password;
    private readonly FileLog? _log;

    public OnvifClient(string username, string password, FileLog? log = null)
    {
        _username = username;
        _password = password;
        _log = log;
    }

    /// <summary>Finds the PTZ service and a profile that supports it. Null means fixed camera or no ONVIF.</summary>
    public async Task<PtzEndpoint?> DiscoverPtzAsync(string host, IEnumerable<int> ports)
    {
        foreach (var port in ports)
        {
            if (!Uri.TryCreate($"http://{host}:{port}/onvif/device_service", UriKind.Absolute, out var device)) continue;
            var capabilities = await CallAsync(device, Onvif.GetCapabilities);
            if (capabilities is null) continue;
            var ptzAddress = Onvif.ServiceAddress("PTZ", capabilities);
            var ptzUrl = ptzAddress is null ? null : Onvif.Rewrite(ptzAddress, host);
            if (ptzUrl is null) continue;

            var mediaAddress = Onvif.ServiceAddress("Media", capabilities);
            var mediaUrl = (mediaAddress is null ? null : Onvif.Rewrite(mediaAddress, host)) ?? device;
            var profiles = await CallAsync(mediaUrl, Onvif.GetProfiles);
            var token = profiles is null ? null : Onvif.PtzProfileToken(profiles);
            if (token is null) continue;
            return new PtzEndpoint(ptzUrl, token);
        }
        return null;
    }

    public Task ContinuousMoveAsync(PtzEndpoint endpoint, double pan, double tilt, double zoom) =>
        CallAsync(endpoint.Url, Onvif.ContinuousMove(endpoint.ProfileToken, pan, tilt, zoom));

    public Task StopAsync(PtzEndpoint endpoint) => CallAsync(endpoint.Url, Onvif.Stop(endpoint.ProfileToken));

    public Task GoHomeAsync(PtzEndpoint endpoint) => CallAsync(endpoint.Url, Onvif.GotoHome(endpoint.ProfileToken));

    // MARK: Transport

    /// <summary>Returns the response body on HTTP 200. Responses (never requests, which carry credentials) are logged.</summary>
    private async Task<string?> CallAsync(Uri url, string body)
    {
        var action = new string(body.Skip(1).TakeWhile(c => c != '>' && c != ' ' && c != '/').ToArray());
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(Onvif.Envelope(body, SecurityHeader()), Encoding.UTF8, "application/soap+xml"),
            };
            using var response = await Http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            var status = (int)response.StatusCode;
            var preview = text.Length > 300 ? text[..300] : text;
            _log?.Append($"{url} {action}: HTTP {status} {preview.Replace("\n", " ")}");
            return status == 200 ? text : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _log?.Append($"{url} {action}: sem resposta");
            return null;
        }
    }

    private string SecurityHeader()
    {
        if (_username.Length == 0) return "";
        var nonce = RandomNumberGenerator.GetBytes(16);
        return Onvif.SecurityHeader(_username, _password, nonce, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
    }
}
