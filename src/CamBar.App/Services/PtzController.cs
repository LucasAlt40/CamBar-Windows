using CamBar.Core;
using CamBar.Infrastructure;

namespace CamBar.Services;

public enum PtzState { Idle, Detecting, Available, Unavailable }

/// <summary>Pan/tilt/zoom for one camera over ONVIF. Commands are serialized so a stop never overtakes its move.</summary>
public sealed class PtzController : ObservableObject
{
    private readonly FileLog? _log;
    private OnvifClient? _client;
    private PtzEndpoint? _endpoint;
    private Task _queue = Task.CompletedTask;
    private PtzState _state = PtzState.Idle;
    private string? _activeMove;

    public PtzController(FileLog? log) => _log = log;

    public PtzState State { get => _state; private set => Set(ref _state, value); }

    /// <summary>The move currently in progress (used to highlight the pressed control).</summary>
    public string? ActiveMove { get => _activeMove; private set => Set(ref _activeMove, value); }

    public async void Detect(Camera camera, string password)
    {
        if (State != PtzState.Idle) return;
        State = PtzState.Detecting;
        _log?.Clear();
        var client = _client = new OnvifClient(camera.Username, password, _log);
        IEnumerable<int> ports = camera.OnvifPort is { } port ? [port] : OnvifClient.DefaultPorts;
        _endpoint = await client.DiscoverPtzAsync(camera.Host, ports);
        State = _endpoint is null ? PtzState.Unavailable : PtzState.Available;
    }

    /// <summary>Starts a continuous move that lasts until <see cref="Stop"/>. Repeating the same move is ignored.</summary>
    public void Move(string name, double pan, double tilt, double zoom)
    {
        if (State != PtzState.Available || ActiveMove == name) return;
        ActiveMove = name;
        Enqueue((client, endpoint) => client.ContinuousMoveAsync(endpoint, pan, tilt, zoom));
    }

    /// <summary>
    /// Joystick input in -1..1 per axis (y up). Quantized so the camera only gets a new command
    /// when the direction or speed noticeably changes.
    /// </summary>
    public void Steer(double dx, double dy)
    {
        static double Quantize(double v) => Math.Abs(v) < 0.25 ? 0 : Math.Round(v * 4) / 4;
        double pan = Quantize(dx), tilt = Quantize(dy);
        if (pan == 0 && tilt == 0) Stop();
        else Move($"joystick {pan} {tilt}", pan, tilt, 0);
    }

    public void Stop()
    {
        if (ActiveMove is null) return;
        ActiveMove = null;
        Enqueue((client, endpoint) => client.StopAsync(endpoint));
    }

    public void GoHome()
    {
        if (State != PtzState.Available) return;
        Enqueue((client, endpoint) => client.GoHomeAsync(endpoint));
    }

    private void Enqueue(Func<OnvifClient, PtzEndpoint, Task> command)
    {
        if (_client is not { } client || _endpoint is not { } endpoint) return;
        var previous = _queue;
        _queue = Task.Run(async () =>
        {
            try { await previous; } catch { /* a failed command must not block the next */ }
            await command(client, endpoint);
        });
    }
}
