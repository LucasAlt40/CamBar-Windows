using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CamBar.Theme;

/// <summary>Small builders so the code-built views stay readable.</summary>
public static class UI
{
    /// <summary>Segoe Fluent / MDL2 glyph codes used by the app.</summary>
    public static class Glyph
    {
        public const string Camera = "";
        public const string Video = "";
        public const string Power = "";
        public const string Pin = "";
        public const string PinFilled = "";
        public const string Unpin = "";
        public const string Expand = "";
        public const string Grid = "\uE80A";
        public const string Settings = "";
        public const string Add = "";
        public const string Remove = "";
        public const string Search = "";
        public const string Eye = "";
        public const string EyeOff = "";
        public const string Delete = "";
        public const string Check = "";
        public const string Close = "";
        public const string Refresh = "";
        public const string Volume = "";
        public const string Mute = "";
        public const string Lock = "";
        public const string Warning = "";
        public const string Bolt = "";
        public const string Up = "";
        public const string Down = "";
        public const string Left = "";
        public const string Right = "";
        public const string Monitor = "\uE7F4";
        public const string Sync = "\uE895";
    }

    public static TextBlock Text(string text, double size = 12, FontWeight? weight = null, Brush? foreground = null, bool mono = false) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = foreground ?? Brushes.White,
            FontFamily = mono ? Palette.MonoFont : Palette.UiFont,
            VerticalAlignment = VerticalAlignment.Center,
        };

    public static TextBlock Icon(string glyph, double size = 12, Brush? foreground = null) =>
        new()
        {
            Text = glyph,
            // Fluent/MDL2 glyphs live in the Private Use Area; anything else (e.g. the ↖ corner arrows) is a
            // regular Unicode symbol that the icon font lacks and would draw as an empty box
            FontFamily = glyph.Length > 0 && glyph[0] is >= '\uE000' and <= '\uF8FF' ? Palette.IconFont : Palette.SymbolFont,
            FontSize = size,
            Foreground = foreground ?? Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

    public static Border Rule(bool vertical = false, Brush? brush = null) =>
        new()
        {
            Background = brush ?? Palette.Hairline,
            Width = vertical ? 1 : double.NaN,
            Height = vertical ? double.NaN : 1,
            SnapsToDevicePixels = true,
        };

    public static Ellipse Dot(Brush fill, double size = 6) =>
        new() { Width = size, Height = size, Fill = fill, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Corner radius meaning "fully round": a capsule, or a circle when the box is square.</summary>
    public const double Round = 999;

    /// <summary>Rounded container with an optional hairline border. <see cref="Round"/> makes a capsule.</summary>
    public static Border Card(UIElement? child, Brush background, double radius = 12, Brush? stroke = null, Thickness? padding = null)
    {
        var border = new Border
        {
            Child = child,
            Background = background,
            CornerRadius = new CornerRadius(radius),
            BorderBrush = stroke ?? Palette.Hairline,
            BorderThickness = new Thickness(stroke is null && background == Brushes.Transparent ? 0 : 1),
            Padding = padding ?? new Thickness(0),
        };
        if (radius >= Round) MakeRound(border);
        return border;
    }

    /// <summary>Small pill that keeps its natural height (inside a row it would otherwise stretch into a tall oval).</summary>
    public static Border Capsule(UIElement child, Brush background, Brush? stroke = null, Thickness? padding = null)
    {
        var border = Card(child, background, Round, stroke, padding);
        border.VerticalAlignment = VerticalAlignment.Center;
        return border;
    }

    /// <summary>
    /// Keeps the corner radius at half the shorter side. WPF does not clamp a large radius into a capsule:
    /// it squeezes the corners into ellipses, so the whole border turns into an oval.
    /// </summary>
    public static void MakeRound(Border border)
    {
        void Apply()
        {
            var w = double.IsNaN(border.Width) ? border.ActualWidth : border.Width;
            var h = double.IsNaN(border.Height) ? border.ActualHeight : border.Height;
            border.CornerRadius = new CornerRadius(w > 0 && h > 0 ? Math.Min(w, h) / 2 : 0);
        }
        border.SizeChanged += (_, _) => Apply();
        Apply();
    }

    /// <summary>Round (or rounded-square) glyph button with a tooltip.</summary>
    public static Button IconButton(string glyph, string tooltip, Action onClick, double size = 30, double glyphSize = 12,
                                    Brush? foreground = null, Brush? background = null, Brush? stroke = null, double radius = Round)
    {
        var face = Card(Icon(glyph, glyphSize, foreground ?? Palette.White(0.85)), background ?? Palette.White(0.06), radius,
                        stroke ?? Palette.White(0.1));
        face.Width = size;
        face.Height = size;
        var button = new Button
        {
            Content = face,
            Style = (Style)Application.Current.FindResource("BareButton"),
            ToolTip = tooltip,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Small round button that sits on top of video (dark glass).</summary>
    public static Button OverlayButton(string glyph, string tooltip, Action onClick, double size = 24) =>
        IconButton(glyph, tooltip, onClick, size, 10, Brushes.White, Palette.Black(0.55), Palette.White(0.12));

    public static Button PillButton(string label, Action onClick, string? glyph = null)
    {
        UIElement content = label.Length == 0 ? Icon(glyph!, 11) : Text(label, 12, FontWeights.Medium, Palette.White(0.85));
        if (glyph is not null && label.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Icon(glyph, 11, Palette.White(0.85)));
            var text = Text(label, 12, FontWeights.Medium, Palette.White(0.85));
            text.Margin = new Thickness(7, 0, 0, 0);
            row.Children.Add(text);
            content = row;
        }
        var button = new Button { Content = content, Style = (Style)Application.Current.FindResource("PillButton"), VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Clips a element to a rounded rectangle that follows its size.</summary>
    public static void RoundClip(FrameworkElement element, double radius)
    {
        void Apply() => element.Clip = element.ActualWidth > 0 && element.ActualHeight > 0
            ? new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius)
            : null;
        element.SizeChanged += (_, _) => Apply();
        Apply();
    }
}
