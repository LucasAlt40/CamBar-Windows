using System.Windows;
using CamBar.Core;
using CamBar.Pinning;
using CamBar.Services;
using CamBar.Views;

namespace CamBar;

/// <summary>Opens one window per destination (re-focusing it if already open).</summary>
public sealed class WindowRouter : IWindowRouter
{
    private readonly AppEnvironment _env;
    private readonly Dictionary<object, Window> _windows = [];

    private static readonly object Overview = new();
    private static readonly object Settings = new();

    public WindowRouter(AppEnvironment env)
    {
        _env = env;
        env.Store.Changed += change =>
        {
            switch (change)
            {
                case CameraChange.Removed removed when _windows.TryGetValue(removed.Id, out var window):
                    window.Close();   // its stream is gone
                    break;
            }
        };
    }

    public void ShowViewer(Camera camera) => Show(camera.Id, () => new ViewerWindow(_env, camera));
    public void ShowOverview() => Show(Overview, () => new OverviewWindow(_env));
    public void ShowSettings() => Show(Settings, () => new SettingsWindow(_env));

    private void Show(object key, Func<Window> create)
    {
        if (!_windows.TryGetValue(key, out var window))
        {
            window = create();
            _windows[key] = window;
            window.Closed += (_, _) => _windows.Remove(key);
            window.Show();
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
