using System.Windows;
using System.Windows.Threading;
using CamBar.Core;
using CamBar.Infrastructure;
using CamBar.Interop;
using CamBar.Services;
using CamBar.Streaming;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CamBar.Pinning;

/// <summary>Navigation between the app's windows.</summary>
public interface IWindowRouter
{
    void ShowViewer(Camera camera);
    void ShowOverview();
    void ShowSettings();
}

/// <summary>A monitor, described so the user can tell them apart.</summary>
public sealed record DisplayInfo(string Id, string Name, int PixelWidth, int PixelHeight, bool IsMain)
{
    public bool IsPortrait => PixelHeight > PixelWidth;
    public string Orientation => IsPortrait ? "Vertical" : "Horizontal";
    public string Title => $"{Name} · {PixelWidth}×{PixelHeight} · {Orientation}";
}

public sealed record PinState(Guid CameraId, PinCorner Corner, string ScreenId);

/// <summary>Keeps one camera floating in a screen corner, above other windows.</summary>
public sealed class PinController : ObservableObject
{
    private const string CameraKey = "pinnedCamera";
    private const string CornerKey = "pinnedCorner";
    private const string ScreenKey = "pinnedScreen";
    private const string WidthKey = "pinnedWidth";

    private readonly CameraStore _store;
    private readonly ISettings _settings;
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private PinWindow? _window;
    private PinState? _pin;
    private double? _customWidth;
    private IReadOnlyList<DisplayInfo> _displays = ConnectedDisplays();
    private (double Width, Native.POINT Mouse)? _resizeStart;

    /// <summary>Set once the router exists; lets the pinned view expand the camera into its window.</summary>
    public IWindowRouter? Router { get; set; }

    public PinController(CameraStore store, ISettings settings)
    {
        _store = store;
        _settings = settings;
        _customWidth = settings.GetDouble(WidthKey);

        store.Changed += change =>
        {
            if (change is CameraChange.Removed removed && _pin?.CameraId == removed.Id) Unpin();
            else if (change is CameraChange.Updated && _pin is not null) _window?.Apply(_pin, _store.Find(_pin.CameraId));
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => _ui.BeginInvoke(() =>
        {
            Displays = ConnectedDisplays();
            Reposition();
        });
    }

    public PinState? Current { get => _pin; private set => Set(ref _pin, value); }
    public IReadOnlyList<DisplayInfo> Displays { get => _displays; private set => Set(ref _displays, value); }

    /// <summary>Width chosen by resizing (device-independent pixels); null follows the screen size automatically.</summary>
    public double? CustomWidth { get => _customWidth; private set => Set(ref _customWidth, value); }

    public static string MainDisplayId => (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).DeviceName;

    /// <summary>Brings back the pin from the last run. Call after the router is set.</summary>
    public void Restore()
    {
        if (!Guid.TryParse(_settings.GetString(CameraKey), out var id) || _store.Find(id) is null) return;
        var corner = Enum.TryParse<PinCorner>(_settings.GetString(CornerKey), out var saved) ? saved : PinCorner.BottomTrailing;
        Show(new PinState(id, corner, _settings.GetString(ScreenKey) ?? MainDisplayId));
    }

    public bool IsPinned(Camera camera) => _pin?.CameraId == camera.Id;

    /// <summary>Pins to <paramref name="display"/> (default: where the camera is already pinned, else the main monitor).</summary>
    public void Pin(Camera camera, PinCorner corner, DisplayInfo? display = null)
    {
        var screenId = display?.Id ?? _pin?.ScreenId ?? MainDisplayId;
        Show(new PinState(camera.Id, corner, screenId));
        _settings.Set(CameraKey, camera.Id.ToString());
        _settings.Set(CornerKey, corner.ToString());
        _settings.Set(ScreenKey, screenId);
    }

    public void Unpin()
    {
        Current = null;
        _window?.Close();
        _window = null;
        _settings.Remove(CameraKey);
        _settings.Remove(CornerKey);
        _settings.Remove(ScreenKey);
    }

    public void OpenViewer()
    {
        if (_pin is { } pin && _store.Find(pin.CameraId) is { } camera) Router?.ShowViewer(camera);
    }

    // MARK: Resizing

    public void BeginResize()
    {
        Native.GetCursorPos(out var mouse);
        _resizeStart = (CurrentSize().Width, mouse);
    }

    /// <summary>Called on every drag event of the resize handle. Uses the pointer's screen position, because the window moves while it resizes.</summary>
    public void Resize()
    {
        if (_pin is not { } pin || _resizeStart is not { } start) return;
        Native.GetCursorPos(out var mouse);
        var scale = Geometry(pin).Scale;
        var change = PinLayout.WidthChange((mouse.X - start.Mouse.X) / scale, (mouse.Y - start.Mouse.Y) / scale, pin.Corner);
        CustomWidth = start.Width + change;
        Reposition();
    }

    public void EndResize()
    {
        _resizeStart = null;
        if (_pin is null) return;
        CustomWidth = CurrentSize().Width;   // the clamped size, so a later screen change starts from what is visible
        _settings.Set(WidthKey, CustomWidth!.Value);
    }

    /// <summary>Back to the automatic size (double-click on the handle).</summary>
    public void ResetSize()
    {
        CustomWidth = null;
        _settings.Remove(WidthKey);
        Reposition();
    }

    // MARK: Window

    /// <summary>The window is reused across pins: switching cameras just swaps the stream.</summary>
    private void Show(PinState pin)
    {
        Current = pin;
        _window ??= new PinWindow(this);
        _window.Apply(pin, _store.Find(pin.CameraId));
        Reposition();
        if (!_window.IsVisible) _window.Show();
        Reposition();   // the first call may have run before the window had a handle on the right monitor
    }

    public void Reposition()
    {
        if (_pin is not { } pin || _window is null) return;
        var geometry = Geometry(pin);
        var frame = PinLayout.Frame(pin.Corner, geometry.Visible, CustomWidth);
        _window.PlaceInPixels(
            (int)Math.Round(frame.X * geometry.Scale), (int)Math.Round(frame.Y * geometry.Scale),
            (int)Math.Round(frame.Width * geometry.Scale), (int)Math.Round(frame.Height * geometry.Scale));
    }

    private SizeD CurrentSize() => PinLayout.Size(Geometry(_pin!).Visible, CustomWidth);

    /// <summary>The target screen's work area (taskbar excluded) in device-independent pixels, plus its scale factor.</summary>
    private (RectD Visible, double Scale) Geometry(PinState pin)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == pin.ScreenId)
                     ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var bounds = screen.Bounds;
        var scale = Native.ScaleAt(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var work = screen.WorkingArea;
        return (new RectD(work.X / scale, work.Y / scale, work.Width / scale, work.Height / scale), scale);
    }

    private static IReadOnlyList<DisplayInfo> ConnectedDisplays()
    {
        var screens = Forms.Screen.AllScreens;
        return screens.Select((s, i) => new DisplayInfo(
            s.DeviceName,
            s.Primary ? $"Monitor {i + 1} (principal)" : $"Monitor {i + 1}",
            s.Bounds.Width, s.Bounds.Height, s.Primary)).ToList();
    }
}
