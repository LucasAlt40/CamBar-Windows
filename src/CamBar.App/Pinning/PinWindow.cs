using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using CamBar.Controls;
using CamBar.Core;
using CamBar.Interop;
using CamBar.Theme;

namespace CamBar.Pinning;

/// <summary>The picture-in-picture: live video with controls that appear on hover, resized by the handle in the inner corner.</summary>
public sealed class PinWindow : Window
{
    private readonly PinController _controller;
    private readonly CameraFeed _feed = new() { IsActive = true };
    private readonly Grid _bar = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly TextBlock _name = UI.Text("", 11, FontWeights.SemiBold);
    private readonly StackPanel _corners = new() { Orientation = Orientation.Horizontal };
    private readonly Button _displayButton;
    private readonly Border _handle = new() { Width = 26, Height = 26, Background = Brushes.Transparent, Opacity = 0.45 };
    private readonly Path _grip;
    private readonly ScaleTransform _gripFlip = new(1, 1);
    private PinState? _pin;
    private Camera? _camera;

    public PinWindow(PinController controller)
    {
        _controller = controller;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        Title = "CamBar";
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontFamily = Palette.UiFont;
        Width = 320;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(_feed);

        // top bar: name and controls, visible on hover
        var gradient = new Border
        {
            Height = 52,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(Color.FromArgb(0xC0, 0, 0, 0), Colors.Transparent, 90),
            IsHitTestVisible = false,
        };
        _bar.Children.Add(gradient);
        var row = new DockPanel { Margin = new Thickness(10, 8, 10, 0), VerticalAlignment = VerticalAlignment.Top, LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);

        buttons.Children.Add(_corners);
        _displayButton = UI.OverlayButton(UI.Glyph.Monitor, "Escolher monitor", () => { if (_camera is { } c) PinMenu.Show(_displayButton!, c, _controller); }, 22);
        _displayButton.Margin = new Thickness(4, 0, 0, 0);
        buttons.Children.Add(_displayButton);
        var expand = UI.OverlayButton(UI.Glyph.Expand, "Abrir em janela", _controller.OpenViewer, 22);
        expand.Margin = new Thickness(4, 0, 0, 0);
        buttons.Children.Add(expand);
        var unpin = UI.OverlayButton(UI.Glyph.Unpin, "Desafixar", _controller.Unpin, 22);
        unpin.Margin = new Thickness(4, 0, 0, 0);
        buttons.Children.Add(unpin);

        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(buttons);
        row.Children.Add(_name);
        _bar.Children.Add(row);
        root.Children.Add(_bar);

        // resize handle in the corner opposite to the pinned one
        _grip = new Path
        {
            Data = Geometry.Parse("M 17,8 L 8,17 M 17,12 L 12,17"),
            Stroke = Palette.White(0.85),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = _gripFlip,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Width = 26,
            Height = 26,
        };
        _handle.Child = _grip;
        _handle.ToolTip = "Arraste para redimensionar; clique duas vezes para o tamanho automático";
        _handle.MouseLeftButtonDown += OnHandleDown;
        _handle.MouseMove += (_, _) => { if (_handle.IsMouseCaptured) _controller.Resize(); };
        _handle.MouseLeftButtonUp += OnHandleUp;
        root.Children.Add(_handle);

        // a click on the picture (not on a button) opens the camera in its own window
        root.MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled) _controller.OpenViewer();
        };
        root.MouseEnter += (_, _) => SetHover(true);
        root.MouseLeave += (_, _) => SetHover(false);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            WindowEffects.NoActivateToolWindow(this);
            WindowEffects.RoundedCorners(this);
        };
        // moving between monitors with different scaling makes WPF resize the window: put it back
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(_controller.Reposition, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void PlaceInPixels(int x, int y, int width, int height) => WindowEffects.PlaceTopmost(this, x, y, width, height);

    public void Apply(PinState pin, Camera? camera)
    {
        _pin = pin;
        _camera = camera;
        _feed.Camera = camera;
        _name.Text = camera?.Name ?? "";

        _corners.Children.Clear();
        foreach (var corner in Enum.GetValues<PinCorner>())
        {
            var selected = corner == pin.Corner;
            var button = UI.IconButton(corner.Glyph(), corner.Label(), () => { if (_camera is { } c) _controller.Pin(c, corner); }, 22, 10,
                                       selected ? Palette.Accent : Brushes.White,
                                       selected ? Palette.With(Palette.Accent, 0.25) : Palette.Black(0.55),
                                       selected ? Palette.With(Palette.Accent, 0.6) : Palette.White(0.12));
            button.Margin = new Thickness(corner == PinCorner.TopLeading ? 0 : 4, 0, 0, 0);
            _corners.Children.Add(button);
        }
        _displayButton.Visibility = _controller.Displays.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        var opposite = pin.Corner.Opposite();
        _handle.HorizontalAlignment = opposite.IsLeading() ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _handle.VerticalAlignment = opposite.IsTop() ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        // the grip is drawn for the bottom-right corner; mirror it for the others
        _gripFlip.ScaleX = opposite.IsLeading() ? -1 : 1;
        _gripFlip.ScaleY = opposite.IsTop() ? -1 : 1;
        _handle.Cursor = opposite is PinCorner.TopLeading or PinCorner.BottomTrailing ? Cursors.SizeNWSE : Cursors.SizeNESW;
    }

    private void SetHover(bool hovering)
    {
        _bar.IsHitTestVisible = hovering;
        _bar.BeginAnimation(OpacityProperty, new DoubleAnimation(hovering ? 1 : 0, TimeSpan.FromMilliseconds(150)));
        _handle.BeginAnimation(OpacityProperty, new DoubleAnimation(hovering ? 1 : 0.45, TimeSpan.FromMilliseconds(150)));
    }

    private void OnHandleDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _controller.ResetSize();
            return;
        }
        _handle.CaptureMouse();
        _controller.BeginResize();
    }

    private void OnHandleUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_handle.IsMouseCaptured) return;
        _handle.ReleaseMouseCapture();
        _controller.EndResize();
    }
}

