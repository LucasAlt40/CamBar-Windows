using System.Windows.Threading;
using CamBar.Core;
using CamBar.Infrastructure;
using CamBar.Services;
using Microsoft.Win32;

namespace CamBar.Streaming;

/// <summary>
/// Shares one stream per camera between every view that shows it (lease pattern) and decides its profile:
/// HD while an expanded window wants it, normal while visible, standby (keyframes only) when nobody looks.
/// </summary>
public sealed class StreamPool : ObservableObject
{
    private const string KeepConnectedKey = "keepConnected";

    private sealed class Entry
    {
        public required CameraStream Stream { get; init; }
        public required Camera Camera { get; set; }
        public int Leases { get; set; }
        public int HdLeases { get; set; }
        public CancellationTokenSource? IdleStop { get; set; }
    }

    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly ICredentialStore _credentials;
    private readonly ISettings _settings;
    private readonly FileLog? _log;
    private readonly TimeSpan _idleGrace;
    private bool _keepConnected;
    private bool _panelOpen;
    private bool _displaysAsleep;

    public StreamPool(CameraStore store, ICredentialStore credentials, ISettings settings, FileLog? log, TimeSpan? idleGrace = null)
    {
        _credentials = credentials;
        _settings = settings;
        _log = log;
        _idleGrace = idleGrace ?? TimeSpan.FromSeconds(60);
        _keepConnected = settings.GetBool(KeepConnectedKey) ?? true;
        store.Changed += Apply;

        // Nobody can see a frame while the session is locked or the PC sleeps; Windows raises these on other threads.
        var ui = System.Windows.Application.Current.Dispatcher;
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
                ui.BeginInvoke(() => DisplaysAsleep = true);
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
                ui.BeginInvoke(() => DisplaysAsleep = false);
        };
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Suspend) ui.BeginInvoke(() => DisplaysAsleep = true);
            else if (e.Mode == PowerModes.Resume) ui.BeginInvoke(() =>
            {
                DisplaysAsleep = false;
                ReconnectAll();   // the network was down while asleep
            });
        };
    }

    /// <summary>Keep streams connected after the popover closes. Off: they stop after a grace period once the last viewer leaves.</summary>
    public bool KeepConnected
    {
        get => _keepConnected;
        set
        {
            if (!Set(ref _keepConnected, value)) return;
            _settings.Set(KeepConnectedKey, value);
            ReevaluateAll();
        }
    }

    /// <summary>While the popover is open every connected camera runs at normal quality, so tab switches are instant.</summary>
    public bool PanelOpen
    {
        get => _panelOpen;
        set { if (Set(ref _panelOpen, value)) ReevaluateAll(); }
    }

    private bool DisplaysAsleep
    {
        get => _displaysAsleep;
        set { if (Set(ref _displaysAsleep, value, nameof(DisplaysAsleep))) ReevaluateAll(); }
    }

    // MARK: Leases

    public StreamLease Lease(Camera camera, bool hd = false)
    {
        var entry = _entries.TryGetValue(camera.Id, out var existing) ? existing : MakeEntry(camera);
        entry.Leases++;
        if (hd) entry.HdLeases++;
        Reevaluate(entry);
        return new StreamLease(entry.Stream, camera.Id, hd, this);
    }

    internal void Release(StreamLease lease)
    {
        if (!_entries.TryGetValue(lease.CameraId, out var entry) || entry.Leases <= 0) return;
        entry.Leases--;
        if (lease.Hd) entry.HdLeases = Math.Max(0, entry.HdLeases - 1);
        Reevaluate(entry);
    }

    internal void HdChanged(StreamLease lease, bool hd)
    {
        if (!_entries.TryGetValue(lease.CameraId, out var entry)) return;
        entry.HdLeases = Math.Max(0, entry.HdLeases + (hd ? 1 : -1));
        Reevaluate(entry);
    }

    // MARK: Commands

    public void Reconnect(Guid id)
    {
        if (_entries.TryGetValue(id, out var entry)) Start(entry);
    }

    public void ReconnectAll()
    {
        foreach (var entry in _entries.Values.Where(e => e.Leases > 0 || e.Stream.IsRunning).ToList()) Start(entry);
    }

    /// <summary>Stops every ffmpeg process (app quitting).</summary>
    public void Shutdown()
    {
        foreach (var entry in _entries.Values)
        {
            entry.IdleStop?.Cancel();
            entry.Stream.Stop();
        }
    }

    // MARK: Policy

    private Entry MakeEntry(Camera camera)
    {
        var entry = new Entry { Camera = camera, Stream = new CameraStream(camera, _log) };
        _entries[camera.Id] = entry;
        return entry;
    }

    private void Apply(CameraChange change)
    {
        switch (change)
        {
            case CameraChange.Updated(var camera):
                if (!_entries.TryGetValue(camera.Id, out var entry)) return;
                entry.Camera = camera;
                // a watched stream that gave up (e.g. launch failed) gets another chance with the new settings
                if (entry.Stream.IsRunning || entry.Leases > 0) Start(entry);
                break;
            case CameraChange.Removed(var id):
                if (!_entries.Remove(id, out var removed)) return;
                removed.IdleStop?.Cancel();
                removed.Stream.Stop();
                break;
        }
    }

    private StreamProfile ProfileFor(Entry entry)
    {
        if (_displaysAsleep) return StreamProfile.Standby;
        if (entry.HdLeases > 0) return StreamProfile.Hd;
        if (_panelOpen || entry.Leases > 0 || !_keepConnected) return StreamProfile.Normal;
        return StreamProfile.Standby;
    }

    private void Reevaluate(Entry entry)
    {
        var stream = entry.Stream;
        stream.IsWatched = entry.Leases > 0;

        if (entry.Leases > 0 || _keepConnected)
        {
            entry.IdleStop?.Cancel();
            entry.IdleStop = null;
        }

        if (entry.Leases > 0 && !stream.IsRunning) Start(entry);
        else if (stream.IsRunning && stream.Profile != ProfileFor(entry)) Start(entry, keepFrame: true);

        if (entry.Leases == 0 && !_keepConnected && stream.IsRunning && entry.IdleStop is null) ScheduleIdleStop(entry);
    }

    private void ReevaluateAll()
    {
        foreach (var entry in _entries.Values.ToList()) Reevaluate(entry);
    }

    private void Start(Entry entry, bool keepFrame = false) =>
        entry.Stream.Start(entry.Camera, _credentials.GetPassword(entry.Camera.Id), ProfileFor(entry), keepFrame);

    private async void ScheduleIdleStop(Entry entry)
    {
        var source = entry.IdleStop = new CancellationTokenSource();
        try { await Task.Delay(_idleGrace, source.Token); }
        catch (OperationCanceledException) { return; }
        if (source.IsCancellationRequested || entry.Leases != 0 || _keepConnected) return;
        entry.Stream.Stop();
        entry.IdleStop = null;
    }
}

