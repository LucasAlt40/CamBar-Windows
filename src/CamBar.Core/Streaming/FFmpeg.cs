namespace CamBar.Core;

/// <summary>Locates the ffmpeg binary and builds its command lines.</summary>
public static class FFmpeg
{
    private const string Exe = "ffmpeg.exe";

    /// <summary>Where ffmpeg usually ends up on Windows: next to the app, winget, Chocolatey, Scoop, manual unzip, then PATH.</summary>
    public static IEnumerable<string> SearchPaths()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(AppContext.BaseDirectory, Exe);
        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg", Exe);
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links", Exe);
        yield return Path.Combine(programData, "chocolatey", "bin", Exe);
        yield return Path.Combine(user, "scoop", "shims", Exe);
        yield return Path.Combine(programFiles, "ffmpeg", "bin", Exe);
        yield return Path.Combine(@"C:\ffmpeg", "bin", Exe);

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim('"'), Exe);
    }

    public static string? Locate(Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        return SearchPaths().FirstOrDefault(exists);
    }

    /// <summary>RTSP in, MJPEG frames out on stdout. Hardware decoding when available, frames passed through as the camera sends them.</summary>
    public static List<string> VideoArguments(string url, RtspTransport transport, StreamProfile profile)
    {
        // -nostats: the progress line is rewritten every 0.5 s with "\r" and carries nothing CamBar uses
        List<string> args = ["-nostats", "-loglevel", "verbose", "-fflags", "nobuffer", "-flags", "low_delay", "-hwaccel", "auto"];
        if (profile.DecodesKeyframesOnly()) args.AddRange(["-skip_frame", "nokey"]);
        args.AddRange(transport.FfmpegArguments());
        args.AddRange(["-i", url, "-an",
                       "-vf", $"scale='min({profile.MaxWidth()},iw)':-2",
                       "-fps_mode", "passthrough",
                       "-q:v", profile.JpegQuality().ToString(),
                       "-f", "mjpeg", "pipe:1"]);
        return args;
    }

    /// <summary>Warnings ffmpeg repeats for almost every frame of some cameras; harmless, so kept out of the log file.</summary>
    public static bool IsNoise(string line) =>
        line.Contains("non monotonically increasing dts") || line.Contains("deblocking filter parameters");

    /// <summary>RTSP in, mono 16-bit little-endian PCM out on stdout.</summary>
    public static List<string> AudioArguments(string url, RtspTransport transport, int sampleRate)
    {
        List<string> args = ["-nostats", "-loglevel", "quiet", "-fflags", "nobuffer"];
        args.AddRange(transport.FfmpegArguments());
        args.AddRange(["-i", url, "-vn", "-f", "s16le", "-ar", sampleRate.ToString(), "-ac", "1", "pipe:1"]);
        return args;
    }
}
