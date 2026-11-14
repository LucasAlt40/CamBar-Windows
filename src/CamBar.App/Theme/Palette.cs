using System.Windows;
using System.Windows.Media;

namespace CamBar.Theme;

/// <summary>Colors of the "Nocturne Guard" design, as frozen brushes for code-built views.</summary>
public static class Palette
{
    private static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    /// <summary>Opaque color from 0xRRGGBB.</summary>
    private static SolidColorBrush Rgb(uint rgb) => Brush(0xFF000000 | rgb);

    /// <summary>White or black at the given opacity.</summary>
    public static SolidColorBrush White(double opacity) => Brush((uint)(opacity * 255) << 24 | 0xFFFFFF);
    public static SolidColorBrush Black(double opacity) => Brush((uint)(opacity * 255) << 24);
    public static SolidColorBrush With(SolidColorBrush source, double opacity) =>
        Brush((uint)(opacity * 255) << 24 | (uint)(source.Color.R << 16 | source.Color.G << 8 | source.Color.B));

    public static readonly SolidColorBrush Accent = Rgb(0x10B981);
    public static readonly SolidColorBrush AccentStrong = Rgb(0x059669);
    public static readonly SolidColorBrush Warning = Rgb(0xF59E0B);
    public static readonly SolidColorBrush WarningText = Rgb(0xFCD34D);
    public static readonly SolidColorBrush Danger = Rgb(0xF43F5E);
    public static readonly SolidColorBrush DangerSoft = Rgb(0xFB7185);
    public static readonly SolidColorBrush DangerText = Rgb(0xFDA4AF);
    public static readonly SolidColorBrush Gray = Rgb(0x8E8E93);

    public static readonly SolidColorBrush Window = Rgb(0x0F1319);
    public static readonly SolidColorBrush TitleBar = Rgb(0x141922);
    public static readonly SolidColorBrush Sidebar = Rgb(0x121720);
    public static readonly SolidColorBrush Panel = Rgb(0x171C25);
    public static readonly SolidColorBrush Card = Rgb(0x1C2128);
    public static readonly SolidColorBrush Hairline = White(0.08);

    public static readonly Color TitleBarColor = Color.FromRgb(0x14, 0x19, 0x22);

    public static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
}
