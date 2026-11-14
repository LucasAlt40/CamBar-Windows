using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CamBar.Core;
using CamBar.Infrastructure;

namespace CamBar.Streaming;

public enum StreamStatus { Idle, Connecting, Live, Failed }

/// <summary>
/// Live picture of one camera: runs ffmpeg, publishes decoded frames, telemetry and status,
/// and retries with backoff while somebody is watching. Everything here lives on the UI thread.
/// </summary>
public sealed class CameraStream : ObservableObject
{
    private readonly Dispatcher _ui = System.Windows.Application.Current.Dispatcher;
    private readonly FileLog? _log;
    private Camera _camera;
    private string _password = "";
    private FFmpegProcess? _process;
    private FFmpegLogParser _logParser = new();
    private CancellationTokenSource? _retry;
    private int _failedAttempts;
    private StreamStatus _status = StreamStatus.Idle;
    private StreamFailure? _failure;
    private BitmapSource? _frame;
    private StreamInfo? _info;

    public CameraStream(Camera camera, FileLog? log)
    {
        _camera = camera;
        _log = log;
    }

    public StreamStatus Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>Why the stream is not playing, while <see cref="Status"/> is <see cref="StreamStatus.Failed"/>.</summary>
    public StreamFailure? Failure { get => _failure; private set => Set(ref _failure, value); }

    public BitmapSource? Frame { get => _frame; private set => Set(ref _frame, value); }
    public StreamInfo? Info { get => _info; private set => Set(ref _info, value); }

    public StreamProfile Profile { get; private set; } = StreamProfile.Normal;
    public bool IsRunning { get; private set; }

    /// <summary>A failing stream nobody is watching stops retrying until a view asks for it again.</summary>
    public bool IsWatched { get; set; } = true;

    /// <summary>(Re)starts the stream. <paramref name="keepFrame"/> leaves the last picture on screen while reconnecting.</summary>
    public void Start(Camera camera, string password, StreamProfile profile, bool keepFrame = false)
    {
        Stop(clearing: !keepFrame);
        _camera = camera;
        _password = password;
        Profile = profile;
        IsRunning = true;
        _failedAttempts = 0;
        Launch();
    }

    public void Stop(bool clearing = true)
    {
        IsRunning = false;
        _retry?.Cancel();
        _retry = null;
        _process?.Terminate();
        _process = null;
        if (!clearing) return;
        Status = StreamStatus.Idle;
        Failure = null;
        Frame = null;
        Info = null;
    }

    // MARK: Process lifecycle

    private void Launch()
    {
        if (FFmpeg.Locate() is not { } ffmpeg)
        {
            IsRunning = false;
            Failure = StreamFailure.FfmpegMissing;
            Status = StreamStatus.Failed;
            return;
        }
        Failure = null;
        Status = StreamStatus.Connecting;
        _logParser = new FFmpegLogParser();

        var run = new FFmpegProcess(ffmpeg, FFmpeg.VideoArguments(_camera.RtspUrl(_password), _camera.Transport, Profile));
        var frames = new FrameExtractor(_ui, image => Receive(image, run));
        var log = _log;
        var tag = _camera.Name;

        try
        {
            // the handlers run on reader threads; everything that touches state hops to the UI thread
            run.Run(new FFmpegProcess.Handlers
            {
                Output = frames.Feed,
                Log = text =>
                {
                    var kept = text.Split('\n').Where(l => l.Length > 0 && !FFmpeg.IsNoise(l)).ToList();
                    if (kept.Count > 0) log?.Append(string.Join('\n', kept), tag);
                    _ui.BeginInvoke(() => ReceiveLog(text, run));
                },
                Exit = () => _ui.BeginInvoke(() => ProcessExited(run)),
            });
            _process = run;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            IsRunning = false;
            Failure = StreamFailure.LaunchFailed(ex.Message);
            Status = StreamStatus.Failed;
        }
    }

    private void Receive(BitmapSource image, FFmpegProcess run)
    {
        if (!ReferenceEquals(run, _process)) return;
        Frame = image;
        if (Status != StreamStatus.Live)
        {
            Failure = null;
            Status = StreamStatus.Live;
        }
        _failedAttempts = 0;
    }

    private void ReceiveLog(string text, FFmpegProcess run)
    {
        if (!ReferenceEquals(run, _process)) return;
        if (_logParser.Consume(text)) Info = _logParser.Info;
    }

    private void ProcessExited(FFmpegProcess run)
    {
        if (!ReferenceEquals(run, _process) || !IsRunning) return;
        _process = null;
        if (_logParser.Finish()) Info = _logParser.Info;
        Failure = StreamFailure.Classify(_logParser.LastError);
        Status = StreamStatus.Failed;
        ScheduleRetry();
    }

    /// <summary>Exponential backoff: 2, 4, 8, 16, then every 30 seconds.</summary>
    private async void ScheduleRetry()
    {
        if (!IsWatched)
        {
            IsRunning = false;
            return;
        }
        _failedAttempts++;
        var delay = TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(_failedAttempts, 5)));
        var source = _retry = new CancellationTokenSource();
        try { await Task.Delay(delay, source.Token); }
        catch (OperationCanceledException) { return; }
        if (source.IsCancellationRequested || !IsRunning) return;
        if (IsWatched) Launch(); else IsRunning = false;
    }

    /// <summary>
    /// Decodes MJPEG on ffmpeg's reader thread and hands only the newest complete frame to the UI thread,
    /// so a slow draw never builds a queue of stale pictures.
    /// </summary>
    private sealed class FrameExtractor
    {
        private readonly MjpegParser _parser = new();
        private readonly Dispatcher _ui;
        private readonly Action<BitmapSource> _deliver;
        private BitmapSource? _pending;
        private int _scheduled;

        public FrameExtractor(Dispatcher ui, Action<BitmapSource> deliver)
        {
            _ui = ui;
            _deliver = deliver;
        }

        public void Feed(byte[] buffer, int count)
        {
            var frames = _parser.Append(buffer.AsSpan(0, count));
            if (frames.Count == 0 || Decode(frames[^1]) is not { } image) return;

            Interlocked.Exchange(ref _pending, image);
            if (Interlocked.Exchange(ref _scheduled, 1) != 0) return;
            _ui.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _scheduled, 0);
                if (Interlocked.Exchange(ref _pending, null) is { } latest) _deliver(latest);
            }, DispatcherPriority.Render);
        }

        private static BitmapSource? Decode(byte[] jpeg)
        {
            try
            {
                using var stream = new MemoryStream(jpeg, writable: false);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();   // frozen images can cross threads
                return frame;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
            {
                return null;   // a truncated frame; the next one will do
            }
        }
    }
}
