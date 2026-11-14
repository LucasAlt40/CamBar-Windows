using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CamBar.Interop;

/// <summary>Native window looks WPF cannot do on its own: dark title bar, rounded corners, no-activate tool windows.</summary>
internal static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWCP_ROUND = 2;

    private static void Set(IntPtr hwnd, int attribute, int value)
    {
        try { Native.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Dark title bar; the caption color needs Windows 11 and is ignored on Windows 10.</summary>
    public static void DarkTitleBar(Window window, Color caption)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, 1);
        Set(hwnd, DWMWA_CAPTION_COLOR, caption.R | caption.G << 8 | caption.B << 16);
    }

    /// <summary>Rounded corners for borderless windows (Windows 11; harmless elsewhere).</summary>
    public static void RoundedCorners(Window window) =>
        Set(new WindowInteropHelper(window).EnsureHandle(), DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    /// <summary>Keeps the window out of Alt+Tab and stops clicks from stealing focus from what the user is working on.</summary>
    public static void NoActivateToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(style | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
    }

    /// <summary>Moves and sizes the window in physical pixels, above other windows, without activating it.</summary>
    public static void PlaceTopmost(Window window, int x, int y, int width, int height) =>
        Native.SetWindowPos(new WindowInteropHelper(window).EnsureHandle(), Native.HWND_TOPMOST, x, y, width, height, Native.SWP_NOACTIVATE);

    /// <summary>Keeps the window's client area at a fixed aspect ratio while the user drags its edges.</summary>
    public static void LockAspectRatio(Window window, double ratio)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        source?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != Native.WM_SIZING || window.WindowState != WindowState.Normal) return IntPtr.Zero;

            var rect = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.RECT>(lParam);
            Native.GetWindowRect(hwnd, out var outer);
            Native.GetClientRect(hwnd, out var inner);
            var chromeW = outer.Right - outer.Left - (inner.Right - inner.Left);
            var chromeH = outer.Bottom - outer.Top - (inner.Bottom - inner.Top);
            var clientW = rect.Right - rect.Left - chromeW;
            var clientH = rect.Bottom - rect.Top - chromeH;

            // edge codes: 1 left, 2 right, 3 top, 4 top-left, 5 top-right, 6 bottom, 7 bottom-left, 8 bottom-right
            var edge = wParam.ToInt32();
            if (edge is 3 or 6)
            {
                rect.Right = rect.Left + (int)Math.Round(clientH * ratio) + chromeW;
            }
            else
            {
                var newH = (int)Math.Round(clientW / ratio) + chromeH;
                if (edge is 4 or 5) rect.Top = rect.Bottom - newH; else rect.Bottom = rect.Top + newH;
            }
            System.Runtime.InteropServices.Marshal.StructureToPtr(rect, lParam, false);
            handled = true;
            return new IntPtr(1);
        });
    }
}
