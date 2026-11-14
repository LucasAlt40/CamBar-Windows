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
        public const string Grid = "";
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
        public const string Monitor = "";
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
            FontFamily = Palette.IconFont,
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

    /// <summary>Rounded container with an optional hairline border.</summary>
    public static Border Card(UIElement? child, Brush background, double radius = 12, Brush? stroke = null, Thickness? padding = null) =>
        new()
        {
            Child = child,
            Background = background,
            CornerRadius = new CornerRadius(radius),
            BorderBrush = stroke ?? Palette.Hairline,
            BorderThickness = new Thickness(stroke is null && background == Brushes.Transparent ? 0 : 1),
            Padding = padding ?? new Thickness(0),
        };

    public static Border Capsule(UIElement child, Brush background, Brush? stroke = null, Thickness? padding = null) =>
        Card(child, background, 999, stroke, padding);

    /// <summary>Round (or rounded-square) glyph button with a tooltip.</summary>
    public static Button IconButton(string glyph, string tooltip, Action onClick, double size = 30, double glyphSize = 12,
                                    Brush? foreground = null, Brush? background = null, Brush? stroke = null, double radius = 999)
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
        var button = new Button { Content = content, Style = (Style)Application.Current.FindResource("PillButton") };
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