/// <summary>"Fixar na tela" menu. With several monitors each one gets its own section.</summary>
public static class PinMenu
{
    public static void Show(FrameworkElement anchor, Camera camera, PinController pins)
    {
        var entries = new List<MenuEntry>();
        var current = pins.Current is { } pin && pin.CameraId == camera.Id ? pin : null;

        if (pins.Displays.Count > 1)
        {
            foreach (var display in pins.Displays)
            {
                entries.Add(new MenuEntry(display.Title, IsHeader: true));
                entries.AddRange(Corners(camera, pins, display, current));
            }
        }
        else
        {
            entries.AddRange(Corners(camera, pins, pins.Displays.FirstOrDefault(), current));
        }

        if (current is not null) entries.Add(new MenuEntry("Desafixar", pins.Unpin, IsDanger: true, Glyph: UI.Glyph.Unpin));
        FlyoutMenu.Show(anchor, entries);
    }

    private static IEnumerable<MenuEntry> Corners(Camera camera, PinController pins, DisplayInfo? display, PinState? current) =>
        Enum.GetValues<PinCorner>().Select(corner => new MenuEntry(
            corner.Label(),
            () => pins.Pin(camera, corner, display),
            IsChecked: current is not null && current.Corner == corner && (display is null || current.ScreenId == display.Id),
            Glyph: corner.Glyph()));

    /// <summary>The pin button used in the popover, viewer and overview.</summary>
    public static Button Button(Camera camera, PinController pins, double size = 24, bool overlay = true)
    {
        Button? button = null;
        button = overlay
            ? UI.OverlayButton(UI.Glyph.PinFilled, "Fixar na tela", () => Show(button!, camera, pins), size)
            : UI.IconButton(UI.Glyph.PinFilled, "Fixar na tela", () => Show(button!, camera, pins), size, 10, Palette.White(0.7));
        return button;
    }
}
