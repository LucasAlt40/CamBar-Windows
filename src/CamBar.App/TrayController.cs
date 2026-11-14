using System.Windows.Threading;
using CamBar.Interop;
using CamBar.Views;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CamBar;

/// <summary>The notification-area icon: click opens the popover, right-click the menu. Shows whether any camera is online.</summary>
public sealed class TrayController : IDisposable
{
    private readonly AppEnvironment _env;
    private readonly MenuWindow _menu;
    private readonly Forms.NotifyIcon _icon;
    private readonly Dispatcher _ui = System.Windows.Application.Current.Dispatcher;
    private Drawing.Icon? _current;

    public TrayController(AppEnvironment env, MenuWindow menu, Pinning.IWindowRouter router)
    {
        _env = env;
        _menu = menu;

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("Abrir painel", null, (_, _) => ShowPopover());
        contextMenu.Items.Add("Visão geral", null, (_, _) => router.ShowOverview());
        contextMenu.Items.Add("Gerenciar câmeras", null, (_, _) => router.ShowSettings());
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Sair", null, (_, _) => env.Quit());

        _icon = new Forms.NotifyIcon { Visible = true, ContextMenuStrip = contextMenu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowPopover();
        };
        Refresh();

        env.Reachability.PropertyChanged += (_, _) => Refresh();
        env.Store.PropertyChanged += (_, _) => Refresh();
        // the icon color follows the taskbar theme
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    private void ShowPopover()
    {
        var cursor = Forms.Cursor.Position;
        _menu.Toggle(cursor.X, cursor.Y);
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            _ui.BeginInvoke(Refresh);
    }

    private void Refresh()
    {
        var online = _env.Reachability.OnlineCount;
        var total = _env.Store.Cameras.Count;
        // NotifyIcon.Text is limited to 63 characters
        _icon.Text = total == 0 ? "CamBar — nenhuma câmera" : $"CamBar — {online} de {total} online";

        var next = Draw(online > 0);
        _icon.Icon = next;
        _current?.Dispose();
        _current = next;
    }

    /// <summary>A small video camera, white on a dark taskbar and black on a light one, with a status dot.</summary>
    private static Drawing.Icon Draw(bool anyOnline)
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        using var bitmap = new Drawing.Bitmap(size, size);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var u = size / 32f;
            var ink = UsesLightTaskbar() ? Drawing.Color.Black : Drawing.Color.White;
            using var brush = new Drawing.SolidBrush(ink);

            using var body = RoundedRect(3 * u, 10 * u, 19 * u, 14 * u, 3 * u);
            g.FillPath(brush, body);
            g.FillPolygon(brush, [new Drawing.PointF(23 * u, 15 * u), new(30 * u, 11 * u), new(30 * u, 23 * u), new(23 * u, 19 * u)]);

            using var dot = new Drawing.SolidBrush(anyOnline ? Drawing.Color.FromArgb(0x10, 0xB9, 0x81) : Drawing.Color.FromArgb(0x8E, 0x8E, 0x93));
            using var ring = new Drawing.Pen(UsesLightTaskbar() ? Drawing.Color.White : Drawing.Color.Black, Math.Max(1f, 1.5f * u));
            g.FillEllipse(dot, 18 * u, 1 * u, 10 * u, 10 * u);
            g.DrawEllipse(ring, 18 * u, 1 * u, 10 * u, 10 * u);
        }

        var handle = bitmap.GetHicon();
        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    private static Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
        path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
        path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static bool UsesLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _current?.Dispose();
    }
}
