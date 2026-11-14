namespace CamBar.Core;

/// <summary>Screen corner where a camera can be pinned.</summary>
public enum PinCorner { TopLeading, TopTrailing, BottomLeading, BottomTrailing }

public static class PinCornerExtensions
{
    public static string Label(this PinCorner corner) => corner switch
    {
        PinCorner.TopLeading => "Superior esquerdo",
        PinCorner.TopTrailing => "Superior direito",
        PinCorner.BottomLeading => "Inferior esquerdo",
        _ => "Inferior direito",
    };

    public static bool IsTop(this PinCorner corner) => corner is PinCorner.TopLeading or PinCorner.TopTrailing;
    public static bool IsLeading(this PinCorner corner) => corner is PinCorner.TopLeading or PinCorner.BottomLeading;

    /// <summary>The diagonally opposite corner, where the resize handle sits.</summary>
    public static PinCorner Opposite(this PinCorner corner) => corner switch
    {
        PinCorner.TopLeading => PinCorner.BottomTrailing,
        PinCorner.TopTrailing => PinCorner.BottomLeading,
        PinCorner.BottomLeading => PinCorner.TopTrailing,
        _ => PinCorner.TopLeading,
    };

    /// <summary>Arrow glyph (Segoe UI Symbol) pointing at the corner.</summary>
    public static string Glyph(this PinCorner corner) => corner switch
    {
        PinCorner.TopLeading => "↖",
        PinCorner.TopTrailing => "↗",
        PinCorner.BottomLeading => "↙",
        _ => "↘",
    };
}

/// <summary>Plain geometry, so layout can be checked without WPF. Origin at the top left, y grows downward.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;

    public bool Contains(RectD other) =>
        other.MinX >= MinX && other.MinY >= MinY && other.MaxX <= MaxX && other.MaxY <= MaxY;
}

public readonly record struct SizeD(double Width, double Height);

/// <summary>Where and how large the pinned camera is on a given screen. Units are device-independent pixels.</summary>
public static class PinLayout
{
    public const double Margin = 16;
    public const double AspectRatio = 16.0 / 9.0;
    /// <summary>Below this the hover controls no longer fit.</summary>
    public const double MinWidth = 240;

    /// <summary>Largest width that still fits the screen with the margin around it.</summary>
    public static double MaxWidth(RectD visible) =>
        Math.Max(MinWidth, Math.Floor(Math.Min(visible.Width - Margin * 2, (visible.Height - Margin * 2) * AspectRatio)));

    /// <summary>The user's width (from resizing) clamped to the screen, or the automatic size when there is none.</summary>
    public static SizeD Size(RectD visible, double? width)
    {
        if (width is not { } preferred) return Size(visible);
        var w = Math.Round(Math.Min(Math.Max(preferred, MinWidth), MaxWidth(visible)));
        return new SizeD(w, Math.Round(w / AspectRatio));
    }

    /// <summary>
    /// Width change for a drag of the resize handle by (dx, dy) in screen coordinates (y down).
    /// The drag is projected onto the window's diagonal, so the handle follows the pointer at 16:9
    /// and moving away from the pinned corner grows the picture.
    /// </summary>
    public static double WidthChange(double dx, double dy, PinCorner pinnedTo)
    {
        var outX = pinnedTo.IsLeading() ? dx : -dx;
        var outY = pinnedTo.IsTop() ? dy : -dy;
        return (outX + outY / AspectRatio) / (1 + 1 / (AspectRatio * AspectRatio));
    }

    /// <summary>
    /// About a fifth of the screen width (never below 280 nor above 560), and never taller than
    /// a third of the screen, so it stays a glanceable picture-in-picture on any display.
    /// </summary>
    public static SizeD Size(RectD visible)
    {
        var width = Math.Min(Math.Max(visible.Width * 0.2, 280), 560);
        width = Math.Min(width, Math.Min(visible.Height / 3 * AspectRatio, visible.Width - Margin * 2));
        width = Math.Round(width);
        return new SizeD(width, Math.Round(width / AspectRatio));
    }

    /// <summary>Frame inside the screen's work area, which already excludes the taskbar.</summary>
    public static RectD Frame(PinCorner corner, RectD visible, double? width = null)
    {
        var size = Size(visible, width);
        var x = corner.IsLeading() ? visible.MinX + Margin : visible.MaxX - Margin - size.Width;
        var y = corner.IsTop() ? visible.MinY + Margin : visible.MaxY - Margin - size.Height;
        return new RectD(x, y, size.Width, size.Height);
    }
}