/// <summary>A view's claim on a camera stream. Release it when the view goes away.</summary>
public sealed class StreamLease
{
    private readonly StreamPool _pool;

    internal StreamLease(CameraStream stream, Guid cameraId, bool hd, StreamPool pool)
    {
        Stream = stream;
        CameraId = cameraId;
        Hd = hd;
        _pool = pool;
    }

    public CameraStream Stream { get; }
    public Guid CameraId { get; }
    public bool Hd { get; private set; }
    public bool IsReleased { get; private set; }

    public void SetHd(bool value)
    {
        if (IsReleased || value == Hd) return;
        Hd = value;
        _pool.HdChanged(this, value);
    }

    public void Release()
    {
        if (IsReleased) return;
        IsReleased = true;
        _pool.Release(this);
    }
}

/// <summary>Holds one view's stream lease and tells the view when it changes.</summary>
public sealed class StreamSession
{
    private StreamLease? _lease;

    public event Action? Changed;

    public CameraStream? Stream => _lease?.Stream;
    public Guid? CameraId => _lease?.CameraId;

    public void Attach(Camera camera, bool hd, StreamPool pool)
    {
        if (_lease is { } current && current.CameraId == camera.Id) return;
        Detach(notify: false);
        _lease = pool.Lease(camera, hd);
        Changed?.Invoke();
    }

    public void Detach(bool notify = true)
    {
        if (_lease is null) return;
        _lease.Release();
        _lease = null;
        if (notify) Changed?.Invoke();
    }

    public void SetHd(bool hd) => _lease?.SetHd(hd);
}
