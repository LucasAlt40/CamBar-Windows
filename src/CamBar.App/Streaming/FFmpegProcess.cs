using System.IO;
using System.Diagnostics;
using System.Text;
using CamBar.Interop;

namespace CamBar.Streaming;

/// <summary>One ffmpeg run. stdout and stderr are read on background threads and handed to the callbacks as they arrive.</summary>
public sealed class FFmpegProcess
{
    public sealed class Handlers
    {
        /// <summary>Called with a reused buffer: copy what you keep.</summary>
        public Action<byte[], int> Output { get; set; } = (_, _) => { };
        public Action<string> Log { get; set; } = _ => { };
        /// <summary>Called once, after the process ended and both pipes were drained.</summary>
        public Action Exit { get; set; } = () => { };
    }

    private readonly Process _process;

    public FFmpegProcess(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        _process = new Process { StartInfo = info };
    }

    /// <summary>Starts ffmpeg. Throws if the executable cannot be launched.</summary>
    public void Run(Handlers handlers)
    {
        _process.Start();
        Native.KillWithApp(_process);

        var output = Task.Run(() => PumpBytes(_process.StandardOutput.BaseStream, handlers.Output));
        var log = Task.Run(() => PumpText(_process.StandardError, handlers.Log));
        _ = Task.Run(async () =>
        {
            try
            {
                await _process.WaitForExitAsync();
                await Task.WhenAll(output, log);   // the last error lines arrive just before the exit
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException) { }
            finally
            {
                handlers.Exit();
                _process.Dispose();
            }
        });
    }

    public void Terminate()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    private static void PumpBytes(Stream stream, Action<byte[], int> output)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) output(buffer, read);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    private static void PumpText(TextReader reader, Action<string> log)
    {
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) log(new string(buffer, 0, read));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }
}
