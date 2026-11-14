using System.Text;
using System.Text.RegularExpressions;

namespace CamBar.Core;

/// <summary>
/// Append-only log in %LOCALAPPDATA%\CamBar\Logs. Masks RTSP credentials, tags lines and stays under a size cap,
/// so several cameras can share one file without wiping each other's history.
/// </summary>
public sealed class FileLog
{
    private readonly object _gate = new();
    private readonly int _maxBytes;

    public string Path { get; }

    public FileLog(string name, int maxBytes = 1_000_000, string? directory = null)
    {
        var dir = directory ?? System.IO.Path.Combine(AppPaths.DataDirectory, "Logs");
        Path = System.IO.Path.Combine(dir, $"{name}.log");
        _maxBytes = maxBytes;
    }

    public void Append(string text, string? tag = null)
    {
        var prefix = tag is null ? "" : $"[{tag}] ";
        var lines = Mask(text).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => prefix + l).ToList();
        if (lines.Count == 0) return;
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        lock (_gate)
        {
            try { Write(bytes); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try { if (File.Exists(Path)) File.WriteAllBytes(Path, []); } catch (IOException) { }
        }
    }

    /// <summary>Replaces <c>user:password@</c> in any URL with <c>***@</c>.</summary>
    public static string Mask(string text) => Regex.Replace(text, @"://[^/@\s]*@", "://***@");

    private void Write(byte[] data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var stream = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var size = stream.Length;
        if (size + data.Length > _maxBytes)
        {
            // keep the newest half so the file never grows without bound
            var tail = new byte[size - size / 2];
            stream.Seek(size / 2, SeekOrigin.Begin);
            stream.ReadExactly(tail);
            stream.SetLength(0);
            stream.Write(tail);
        }
        stream.Seek(0, SeekOrigin.End);
        stream.Write(data);
    }
}

/// <summary>Where CamBar keeps its data on disk.</summary>
public static class AppPaths
{
    public static string DataDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CamBar");
}
