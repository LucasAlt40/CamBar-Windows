using System.Text.Json;
using System.Text.Json.Serialization;

namespace CamBar.Core;

/// <summary>How ffmpeg talks RTSP to the camera.</summary>
public enum RtspTransport { Auto, Udp, Tcp, Http }

public static class RtspTransportExtensions
{
    /// <summary>Arguments that force this transport; <c>Auto</c> lets ffmpeg negotiate.</summary>
    public static string[] FfmpegArguments(this RtspTransport transport) =>
        transport == RtspTransport.Auto ? [] : ["-rtsp_transport", transport.ToString().ToLowerInvariant()];
}

/// <summary>Reads the transport tolerantly: data saved by older versions may hold null or an unknown name.</summary>
public sealed class TolerantTransportConverter : JsonConverter<RtspTransport>
{
    public override bool HandleNull => true;

    public override RtspTransport Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Enum.TryParse<RtspTransport>(reader.GetString(), true, out var value)
            ? value
            : RtspTransport.Auto;

    public override void Write(Utf8JsonWriter writer, RtspTransport value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

/// <summary>A camera as the user configured it. The password is not part of it: it lives in the Credential Manager.</summary>
public sealed record Camera
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 554;
    public string Path { get; set; } = "/";
    public string Username { get; set; } = "";
    [JsonConverter(typeof(TolerantTransportConverter))]
    public RtspTransport Transport { get; set; } = RtspTransport.Auto;
    /// <summary>ONVIF service port used for PTZ; null tries the usual ones.</summary>
    public int? OnvifPort { get; set; }

    [JsonIgnore] public string NormalizedPath => Path.StartsWith('/') ? Path : "/" + Path;

    /// <summary><c>host:port</c>, for labels.</summary>
    [JsonIgnore] public string Endpoint => $"{Host}:{Port}";

    /// <summary>Stream address without credentials, safe to show on screen.</summary>
    [JsonIgnore] public string DisplayUrl => $"rtsp://{(Host.Length == 0 ? "…" : Host)}:{Port}{NormalizedPath}";

    /// <summary>Full stream address. Credentials are percent-encoded, so passwords with @, : or / work.</summary>
    public string RtspUrl(string password)
    {
        var auth = "";
        if (Username.Length > 0)
        {
            var user = Uri.EscapeDataString(Username);
            auth = password.Length == 0 ? $"{user}@" : $"{user}:{Uri.EscapeDataString(password)}@";
        }
        return $"rtsp://{auth}{Host}:{Port}{NormalizedPath}";
    }
}

public static class CameraJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
