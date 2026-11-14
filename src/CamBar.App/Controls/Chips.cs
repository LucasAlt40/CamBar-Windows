using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamBar.Streaming;
using CamBar.Theme;

namespace CamBar.Controls;

public enum ChipStyle
{
    /// <summary>Over video: dark capsule, colored dot, white text.</summary>
    Overlay,
    /// <summary>On chrome: tinted capsule and text.</summary>
    Tinted,
}

public static class StatusInfo
{
    public static string Label(StreamStatus status) => status switch
    {
        StreamStatus.Live => "AO VIVO",
        StreamStatus.Connecting => "CONECTANDO",
        StreamStatus.Failed => "SEM SINAL",
        _ => "PARADO",
    };

    public static SolidColorBrush Color(StreamStatus status, ChipStyle style) => status switch
    {
        StreamStatus.Live => style == ChipStyle.Overlay ? Palette.Danger : Palette.Accent,
        StreamStatus.Connecting => Palette.Warning,
        StreamStatus.Failed => style == ChipStyle.Overlay ? Palette.Gray : Palette.Danger,
        _ => Palette.Gray,
    };
}

/// <summary>Small monospaced capsule with a colored dot, e.g. "● AO VIVO".</summary>
public sealed class StatusChip : Border
{
    private readonly ChipStyle _style;
    private readonly System.Windows.Shapes.Ellipse _dot = UI.Dot(Palette.Gray);
    private readonly TextBlock _label = UI.Text("", 10, FontWeights.SemiBold, mono: true);
    private readonly StreamWatcher _watcher;

    public StatusChip(ChipStyle style = ChipStyle.Tinted)
    {
        _style = style;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        _label.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(_dot);
        row.Children.Add(_label);
        Child = row;
        CornerRadius = new CornerRadius(999);
        BorderThickness = new Thickness(1);
        var vertical = style == ChipStyle.Overlay ? 5 : 4;
        Padding = new Thickness(9, vertical, 9, vertical);
        VerticalAlignment = VerticalAlignment.Center;
        _watcher = new StreamWatcher(this, name => { if (name is null or nameof(CameraStream.Status)) Refresh(); });
        Refresh();
    }

    public void Bind(CameraStream? stream) => _watcher.Bind(stream);

    private void Refresh()
    {
        var status = _watcher.Stream?.Status ?? StreamStatus.Idle;
        var color = StatusInfo.Color(status, _style);
        _dot.Fill = color;
        _label.Text = StatusInfo.Label(status);
        _label.Foreground = _style == ChipStyle.Overlay ? Brushes.White : color;
        Background = _style == ChipStyle.Overlay ? Palette.Black(0.55) : Palette.With(color, 0.12);
        BorderBrush = _style == ChipStyle.Overlay ? Palette.White(0.12) : Palette.With(color, 0.3);
    }
}

/// <summary>"1920×1080 • 15 FPS • H.265 Main", once ffmpeg has reported it.</summary>
public sealed class TelemetryChip : Border
{
    private readonly TextBlock _text = UI.Text("", 11, foreground: Palette.Accent, mono: true);
    private readonly StreamWatcher _watcher;

    public TelemetryChip()
    {
        Child = _text;
        CornerRadius = new CornerRadius(999);
        Background = Palette.Black(0.35);
        BorderBrush = Palette.Hairline;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 5, 12, 5);
        _watcher = new StreamWatcher(this, name => { if (name is null or nameof(CameraStream.Info)) Refresh(); });
        Refresh();
    }

    public void Bind(CameraStream? stream) => _watcher.Bind(stream);

    private void Refresh()
    {
        if (_watcher.Stream?.Info is { HasVideo: true } info)
        {
            _text.Text = info.Summary;
            Visibility = Visibility.Visible;
        }
        else Visibility = Visibility.Collapsed;
    }
}

/// <summary>Small monospaced tag on a dark capsule, used for telemetry over video.</summary>
public static class OverlayTag
{
    public static Border Create(string text, Brush? color = null) =>
        UI.Capsule(UI.Text(text, 10, foreground: color ?? Brushes.White, mono: true), Palette.Black(0.6),
                   Brushes.Transparent, new Thickness(8, 4, 8, 4));
}
