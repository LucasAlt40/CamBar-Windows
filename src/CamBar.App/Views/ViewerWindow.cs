using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CamBar.Controls;
using CamBar.Core;
using CamBar.Interop;
using CamBar.Pinning;
using CamBar.Services;
using CamBar.Streaming;
using CamBar.Theme;

namespace CamBar.Views;

/// <summary>Expanded camera window: full-bleed video, header on hover, sound, snapshot, PTZ.</summary>
public sealed class ViewerWindow : DarkWindow
{
    private readonly AppEnvironment _env;
    private readonly StreamSession _session = new();
    private readonly AudioPlayer _audio = new();
    private readonly PtzController _ptz;
    private readonly StreamView _view = new() { ShowsStatus = false };
    private readonly Grid _header = new() { Opacity = 1 };
    private readonly TextBlock _name = UI.Text("", 13, FontWeights.SemiBold);
    private readonly StatusChip _status = new();
    private readonly TelemetryChip _telemetry = new();
    private readonly ActionPill _actions;
    private readonly ContentControl _ptzHost = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Border _toast;
    private readonly TextBlock _toastText = UI.Text("", 11, FontWeights.Medium);
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly Button _hdButton;
    private readonly Button _sdButton;
    private Camera _camera;
    private bool _isHd = true;
    private bool _hovering = true;
    private bool _fullScreen;
    private (WindowStyle Style, ResizeMode Resize) _restore;

    public ViewerWindow(AppEnvironment env, Camera camera)
    {
        _env = env;
        _camera = camera;
        _ptz = new PtzController(env.PtzLog);
        _actions = new ActionPill(_audio, ToggleAudio, TakeSnapshot);

        Title = camera.Name;
        Width = 1016;
        Height = 602;
        MinWidth = 656;
        MinHeight = 399;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;

        (_hdButton, _sdButton) = (QualityButton(true), QualityButton(false));
        _toast = UI.Capsule(_toastText, Palette.Black(0.7), Palette.White(0.12), new Thickness(12, 6, 12, 6));
        _toast.Visibility = Visibility.Collapsed;
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.VerticalAlignment = VerticalAlignment.Top;
        _toast.Margin = new Thickness(0, 56, 0, 0);

        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(_view);
        _actions.HorizontalAlignment = HorizontalAlignment.Left;
        _actions.VerticalAlignment = VerticalAlignment.Bottom;
        _actions.Margin = new Thickness(14);
        root.Children.Add(_actions);
        _ptzHost.Margin = new Thickness(16);
        root.Children.Add(_ptzHost);
        root.Children.Add(_toast);
        BuildHeader();
        root.Children.Add(_header);
        Content = root;

        root.MouseEnter += (_, _) => SetHover(true);
        root.MouseLeave += (_, _) => SetHover(false);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast.Visibility = Visibility.Collapsed; };
        PreviewKeyDown += OnKeyDown;

        SourceInitialized += (_, _) => WindowEffects.LockAspectRatio(this, 16.0 / 9.0);
        Loaded += (_, _) => Appear();
        Closed += (_, _) => Disappear();

