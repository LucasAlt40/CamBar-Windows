using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CamBar.Services;
using CamBar.Theme;

namespace CamBar.Controls;

/// <summary>
/// Directional pad + zoom. Holding a control moves the camera, releasing stops it;
/// the green knob in the middle works as a joystick, and a plain click on it recenters.
/// </summary>
public sealed class PtzPad : StackPanel
{
    private const double Speed = 0.5;
    private const double KnobRadius = 36;

    private readonly PtzController _ptz;
    private readonly Dictionary<string, (Border Face, TextBlock Glyph)> _holds = [];
    private readonly TranslateTransform _knobOffset = new();

    public PtzPad(PtzController ptz)
    {
        _ptz = ptz;
        Orientation = Orientation.Horizontal;

        // zoom capsule
        var zoom = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        zoom.Children.Add(HoldButton(UI.Glyph.Add, "zoom-in", zoom: Speed));
        var divider = UI.Rule(brush: Palette.White(0.12));
        divider.Width = 18;
        zoom.Children.Add(divider);
        zoom.Children.Add(HoldButton(UI.Glyph.Remove, "zoom-out", zoom: -Speed));
        var zoomCapsule = UI.Capsule(zoom, Palette.Black(0.5), Palette.White(0.12));
        zoomCapsule.Width = 38;
        zoomCapsule.Height = 84;
        zoomCapsule.VerticalAlignment = VerticalAlignment.Center;
        zoomCapsule.Margin = new Thickness(0, 0, 12, 0);
        Children.Add(zoomCapsule);

        // directional ring
        var ring = new Grid { Width = 124, Height = 124 };
        ring.Children.Add(UI.Card(null, Palette.Black(0.5), UI.Round, Palette.White(0.12)));
        ring.Children.Add(Place(HoldButton(UI.Glyph.Up, "up", tilt: Speed), 0, -40));
        ring.Children.Add(Place(HoldButton(UI.Glyph.Down, "down", tilt: -Speed), 0, 40));
        ring.Children.Add(Place(HoldButton(UI.Glyph.Left, "left", pan: -Speed), -40, 0));
        ring.Children.Add(Place(HoldButton(UI.Glyph.Right, "right", pan: Speed), 40, 0));
        ring.Children.Add(Knob());
        Children.Add(ring);

        _ptz.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PtzController.ActiveMove)) Highlight(); };
        Unloaded += (_, _) => _ptz.Stop();
    }

    private static FrameworkElement Place(FrameworkElement element, double x, double y)
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        element.RenderTransform = new TranslateTransform(x, y);
        return element;
    }

    private FrameworkElement HoldButton(string glyph, string move, double pan = 0, double tilt = 0, double zoom = 0)
    {
        var icon = UI.Icon(glyph, 13, Palette.White(0.9));
        var face = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = Brushes.Transparent,   // transparent but hit-testable
            Child = icon,
            Cursor = Cursors.Hand,
        };
        _holds[move] = (face, icon);

        face.MouseLeftButtonDown += (_, e) =>
        {
            face.CaptureMouse();
            _ptz.Move(move, pan, tilt, zoom);
            e.Handled = true;
        };
        face.MouseLeftButtonUp += (_, e) =>
        {
            face.ReleaseMouseCapture();
            _ptz.Stop();
            e.Handled = true;
        };
        face.LostMouseCapture += (_, _) => _ptz.Stop();
        return face;
    }

    private void Highlight()
    {
        foreach (var (move, (face, glyph)) in _holds)
        {
            var active = _ptz.ActiveMove == move;
            face.Background = active ? Palette.With(Palette.Accent, 0.2) : Brushes.Transparent;
            glyph.Foreground = active ? Palette.Accent : Palette.White(0.9);
        }
    }

    private FrameworkElement Knob()
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Palette.Accent };
        var knob = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = Palette.With(Palette.Accent, 0.25),
            BorderBrush = Palette.With(Palette.Accent, 0.5),
            BorderThickness = new Thickness(1),
            Child = dot,
            RenderTransform = _knobOffset,
            Cursor = Cursors.SizeAll,
            ToolTip = "Arraste para mover; clique para centralizar",
        };

        Point origin = default;
        knob.MouseLeftButtonDown += (_, e) =>
        {
            knob.CaptureMouse();
            origin = e.GetPosition((IInputElement)knob.Parent);
            e.Handled = true;
        };
        knob.MouseMove += (_, e) =>
        {
            if (!knob.IsMouseCaptured) return;
            var delta = e.GetPosition((IInputElement)knob.Parent) - origin;
            var length = delta.Length;
            var scale = length > KnobRadius ? KnobRadius / length : 1;
            _knobOffset.X = delta.X * scale;
            _knobOffset.Y = delta.Y * scale;
            _ptz.Steer(_knobOffset.X / KnobRadius, -_knobOffset.Y / KnobRadius);   // y up for the camera
        };
        knob.MouseLeftButtonUp += (_, e) =>
        {
            if (!knob.IsMouseCaptured) return;
            var delta = e.GetPosition((IInputElement)knob.Parent) - origin;
            knob.ReleaseMouseCapture();
            if (delta.Length < 4) _ptz.GoHome();
            _ptz.Stop();
            _knobOffset.X = _knobOffset.Y = 0;
            e.Handled = true;
        };
        return knob;
    }
}
