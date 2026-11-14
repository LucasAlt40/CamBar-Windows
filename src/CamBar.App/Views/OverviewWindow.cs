using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CamBar.Controls;
using CamBar.Core;
using CamBar.Streaming;
using CamBar.Theme;

namespace CamBar.Views;

/// <summary>Grid with every camera.</summary>
public sealed class OverviewWindow : DarkWindow
{
    private const double MinTile = 340;
    private const double Gap = 16;

    private readonly AppEnvironment _env;
    private readonly WrapPanel _grid = new() { Margin = new Thickness(20, 20, 4, 4) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly UIElement _empty;
    private readonly TextBlock _channels = UI.Text("", 11, foreground: Palette.White(0.65), mono: true);
    private readonly TextBlock _onlineText = UI.Text("", 11, foreground: Palette.Accent, mono: true);
    private readonly TextBlock _offlineText = UI.Text("", 11, foreground: Palette.Danger, mono: true);
    private readonly List<OverviewTile> _tiles = [];

    public OverviewWindow(AppEnvironment env)
    {
        _env = env;
        Title = "Visão geral";
        Width = 936;
        Height = 679;
        MinWidth = 536;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _empty = BuildEmpty();
        _scroll.Content = _grid;
        _scroll.SizeChanged += (_, _) => LayoutTiles();

        var content = new Grid();
        content.Children.Add(_scroll);
        content.Children.Add(_empty);

        var root = new DockPanel();
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var rule = UI.Rule();
        DockPanel.SetDock(rule, Dock.Top);
        root.Children.Add(rule);
        root.Children.Add(content);
        Content = root;

        env.Store.PropertyChanged += OnChanged;
        env.Reachability.PropertyChanged += OnChanged;
        Closed += (_, _) =>
        {
            env.Store.PropertyChanged -= OnChanged;
            env.Reachability.PropertyChanged -= OnChanged;
        };
        Rebuild();
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is Services.CameraStore && e.PropertyName == nameof(Services.CameraStore.Cameras)) Rebuild();
        else RefreshCounts();
    }

    private UIElement BuildHeader()
    {
        var dock = new DockPanel { Height = 46, Margin = new Thickness(16, 0, 14, 0), LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(UI.Text("CamBar — Visão Geral", 13, FontWeights.SemiBold));
        var chip = UI.Capsule(_channels, Palette.White(0.06), Brushes.Transparent, new Thickness(9, 3, 9, 3));
        chip.Margin = new Thickness(12, 0, 0, 0);
        left.Children.Add(chip);
        dock.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(right, Dock.Right);
        right.Children.Add(Counter(Palette.Accent, _onlineText));
        var offline = Counter(Palette.Danger, _offlineText);
        offline.Margin = new Thickness(14, 0, 14, 0);
        right.Children.Add(offline);
        right.Children.Add(UI.PillButton("Atualizar", () =>
        {
            _env.Reachability.Refresh();
            _env.Pool.ReconnectAll();
        }, UI.Glyph.Refresh));
        dock.Children.Add(right);

        return new Border { Child = dock, Background = Palette.TitleBar };
    }

    private static StackPanel Counter(Brush color, TextBlock text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dot = UI.Dot(color);
        dot.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(dot);
        row.Children.Add(text);
        return row;
    }

    private static UIElement BuildEmpty()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = UI.Icon(UI.Glyph.Video, 28, Palette.White(0.4));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var text = UI.Text("Nenhuma câmera cadastrada", 13, foreground: Palette.White(0.55));
        text.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(text);
        return stack;
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _tiles.Clear();
        foreach (var camera in _env.Store.Cameras)
        {
            var tile = new OverviewTile(_env, camera, _env.Store.NumberOf(camera) ?? 0) { Margin = new Thickness(0, 0, Gap, Gap) };
            _tiles.Add(tile);
            _grid.Children.Add(tile);
        }
        _empty.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = _tiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        LayoutTiles();
        RefreshCounts();
    }

    /// <summary>As many columns as fit with at least <see cref="MinTile"/> each; tiles share the leftover width.</summary>
    private void LayoutTiles()
    {
        var available = _scroll.ViewportWidth - 24;
        if (available <= 0 || double.IsNaN(available)) available = ActualWidth - 40;
        var columns = Math.Max(1, (int)Math.Floor(available / (MinTile + Gap)));
        var width = Math.Max(200, available / columns - Gap);
        foreach (var tile in _tiles) tile.SetWidth(width);
    }

    private void RefreshCounts()
    {
        var total = _env.Store.Cameras.Count;
        var online = _env.Reachability.OnlineCount;
        _channels.Text = $"{total} canais";
        _onlineText.Text = $"{online} Online";
        _offlineText.Text = $"{total - online} Sem sinal";
    }
}

