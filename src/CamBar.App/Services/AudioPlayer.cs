using CamBar.Core;
using CamBar.Infrastructure;
using CamBar.Streaming;
using NAudio.Wave;

namespace CamBar.Services;

/// <summary>Plays a camera's audio track. A second ffmpeg decodes it to PCM, played through the default output device.</summary>
public sealed class AudioPlayer : ObservableObject
{
    private const int SampleRate = 16_000;

    private WaveOutEvent? _output;
    private FFmpegProcess? _process;
    private bool _isPlaying;

    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }

    public void Start(Camera camera, string password)
    {
        if (IsPlaying || FFmpeg.Locate() is not { } ffmpeg) return;

        var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        var buffer = new BufferedWaveProvider(format) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(2) };
        var converter = new PcmConverter();   // used only from ffmpeg's reader thread
        var run = new FFmpegProcess(ffmpeg, FFmpeg.AudioArguments(camera.RtspUrl(password), camera.Transport, SampleRate));
        var ui = System.Windows.Application.Current.Dispatcher;

        try
        {
            var output = _output = new WaveOutEvent { DesiredLatency = 150 };
            output.Init(buffer);
            run.Run(new FFmpegProcess.Handlers
            {
                Output = (data, count) =>
                {
                    var samples = converter.Convert(data.AsSpan(0, count));
                    if (samples.Length == 0) return;
                    var bytes = new byte[samples.Length * sizeof(float)];
                    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                    buffer.AddSamples(bytes, 0, bytes.Length);
                },
                Exit = () => ui.BeginInvoke(() =>
                {
                    if (ReferenceEquals(_process, run)) Stop();
                }),
            });
            output.Play();
            _process = run;
            IsPlaying = true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NAudio.MmException)
        {
            run.Terminate();
            _output?.Dispose();
            _output = null;
        }
    }

    public void Stop()
    {
        var process = _process;
        _process = null;
        process?.Terminate();
        if (_output is { } output)
        {
            _output = null;
            try { output.Stop(); } catch (InvalidOperationException) { }
            output.Dispose();
        }
        IsPlaying = false;
    }
}
