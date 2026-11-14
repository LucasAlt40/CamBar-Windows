using System.IO;
using System.Windows;
using CamBar.Core;
using CamBar.Views;

namespace CamBar;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayController? _tray;

    public AppEnvironment? Environment { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\CamBar.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();   // already running: the icon is in the tray
            return;
        }

        var crashLog = new FileLog("CamBar-app");
        DispatcherUnhandledException += (_, args) =>
        {
            crashLog.Append(args.Exception.ToString());
            args.Handled = true;   // a failed redraw should not take every camera down
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => crashLog.Append(args.ExceptionObject.ToString() ?? "erro desconhecido");

        var env = Environment = AppEnvironment.Live();
        env.Quit = Quit;
        var router = new WindowRouter(env);
        env.Router = router;
        env.Pins.Router = router;

        var menu = new MenuWindow(env);
        _tray = new TrayController(env, menu, router);
        env.Pins.Restore();
        env.Reachability.Start();
    }

    private void Quit()
    {
        Environment?.Pool.Shutdown();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Environment?.Pool.Shutdown();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
