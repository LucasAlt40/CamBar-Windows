using CamBar.Core;
using CamBar.Pinning;
using CamBar.Services;
using CamBar.Streaming;

namespace CamBar;

/// <summary>Composition root: the long-lived services, created once and handed to every view.</summary>
public sealed class AppEnvironment
{
    public required ISettings Settings { get; init; }
    public required CameraStore Store { get; init; }
    public required StreamPool Pool { get; init; }
    public required ReachabilityMonitor Reachability { get; init; }
    public required PinController Pins { get; init; }
    public required IReachabilityChecker Checker { get; init; }
    public required FileLog PtzLog { get; init; }

    /// <summary>Set by the app once the windows exist.</summary>
    public IWindowRouter? Router { get; set; }

    /// <summary>Raised by the popover's power button.</summary>
    public Action Quit { get; set; } = () => { };

    public static AppEnvironment Live()
    {
        ISettings settings = new JsonSettings();
        ICredentialStore credentials = new CredentialManagerStore();
        IReachabilityChecker checker = new TcpReachability();
        var store = new CameraStore(new JsonFileCameraRepository(), credentials);
        var pool = new StreamPool(store, credentials, settings, new FileLog("CamBar"));
        return new AppEnvironment
        {
            Settings = settings,
            Store = store,
            Pool = pool,
            Reachability = new ReachabilityMonitor(store, checker),
            Pins = new PinController(store, settings),
            Checker = checker,
            PtzLog = new FileLog("CamBar-ptz"),
        };
    }
}