/// <summary>One camera in the grid. Leases its stream while on screen.</summary>
internal sealed class OverviewTile : Border
{
    private readonly AppEnvironment _env;
    private readonly Camera _camera;
    private readonly StreamSession _session = new();
    private readonly StreamView _view = new() { Alert = true, ShowsStatus = false };
    private readonly System.Windows.Shapes.Ellipse _dot = UI.Dot(Palette.Gray, 7);
    private readonly TextBlock _fps = UI.Text("", 10.5, foreground: Palette.Accent, mono: true);
    private readonly StatusChip _chip = new();
    private readonly Grid _videoArea = new() { Background = Brushes.Black };
    private readonly Grid _liveOverlay = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10) };
    private readonly Border _resolutionTag = new();
    private readonly StreamWatcher _watcher;

    public OverviewTile(AppEnvironment env, Camera camera, int number)
    {
        _env = env;
        _camera = camera;
        Background = Palette.Panel;
        CornerRadius = new CornerRadius(14);
        BorderBrush = Palette.Hairline;
        BorderThickness = new Thickness(1);
        UI.RoundClip(this, 14);

        var header = new DockPanel { Height = 40, Margin = new Thickness(12, 0, 12, 0), LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _dot.Margin = new Thickness(0, 0, 8, 0);
        left.Children.Add(_dot);
        var name = UI.Text(camera.Name, 12.5, FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxWidth = 150;
        left.Children.Add(name);
        var tag = UI.Card(UI.Text($"CAM {number:00}", 10, foreground: Palette.White(0.55), mono: true), Palette.White(0.06), 5,
                          Brushes.Transparent, new Thickness(6, 2, 6, 2));
        tag.Margin = new Thickness(8, 0, 0, 0);
        tag.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(tag);
        header.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(right, Dock.Right);
        _fps.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(_fps);
        right.Children.Add(_chip);
        var pin = PinButton(camera);
        pin.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(pin);
        header.Children.Add(right);

        _view.Retry = () => env.Pool.Reconnect(camera.Id);
        _view.ConnectingContent = Negotiating;
        _videoArea.Children.Add(_view);

        _liveOverlay.Children.Add(_resolutionTag);
        var expand = UI.Capsule(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { UI.Icon(UI.Glyph.Eye, 10, Palette.Accent), Spaced(UI.Text("Clique para expandir", 10, foreground: Palette.Accent, mono: true)) },
            },
            Palette.Black(0.6), Brushes.Transparent, new Thickness(8, 4, 8, 4));
        expand.HorizontalAlignment = HorizontalAlignment.Right;
        _liveOverlay.Children.Add(expand);
        _resolutionTag.HorizontalAlignment = HorizontalAlignment.Left;
        _videoArea.Children.Add(_liveOverlay);

        // a click on the picture opens the expanded window (the failure card keeps its own retry button)
        _videoArea.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled || _view.Stream is not { Status: StreamStatus.Live }) return;
            _env.Router!.ShowViewer(_camera);
        };
        _videoArea.Cursor = Cursors.Hand;

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(UI.Rule());
        stack.Children.Add(_videoArea);
        Child = stack;

        _watcher = new StreamWatcher(this, name => { if (name is not nameof(CameraStream.Frame)) Refresh(); });
        _session.Changed += () =>
        {
            _view.Bind(_session.Stream);
            _chip.Bind(_session.Stream);
            _watcher.Bind(_session.Stream);
        };
        Loaded += (_, _) => _session.Attach(_camera, hd: false, _env.Pool);
        Unloaded += (_, _) => _session.Detach();
        Refresh();
    }

    public void SetWidth(double width)
    {
        Width = width;
        _videoArea.Height = Math.Round((width - 2) * 9 / 16);
    }

    private static UIElement Spaced(TextBlock text)
    {
        text.Margin = new Thickness(6, 0, 0, 0);
        return text;
    }

    private Button PinButton(Camera camera) => Pinning.PinMenu.Button(camera, _env.Pins, 24, overlay: false);

    private UIElement Negotiating()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var spinner = new Spinner(20, Palette.Warning) { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(spinner);
        var title = UI.Text("Negociando RTSP…", 12, FontWeights.SemiBold, Palette.WarningText);
        title.Margin = new Thickness(0, 8, 0, 0);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        var endpoint = UI.Text(_camera.Endpoint, 10.5, foreground: Palette.White(0.5), mono: true);
        endpoint.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(endpoint);
        return stack;
    }

    private void Refresh()
    {
        var stream = _watcher.Stream;
        var status = stream?.Status ?? StreamStatus.Idle;
        _dot.Fill = StatusInfo.Color(status, ChipStyle.Tinted);

        var info = stream?.Info;
        _fps.Text = info is { HasVideo: true } && status == StreamStatus.Live ? $"{info.Value.FpsText} FPS" : "";

        var showOverlay = stream is { Frame: not null } && status != StreamStatus.Failed;
        _liveOverlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
        _resolutionTag.Child = info is { HasVideo: true } i
            ? OverlayTag.Create(i.Resolution + (i.Codec.Length == 0 ? "" : $" • {i.Codec}"))
            : null;
    }
}
