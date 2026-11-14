using System.Globalization;
using System.Text.RegularExpressions;

namespace CamBar.Core;

/// <summary>What the camera reports about its stream, read from ffmpeg's log.</summary>
public readonly record struct StreamInfo(int Width = 0, int Height = 0, double Fps = 0, string Codec = "", bool HasAudio = false)
{
    /// <summary>The audio line can arrive before the video one; until then there is no picture size.</summary>
    public bool HasVideo => Width > 0 && Height > 0;

    public string Resolution => $"{Width}×{Height}";

    public string FpsText => Fps == Math.Round(Fps) ? ((int)Fps).ToString() : Fps.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>e.g. <c>1920×1080 • 15 FPS • H.265 Main</c></summary>
    public string Summary => $"{Resolution} • {FpsText} FPS" + (Codec.Length == 0 ? "" : $" • {Codec}");
}

public enum StreamFailureKind
{
    Unauthorized, PathNotFound, NotRtsp, BadRequest, Unreachable, FfmpegMissing, LaunchFailed, Unknown,
}

/// <summary>Why a stream could not play, phrased for the user.</summary>
public readonly record struct StreamFailure(StreamFailureKind Kind, string Reason = "")
{
    public static StreamFailure Unauthorized => new(StreamFailureKind.Unauthorized);
    public static StreamFailure PathNotFound => new(StreamFailureKind.PathNotFound);
    public static StreamFailure NotRtsp => new(StreamFailureKind.NotRtsp);
    public static StreamFailure BadRequest => new(StreamFailureKind.BadRequest);
    public static StreamFailure Unreachable => new(StreamFailureKind.Unreachable);
    public static StreamFailure FfmpegMissing => new(StreamFailureKind.FfmpegMissing);
    public static StreamFailure Unknown => new(StreamFailureKind.Unknown);
    public static StreamFailure LaunchFailed(string reason) => new(StreamFailureKind.LaunchFailed, reason);

    public string Title => Kind switch
    {
        StreamFailureKind.Unauthorized => "Usuário ou senha incorretos",
        StreamFailureKind.PathNotFound => "Caminho RTSP não encontrado",
        StreamFailureKind.NotRtsp => "A câmera não aceitou o RTSP",
        StreamFailureKind.BadRequest => "A câmera recusou o pedido",
        StreamFailureKind.Unreachable => "Câmera inacessível",
        StreamFailureKind.FfmpegMissing => "ffmpeg não encontrado",
        StreamFailureKind.LaunchFailed => "Não foi possível iniciar",
        _ => "Sem sinal",
    };

    public string Hint => Kind switch
    {
        StreamFailureKind.Unauthorized => "Confira as credenciais em Gerenciar câmeras.",
        StreamFailureKind.PathNotFound => "Verifique o caminho do stream, por exemplo /onvif1.",
        StreamFailureKind.NotRtsp => "A porta responde, mas não fala RTSP. Ative RTSP/ONVIF no app da câmera.",
        StreamFailureKind.BadRequest => "Costuma ser usuário ou caminho RTSP incorreto.",
        StreamFailureKind.Unreachable => "Confira o IP e se ela está ligada e na mesma rede.",
        StreamFailureKind.FfmpegMissing => "Instale com: winget install ffmpeg",
        StreamFailureKind.LaunchFailed => Reason,
        _ => @"Detalhes em %LOCALAPPDATA%\CamBar\Logs\CamBar.log",
    };

    /// <summary>Maps ffmpeg's most relevant error line to something the user can act on.</summary>
    public static StreamFailure Classify(string line)
    {
        var s = line.ToLowerInvariant();
        bool Has(params string[] needles) => needles.Any(s.Contains);

        if (Has("401", "403", "unauthorized", "forbidden")) return Unauthorized;
        if (Has("404", "not found")) return PathNotFound;
        if (Has("405", "not one of 40", "method not allowed")) return NotRtsp;
        if (Has("400 bad request")) return BadRequest;
        if (Has("refused", "timed out", "unreachable", "no route")) return Unreachable;
        return Unknown;
    }
}

/// <summary>
/// Reads ffmpeg's stderr incrementally: stream telemetry plus the latest error line.
/// Pipe reads can split a line in two, so an unfinished line waits for the next chunk.
/// </summary>
public sealed class FFmpegLogParser
{
    private static readonly char[] Newlines = ['\n', '\r', '\u0085', '\u2028', '\u2029'];
    private const int MaxPending = 4096;

    private string _pending = "";

    public StreamInfo? Info { get; private set; }
    public string LastError { get; private set; } = "";

    /// <summary>Consumes a chunk of log text. Returns true when <see cref="Info"/> changed.</summary>
    public bool Consume(string text)
    {
        // progress lines end in "\r" only; without splitting on it _pending would grow forever
        var lines = (_pending + text).Split(Newlines);
        var last = lines[^1];
        _pending = last.Length > MaxPending ? last[^MaxPending..] : last;   // empty when the chunk ended with a newline
        var changed = false;
        for (var i = 0; i < lines.Length - 1; i++) changed |= Handle(lines[i]);
        return changed;
    }

    /// <summary>Processes a trailing line that never got its newline (call when ffmpeg exits).</summary>
    public bool Finish()
    {
        var line = _pending;
        _pending = "";
        return line.Length > 0 && Handle(line);
    }

    private bool Handle(string line)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("RTSP/1.0 4"))
            LastError = line;
        if (!line.Contains("Stream #0:")) return false;

        var updated = Info ?? new StreamInfo();
        if (line.Contains("Video:") && !line.Contains("mjpeg") && ParseVideo(line) is { } video)
            updated = updated with { Width = video.Width, Height = video.Height, Fps = video.Fps, Codec = video.Codec };
        else if (line.Contains("Audio:"))
            updated = updated with { HasAudio = true };
        else
            return false;

        if (Info is { } current && current == updated) return false;
        Info = updated;
        return true;
    }

    // MARK: Parsing

    private static readonly Regex SizeRegex = new(@"\b(\d{3,5})x(\d{3,5})\b");
    private static readonly Regex FpsRegex = new(@"(\d+(?:\.\d+)?) fps");
    private static readonly Regex CodecRegex = new(@"Video: ([a-z0-9]+)(?: \(([^)]*)\))?");

    /// <summary>Parses ffmpeg's input video line, e.g. <c>Stream #0:0: Video: h264 (Main), …, 2304x1296, 15 fps, …</c>.</summary>
    public static (int Width, int Height, double Fps, string Codec)? ParseVideo(string line)
    {
        var size = SizeRegex.Match(line);
        var rate = FpsRegex.Match(line);
        if (!size.Success || !rate.Success) return null;
        var fps = double.Parse(rate.Groups[1].Value, CultureInfo.InvariantCulture);

        var codec = "";
        var c = CodecRegex.Match(line);
        if (c.Success)
        {
            var raw = c.Groups[1].Value;
            var name = raw switch { "h264" => "H.264", "hevc" => "H.265", _ => raw.ToUpperInvariant() };
            codec = c.Groups[2].Value.Length == 0 ? name : $"{name} {c.Groups[2].Value}";
        }
        return (int.Parse(size.Groups[1].Value), int.Parse(size.Groups[2].Value), fps, codec);
    }
}