        _session.Changed += () =>
        {
            _view.Bind(_session.Stream);
            _status.Bind(_session.Stream);
            _telemetry.Bind(_session.Stream);
            _actions.Bind(_session.Stream);
        };
        _ptz.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PtzController.State)) RefreshPtz(); };
        env.Store.Changed += OnStoreChanged;
        Closed += (_, _) => env.Store.Changed -= OnStoreChanged;
        _name.Text = camera.Name;
        UpdateQuality();
    }

    // MARK: Lifecycle

    private void Appear()
    {
        _session.Attach(_camera, _isHd, _env.Pool);
        _ptz.Detect(_camera, _env.Store.PasswordFor(_camera.Id));
    }

    private void Disappear()
    {
        _ptz.Stop();
        _audio.Stop();
        _session.Detach();
    }

    /// <summary>Follows edits made in settings while the window is open (name, host, credentials).</summary>
    private void OnStoreChanged(CameraChange change)
    {
        if (change is not CameraChange.Updated updated || updated.Camera.Id != _camera.Id) return;
        _camera = updated.Camera;
        Title = _camera.Name;
        _name.Text = _camera.Name;
        _audio.Stop();   // audio runs its own ffmpeg with the old address and credentials
    }

    // MARK: Header

    private void BuildHeader()
    {
        _header.VerticalAlignment = VerticalAlignment.Top;
        _header.Height = 70;
        _header.Background = new LinearGradientBrush(Color.FromArgb(0xC0, 0, 0, 0), Colors.Transparent, 90);

        var row = new DockPanel { Margin = new Thickness(16, 0, 14, 0), Height = 46, VerticalAlignment = VerticalAlignment.Top, LastChildFill = false };

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        _name.Margin = new Thickness(0, 0, 12, 0);
        left.Children.Add(_name);
        left.Children.Add(_status);
        row.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        var picker = UI.Capsule(new StackPanel { Orientation = Orientation.Horizontal, Children = { _hdButton, _sdButton } },
                                Palette.Black(0.45), Palette.Hairline, new Thickness(2));
        right.Children.Add(picker);
        var pin = PinMenu.Button(_camera, _env.Pins, 28, overlay: false);
        pin.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(pin);
        var full = UI.IconButton(UI.Glyph.Expand, "Tela cheia (F11)", ToggleFullScreen, 28, 11, Palette.White(0.8), Palette.White(0.08), Palette.Hairline, 8);
        full.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(full);
        row.Children.Add(right);

        // telemetry sits in the middle of whatever space is left
        _telemetry.HorizontalAlignment = HorizontalAlignment.Center;
        _telemetry.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_telemetry);
        DockPanel.SetDock(_telemetry, Dock.Left);
        _telemetry.Margin = new Thickness(24, 0, 0, 0);

        _header.Children.Add(row);
    }

    private Button QualityButton(bool hd)
    {
        var label = UI.Text(hd ? "HD" : "SD", 11, FontWeights.Bold, mono: true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var face = UI.Capsule(label, Brushes.Transparent, Brushes.Transparent, new Thickness(0, 0, 0, 0));
        face.Width = 40;
        face.Height = 24;
        var button = new Button { Content = face, Style = (Style)Application.Current.FindResource("BareButton"), Focusable = false, Tag = label };
        button.Click += (_, _) =>
        {
            _isHd = hd;
            _session.SetHd(hd);
            UpdateQuality();
        };
        return button;
    }

    private void UpdateQuality()
    {
        foreach (var (button, hd) in new[] { (_hdButton, true), (_sdButton, false) })
        {
            var selected = _isHd == hd;
            var face = (Border)button.Content;
            face.Background = selected ? Palette.Accent : Brushes.Transparent;
            ((TextBlock)button.Tag).Foreground = selected ? Brushes.Black : Palette.White(0.6);
        }
    }

    private void SetHover(bool hovering)
    {
        _hovering = hovering;
        _header.IsHitTestVisible = hovering;
        _header.BeginAnimation(OpacityProperty, new DoubleAnimation(hovering ? 1 : 0, TimeSpan.FromMilliseconds(200)));
    }

    // MARK: Actions

    private void RefreshPtz()
    {
        switch (_ptz.State)
        {
            case PtzState.Available:
                _ptzHost.Content = new PtzPad(_ptz);
                break;
            case PtzState.Unavailable:
                _ptzHost.Content = UI.Capsule(UI.Text("Sem suporte PTZ", 11, foreground: Palette.White(0.7)),
                                              Palette.Black(0.5), Palette.White(0.12), new Thickness(12, 6, 12, 6));
                break;
            default:
                _ptzHost.Content = null;
                break;
        }
    }

    private void ToggleAudio()
    {
        if (_audio.IsPlaying) _audio.Stop();
        else _audio.Start(_camera, _env.Store.PasswordFor(_camera.Id));
    }

    private void TakeSnapshot()
    {
        if (_session.Stream?.Frame is not { } frame) return;
        try
        {
            Snapshot.Save(frame, _camera.Name);
            ShowToast("Foto salva em Imagens\\CamBar");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowToast("Não foi possível salvar a foto");
        }
    }

    private void ShowToast(string text)
    {
        _toastText.Text = text;
        _toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ToggleFullScreen()
    {
        if (_fullScreen)
        {
            WindowState = WindowState.Normal;
            WindowStyle = _restore.Style;
            ResizeMode = _restore.Resize;
        }
        else
        {
            _restore = (WindowStyle, ResizeMode);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        _fullScreen = !_fullScreen;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Alt))
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _fullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    /// <summary>Sound + snapshot pill.</summary>
    private sealed class ActionPill : Border
    {
        private readonly AudioPlayer _audio;
        private readonly Button _sound;
        private readonly TextBlock _soundIcon = UI.Icon(UI.Glyph.Mute, 13);
        private readonly TextBlock _soundText = UI.Text("MUDO", 11, FontWeights.Bold, mono: true);
        private readonly Border _divider = UI.Rule(vertical: true, brush: Palette.White(0.15));
        private readonly Button _snapshot;
        private readonly StreamWatcher _watcher;

        public ActionPill(AudioPlayer audio, Action toggleAudio, Action snapshot)
        {
            _audio = audio;
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            var soundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0) };
            _soundIcon.Margin = new Thickness(0, 0, 6, 0);
            soundRow.Children.Add(_soundIcon);
            soundRow.Children.Add(_soundText);
            _sound = new Button { Content = soundRow, Style = (Style)Application.Current.FindResource("BareButton"), Height = 30, Focusable = false };
            _sound.Click += (_, _) => toggleAudio();
            row.Children.Add(_sound);

            _divider.Height = 16;
            row.Children.Add(_divider);

            var camera = UI.Icon(UI.Glyph.Camera, 13, Palette.White(0.85));
            camera.Margin = new Thickness(12, 0, 12, 0);
            _snapshot = new Button { Content = camera, Style = (Style)Application.Current.FindResource("BareButton"), Height = 30, ToolTip = "Salvar foto", Focusable = false };
            _snapshot.Click += (_, _) => snapshot();
            row.Children.Add(_snapshot);

            Child = row;
            CornerRadius = new CornerRadius(999);
            Background = Palette.Black(0.5);
            BorderBrush = Palette.White(0.12);
            BorderThickness = new Thickness(1);

            audio.PropertyChanged += (_, _) => Refresh();
            _watcher = new StreamWatcher(this, name => { if (name is null or nameof(CameraStream.Info) or nameof(CameraStream.Frame)) Refresh(); });
            Refresh();
        }

        public void Bind(CameraStream? stream) => _watcher.Bind(stream);

        private void Refresh()
        {
            var hasAudio = _watcher.Stream?.Info?.HasAudio == true;
            _sound.Visibility = _divider.Visibility = hasAudio ? Visibility.Visible : Visibility.Collapsed;
            var playing = _audio.IsPlaying;
            _soundIcon.Text = playing ? UI.Glyph.Volume : UI.Glyph.Mute;
            _soundText.Text = playing ? "ON" : "MUDO";
            var tint = playing ? Palette.Accent : Palette.White(0.75);
            _soundIcon.Foreground = _soundText.Foreground = tint;
            _sound.ToolTip = playing ? "Silenciar" : "Ouvir o som da câmera";
            _snapshot.IsEnabled = _watcher.Stream?.Frame is not null;
        }
    }
}
