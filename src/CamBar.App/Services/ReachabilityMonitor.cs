using System.Windows.Threading;
using CamBar.Core;
using CamBar.Infrastructure;

namespace CamBar.Services;

/// <summary>Which cameras answer on the network, refreshed periodically and whenever the list changes.</summary>
public sealed class ReachabilityMonitor : ObservableObject
{
    private readonly CameraStore _store;
    private readonly IReachabilityChecker _checker;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _refresh;
    private HashSet<Guid> _online = [];

    public ReachabilityMonitor(CameraStore store, IReachabilityChecker checker, TimeSpan? interval = null)
    {
        _store = store;
        _checker = checker;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Refresh();
        store.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CameraStore.Cameras)) Refresh(); };
    }

    public IReadOnlySet<Guid> Online => _online;
    public int OnlineCount => _online.Count;
    public bool IsOnline(Guid id) => _online.Contains(id);

    public void Start()
    {
        _timer.Start();
        Refresh();
    }

    public async void Refresh()
    {
        _refresh?.Cancel();
        var source = _refresh = new CancellationTokenSource();
        var targets = _store.Cameras.ToList();
        try
        {
            var results = await Task.WhenAll(targets.Select(async camera =>
                (camera.Id, Up: await _checker.IsReachableAsync(camera.Host, camera.Port, TimeSpan.FromSeconds(2)))));
            if (source.IsCancellationRequested) return;
            var next = results.Where(r => r.Up).Select(r => r.Id).ToHashSet();
            if (next.SetEquals(_online)) return;
            _online = next;
            Raise(nameof(Online));
            Raise(nameof(OnlineCount));
        }
        catch (OperationCanceledException) { }
    }
}
