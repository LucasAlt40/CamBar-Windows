using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CamBar.Controls;
using CamBar.Core;
using CamBar.Interop;
using CamBar.Pinning;
using CamBar.Services;
using CamBar.Theme;
using Forms = System.Windows.Forms;

namespace CamBar.Views;

/// <summary>The tray popover: selected camera live, tabs to switch, quick actions.</summary>
public sealed class MenuWindow : Window
{
    private const double PreviewWidth = 372;
    private const double PreviewHeight = 209;

    private readonly AppEnvironment _env;
    private readonly CameraFeed _feed = new() { IsActive = false, Width = PreviewWidth, Height = PreviewHeight };
    private readonly TextBlock _title = UI.Text("Câmeras", 14, FontWeights.SemiBold);
    private readonly System.Windows.Shapes.Ellipse _titleDot = UI.Dot(Palette.Gray);
    private readonly Grid _previewHost = new() { Width = PreviewWidth, Height = PreviewHeight };
    private readonly UIElement _empty;
    private readonly Border _previewBox;
    private readonly ScrollViewer _tabScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Border _tabBar;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
    private readonly TextBlock _footerText = UI.Text("", 12, FontWeights.Medium);
    private readonly TextBlock _footerIcon = UI.Icon(UI.Glyph.Check, 13, Palette.Accent);
    private readonly StackPanel _previewButtons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10) };
    private DateTime _hiddenAt = DateTime.MinValue;

    public MenuWindow(AppEnvironment env)
    {
        _env = env;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        Width = 402;
        SizeToContent = SizeToContent.Height;
        Background = Palette.Window;
        Foreground = Brushes.White;
        FontFamily = Palette.UiFont;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Title = "CamBar";
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10_000;   // first paint happens off screen; ShowNear puts it in place
        Top = -10_000;

        _empty = BuildEmptyState();
        _previewBox = BuildPreview();
        _tabScroll.Content = _tabs;
        _tabBar = UI.Capsule(_tabScroll, Palette.Black(0.45), Palette.White(0.06));
        _tabBar.Margin = new Thickness(0, 12, 0, 0);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(_previewHost);
        _previewHost.Children.Add(_previewBox);
        _previewHost.Children.Add(_empty);
        content.Children.Add(_tabBar);

        var root = new StackPanel();
        root.Children.Add(BuildHeader());
        root.Children.Add(UI.Rule(brush: Palette.White(0.06)));
        root.Children.Add(content);
        root.Children.Add(UI.Rule(brush: Palette.White(0.06)));
        root.Children.Add(BuildFooter());

        Content = new Border { Child = root, BorderBrush = Palette.White(0.14), BorderThickness = new Thickness(1), Background = Palette.Window };

        SourceInitialized += (_, _) => WindowEffects.RoundedCorners(this);
        Deactivated += (_, _) => { if (IsVisible) Dismiss(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Dismiss(); };

        env.Store.PropertyChanged += (_, _) => Rebuild();
        env.Reachability.PropertyChanged += (_, _) => RefreshStatus();
        Rebuild();
    }

    // MARK: Showing

    /// <summary>Opens the popover next to the tray icon, or closes it when it is already open.</summary>
    public void Toggle(int cursorX, int cursorY)
    {
        if (IsVisible) { Dismiss(); return; }
        // the click that deactivated the popover also reaches the tray icon: do not reopen at once
        if (DateTime.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(300)) return;

        _env.Pool.PanelOpen = true;
        _env.Reachability.Refresh();
        _feed.IsActive = true;
        Show();
        PlaceNear(cursorX, cursorY);
        Dispatcher.BeginInvoke(() => PlaceNear(cursorX, cursorY), System.Windows.Threading.DispatcherPriority.Loaded);
        Activate();
    }

    private void Dismiss()
    {
        _hiddenAt = DateTime.UtcNow;
        _feed.IsActive = false;
        _env.Pool.PanelOpen = false;
        Hide();
    }

    /// <summary>Above the taskbar, on the side of the screen where the tray icon was clicked.</summary>
    private void PlaceNear(int cursorX, int cursorY)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (!Native.GetWindowRect(hwnd, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(cursorX, cursorY));
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var margin = (int)Math.Round(12 * Native.ScaleAt(cursorX, cursorY));

        var x = cursorX > bounds.X + bounds.Width / 2 ? work.Right - width - margin : work.Left + margin;
        var y = work.Top > bounds.Top ? work.Top + margin : work.Bottom - height - margin;   // taskbar on top or elsewhere
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // MARK: Pieces

    private UIElement BuildHeader()
    {
        var circle = UI.Card(UI.Icon(UI.Glyph.Camera, 14, Palette.White(0.85)), Palette.White(0.06), 999, Palette.White(0.1));
        circle.Width = circle.Height = 30;

        var dock = new DockPanel { Margin = new Thickness(14, 12, 14, 12), LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        _title.Margin = new Thickness(10, 0, 8, 0);
        left.Children.Add(circle);
        left.Children.Add(_title);
        left.Children.Add(_titleDot);
        dock.Children.Add(left);

        var quit = UI.IconButton(UI.Glyph.Power, "Sair", _env.Quit, 30, 12);
        DockPanel.SetDock(quit, Dock.Right);
        dock.Children.Add(quit);
        return dock;
    }

    private Border BuildPreview()
    {
        var overlay = _previewButtons;
        overlay.Children.Add(new Border());   // placeholder replaced in Rebuild
        var grid = new Grid { Width = PreviewWidth, Height = PreviewHeight };
        grid.Children.Add(_feed);
        grid.Children.Add(overlay);

        var box = UI.Card(grid, Brushes.Black, 12, Palette.White(0.08));
        box.Width = PreviewWidth;
        box.Height = PreviewHeight;
        grid.Clip = new RectangleGeometry(new Rect(0, 0, PreviewWidth, PreviewHeight), 12, 12);
        return box;
    }

    private UIElement BuildEmptyState()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = UI.Icon(UI.Glyph.Video, 26, Palette.White(0.4));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var text = UI.Text("Nenhuma câmera ainda", 12, foreground: Palette.White(0.55));
        text.Margin = new Thickness(0, 10, 0, 10);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(text);
        var add = UI.PillButton("Adicionar câmera", () => { Dismiss(); _env.Router!.ShowSettings(); }, UI.Glyph.Add);
        add.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(add);
        return UI.Card(stack, Palette.Card, 12, Palette.White(0.08));
    }

    private UIElement BuildFooter()
    {
        var dock = new DockPanel { Margin = new Thickness(14, 11, 14, 11), LastChildFill = false };

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        _footerIcon.Margin = new Thickness(0, 0, 8, 0);
        left.Children.Add(_footerIcon);
        left.Children.Add(_footerText);
        dock.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);

        var keep = new ToggleButton
        {
            Style = (Style)Application.Current.FindResource("SwitchToggle"),
            IsChecked = _env.Pool.KeepConnected,
            ToolTip = "Mantém as câmeras conectadas em modo espera com o painel fechado.",
            Focusable = false,
        };
        keep.Click += (_, _) => _env.Pool.KeepConnected = keep.IsChecked == true;
        var keepLabel = UI.Text("Sempre ativo", 11, foreground: Palette.White(0.6));
        keepLabel.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(keepLabel);
        right.Children.Add(keep);

        var overview = UI.IconButton(UI.Glyph.Grid, "Visão geral", () => { Dismiss(); _env.Router!.ShowOverview(); }, 28, 13,
                                     Palette.White(0.6), Brushes.Transparent, Brushes.Transparent, 8);
        overview.Margin = new Thickness(12, 0, 0, 0);
        var manage = UI.IconButton(UI.Glyph.Settings, "Gerenciar câmeras", () => { Dismiss(); _env.Router!.ShowSettings(); }, 28, 13,
                                   Palette.White(0.6), Brushes.Transparent, Brushes.Transparent, 8);
        right.Children.Add(overview);
        right.Children.Add(manage);
        dock.Children.Add(right);
        return dock;
    }

    // MARK: Refresh

    private void Rebuild()
    {
        var store = _env.Store;
        var selected = store.Selected;
        _title.Text = selected?.Name is { Length: > 0 } name ? name : "Câmeras";
        _feed.Camera = selected;
        _previewBox.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = selected is null ? Visibility.Visible : Visibility.Collapsed;

        _previewButtons.Children.Clear();
        if (selected is not null)
        {
            _previewButtons.Children.Add(PinMenu.Button(selected, _env.Pins));
            var expand = UI.OverlayButton(UI.Glyph.Expand, "Abrir em janela", () => { Dismiss(); _env.Router!.ShowViewer(selected); });
            expand.Margin = new Thickness(6, 0, 0, 0);
            _previewButtons.Children.Add(expand);
        }

        _tabs.Children.Clear();
        _tabBar.Visibility = store.Cameras.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var camera in store.Cameras)
        {
            var isSelected = camera.Id == store.SelectedId;
            var label = UI.Text(camera.Name, 12, isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                                isSelected ? Brushes.White : Palette.White(0.55));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var face = UI.Capsule(label, isSelected ? Palette.White(0.14) : Brushes.Transparent, Brushes.Transparent,
                                  new Thickness(14, 7, 14, 7));
            face.MinWidth = Math.Max(0, (PreviewWidth - 6) / store.Cameras.Count - 2);
            var button = new Button { Content = face, Style = (Style)Application.Current.FindResource("BareButton"), Focusable = false };
            var id = camera.Id;
            button.Click += (_, _) => store.SelectedId = id;
            _tabs.Children.Add(button);
        }
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var store = _env.Store;
        var online = _env.Reachability.OnlineCount;
        _titleDot.Visibility = store.Selected is null ? Visibility.Collapsed : Visibility.Visible;
        _titleDot.Fill = store.Selected is { } selected && _env.Reachability.IsOnline(selected.Id) ? Palette.Accent : Palette.Gray;
        _footerText.Text = $"{online} de {store.Cameras.Count} online";
        _footerIcon.Text = online == 0 ? "" : UI.Glyph.Check;
        _footerIcon.Foreground = online == 0 ? Palette.Gray : Palette.Accent;
    }
}
