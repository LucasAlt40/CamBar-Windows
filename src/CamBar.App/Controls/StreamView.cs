using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CamBar.Core;
using CamBar.Streaming;
using CamBar.Theme;

namespace CamBar.Controls;

/// <summary>Small rotating arc shown while connecting.</summary>
public sealed class Spinner : Grid
{
    public Spinner(double size = 18, Brush? brush = null)
    {
        Width = Height = size;
        var rotation = new RotateTransform(0, size / 2, size / 2);
        Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Stroke = brush ?? Palette.White(0.7),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 8, 20 },
            StrokeDashCap = PenLineCap.Round,
            RenderTransform = rotation,
        });
        Loaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        Unloaded += (_, _) => rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}

/// <summary>Why the stream is not playing, with an optional retry.</summary>
public static class FailureView
{
    public static UIElement Create(StreamFailure failure, bool alert, Action? retry)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 28, 0) };

        var icon = UI.Card(UI.Icon(UI.Glyph.Warning, 15, alert ? Palette.DangerSoft : Palette.White(0.7)),
                           alert ? Palette.With(Palette.Danger, 0.12) : Palette.White(0.06), UI.Round,
                           alert ? Palette.With(Palette.Danger, 0.3) : Palette.White(0.1));
        icon.Width = icon.Height = 38;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(icon);

        var title = UI.Text(failure.Title, 12, FontWeights.SemiBold, alert ? Palette.DangerText : Brushes.White);
        title.Margin = new Thickness(0, 8, 0, 0);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(title);

        var hint = UI.Text(failure.Hint, 10.5, foreground: Palette.White(0.55));
        hint.TextWrapping = TextWrapping.Wrap;
        hint.TextAlignment = TextAlignment.Center;
        hint.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(hint);

        if (retry is not null)
        {
            var button = UI.PillButton("Tentar reconectar", retry, UI.Glyph.Refresh);
            button.Margin = new Thickness(0, 10, 0, 0);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(button);
        }
        return panel;
    }
}

/// <summary>Draws a stream: the latest frame plus connecting/failure states.</summary>
public sealed class StreamView : Grid
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly ContentControl _overlay = new() { HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StatusChip _chip = new(ChipStyle.Overlay) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10) };
    private readonly StreamWatcher _watcher;

    public StreamView()
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.Linear);
        Children.Add(_image);
        Children.Add(_overlay);
        Children.Add(_chip);
        _chip.Visibility = Visibility.Collapsed;
        _watcher = new StreamWatcher(this, OnChanged);
    }

    /// <summary>Show the "AO VIVO" chip in the corner.</summary>
    public bool ShowsStatus
    {
        get => _chip.Visibility == Visibility.Visible;
        set => _chip.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Red tones for failures (used on the overview grid).</summary>
    public bool Alert { get; set; }

    /// <summary>Called by the retry button of the failure card; no button when null.</summary>
    public Action? Retry { get; set; }

    /// <summary>Content shown while connecting and no picture has arrived yet (default: a spinner).</summary>
    public Func<UIElement>? ConnectingContent { get; set; }

    public CameraStream? Stream => _watcher.Stream;

    public void Bind(CameraStream? stream)
    {
        _chip.Bind(stream);
        _watcher.Bind(stream);
    }

    private void OnChanged(string? property)
    {
        if (property == nameof(CameraStream.Frame))
        {
            _image.Source = _watcher.Stream?.Frame;
            return;
        }
        if (property is not (null or nameof(CameraStream.Status) or nameof(CameraStream.Failure))) return;

        var stream = _watcher.Stream;
        _image.Source = stream?.Frame;
        _overlay.Content = stream switch
        {
            { Status: StreamStatus.Connecting, Frame: null } => ConnectingContent?.Invoke() ?? new Spinner(),
            { Status: StreamStatus.Failed, Failure: { } failure } => FailureView.Create(failure, Alert, Retry),
            _ => null,
        };
    }
}

/// <summary>Live picture of a camera. Leases the stream while visible and active.</summary>
public sealed class CameraFeed : Grid
{
    private readonly StreamView _view = new() { ShowsStatus = true };
    private readonly StreamSession _session = new();
    private Camera? _camera;
    private bool _isActive = true;
    private bool _hd;

    public CameraFeed()
    {
        Background = Brushes.Black;
        Children.Add(_view);
        _session.Changed += () => _view.Bind(_session.Stream);
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => _session.Detach();
    }

    public StreamView View => _view;

    public Camera? Camera
    {
        get => _camera;
        set { if (!ReferenceEquals(_camera, value)) { _camera = value; Sync(); } }
    }

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; Sync(); } }
    }

    public bool Hd
    {
        get => _hd;
        set { if (_hd != value) { _hd = value; _session.SetHd(value); } }
    }

    private void Sync()
    {
        var pool = ((CamBar.App)Application.Current).Environment?.Pool;
        if (pool is null || !IsLoaded) return;
        if (_isActive && _camera is { } camera) _session.Attach(camera, _hd, pool);
        else _session.Detach();
    }
}
