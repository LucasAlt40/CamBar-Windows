using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CamBar.Controls;
using CamBar.Core;
using CamBar.Theme;

namespace CamBar.Views;

/// <summary>"Gerenciar câmeras": camera list on the left, connection form with live preview on the right.</summary>
public sealed class SettingsWindow : DarkWindow
{
    private const double WideBreakpoint = 900;

    private readonly AppEnvironment _env;
    private readonly SettingsViewModel _model;
    private readonly StackPanel _rows = new() { Margin = new Thickness(8) };
    private readonly Grid _detail = new() { Margin = new Thickness(24) };
    private readonly FrameworkElement _formColumn;
    private readonly FrameworkElement _sideColumn;
    private readonly Border _previewHost = new() { Background = Brushes.Black };
    private readonly CameraFeed _previewFeed = new();
    private readonly UIElement _previewPlaceholder;
    private readonly Grid _headerBadge = new();
    private readonly System.Windows.Shapes.Ellipse _badgeDot = UI.Dot(Palette.Gray, 7);
    private readonly TextBlock _badgeText = UI.Text("OFFLINE", 10.5, FontWeights.SemiBold, mono: true);
    private readonly PasswordBox _passwordBox = new() { Style = (Style)Application.Current.FindResource("FieldPasswordBox"), Foreground = Brushes.White, FontSize = 12 };
    private readonly TextBox _passwordText = Field("", mono: true);
    private readonly TextBlock _showIcon = UI.Icon(UI.Glyph.Eye, 11, Palette.White(0.5));
    private readonly Dictionary<RtspTransport, (Border Face, TextBlock Label)> _transports = [];
    private readonly TextBlock _signalTitle = UI.Text("", 12, FontWeights.Medium);
    private readonly TextBlock _signalUrl = UI.Text("", 10.5, foreground: Palette.White(0.5), mono: true);
    private readonly Border _signalIconBox = new();
    private readonly TextBlock _signalIcon = UI.Icon(UI.Glyph.Bolt, 13);
    private readonly Button _testButton;
    private readonly Button _deleteButton;
    private readonly Button _saveButton;
    private readonly Border _saveFace = new();
    private readonly TextBlock _saveLabel = UI.Text("Salvar", 12, FontWeights.SemiBold);
    private readonly TextBlock _sectionCount = UI.Text("", 10.5, FontWeights.Medium, Palette.White(0.45));
    private bool _isWide;

    public SettingsWindow(AppEnvironment env)
    {
        _env = env;
        _model = new SettingsViewModel(env.Store, env.Checker);
        Title = "Gerenciar câmeras";
        Width = 916;
        Height = 679;
        MinWidth = 776;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        (_testButton, _deleteButton, _saveButton) = (UI.PillButton("Testar Sinal", _model.TestSignal), BuildDelete(), BuildSave());
        _previewPlaceholder = BuildPlaceholder();
        _formColumn = BuildFormColumn();
        _sideColumn = BuildSideColumn();

        var root = new DockPanel();
        var title = BuildTitleBar();
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
        var rule = UI.Rule();
        DockPanel.SetDock(rule, Dock.Top);
        root.Children.Add(rule);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = BuildSidebar();
        body.Children.Add(sidebar);
        var divider = UI.Rule(vertical: true);
        Grid.SetColumn(divider, 1);
        body.Children.Add(divider);
        var detail = BuildDetail();
        Grid.SetColumn(detail, 2);
        body.Children.Add(detail);
        root.Children.Add(body);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _model.StartNewCamera();
                e.Handled = true;
            }
        };
        _model.PropertyChanged += OnModelChanged;
        env.Store.PropertyChanged += (_, _) => RefreshSidebar();
        env.Reachability.PropertyChanged += (_, _) => { RefreshSidebar(); RefreshBadge(); };
        Closed += (_, _) => _previewFeed.IsActive = false;

        _model.Start();
        RefreshAll();
    }

    private ScanWindow? _scanner;

    private void OpenScanner()
    {
        if (_scanner is { IsVisible: true })
        {
            _scanner.Activate();
            return;
        }
        _scanner = new ScanWindow(_env, device => _model.StartNewCamera(device)) { Owner = this };
        _scanner.Closed += (_, _) => _scanner = null;
        _scanner.Show();
    }

    // MARK: Layout

    private UIElement BuildTitleBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var icon = UI.Icon(UI.Glyph.Video, 12, Palette.Accent);
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        row.Children.Add(UI.Text("CamBar — Gerenciar Câmeras", 13, FontWeights.SemiBold));

        var add = UI.IconButton(UI.Glyph.Add, "Adicionar câmera (Ctrl+N)", _model.StartNewCamera, 26, 13, Palette.White(0.65), Brushes.Transparent, Brushes.Transparent, 8);
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.Margin = new Thickness(0, 0, 12, 0);

        var grid = new Grid { Height = 44 };
        grid.Children.Add(row);
        grid.Children.Add(add);
        return new Border { Child = grid, Background = Palette.TitleBar };
    }

    private UIElement BuildSidebar()
    {
        var search = Field("Buscar câmera...", right: false);
        search.SetBinding(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.Search)) { Source = _model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var searchRow = new DockPanel();
        var glass = UI.Icon(UI.Glyph.Search, 11, Palette.White(0.45));
        glass.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(glass, Dock.Left);
        searchRow.Children.Add(glass);
        searchRow.Children.Add(search);
        var searchBox = UI.Card(searchRow, Palette.White(0.05), 8, Palette.Hairline, new Thickness(10, 6, 10, 6));
        searchBox.Margin = new Thickness(12);

        var add = new Button { Style = (Style)Application.Current.FindResource("BareButton"), Focusable = false };
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        addRow.Children.Add(UI.Icon(UI.Glyph.Add, 11, Brushes.White));
        var addText = UI.Text("Adicionar Nova Câmera", 12, FontWeights.Medium);
        addText.Margin = new Thickness(8, 0, 8, 0);
        addRow.Children.Add(addText);
        addRow.Children.Add(UI.Card(UI.Text("Ctrl+N", 10, foreground: Palette.White(0.5), mono: true), Palette.Black(0.4), 5, Brushes.Transparent, new Thickness(6, 2, 6, 2)));
        add.Content = UI.Card(addRow, Palette.White(0.05), 9, Palette.White(0.1), new Thickness(0, 8, 0, 8));
        add.Click += (_, _) => _model.StartNewCamera();
        add.Margin = new Thickness(12, 0, 12, 12);

        var scan = new Button { Style = (Style)Application.Current.FindResource("BareButton"), Focusable = false, ToolTip = "Encontrar câmeras na sua rede local" };
        var scanRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        scanRow.Children.Add(UI.Icon(UI.Glyph.Search, 11, Palette.Accent));
        var scanText = UI.Text("Procurar câmeras na rede", 12, FontWeights.Medium, Palette.Accent);
        scanText.Margin = new Thickness(8, 0, 0, 0);
        scanRow.Children.Add(scanText);
        scan.Content = UI.Card(scanRow, Palette.With(Palette.Accent, 0.08), 9, Palette.With(Palette.Accent, 0.3), new Thickness(0, 8, 0, 8));
        scan.Click += (_, _) => OpenScanner();
        scan.Margin = new Thickness(12, 12, 12, 8);

        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var dock = new DockPanel { Background = Palette.Sidebar };
        searchBox.SetValue(DockPanel.DockProperty, Dock.Top);
        dock.Children.Add(searchBox);
        var topRule = UI.Rule();
        DockPanel.SetDock(topRule, Dock.Top);
        dock.Children.Add(topRule);
        DockPanel.SetDock(add, Dock.Bottom);
        dock.Children.Add(add);
        DockPanel.SetDock(scan, Dock.Bottom);
        dock.Children.Add(scan);
        var bottomRule = UI.Rule();
        DockPanel.SetDock(bottomRule, Dock.Bottom);
        dock.Children.Add(bottomRule);
        dock.Children.Add(scroll);
        return dock;
    }

    private UIElement BuildDetail()
    {
        _detail.Children.Add(_formColumn);
        _detail.Children.Add(_sideColumn);
        var scroll = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroll.SizeChanged += (_, e) => ArrangeDetail(e.NewSize.Width);

        var actions = BuildActionBar();
        var dock = new DockPanel { Background = Palette.Window };
        DockPanel.SetDock(actions, Dock.Bottom);
        dock.Children.Add(actions);
        var rule = UI.Rule();
        DockPanel.SetDock(rule, Dock.Bottom);
        dock.Children.Add(rule);
        dock.Children.Add(scroll);
        return dock;
    }

    /// <summary>Wide: form on the left, preview and signal test on the right. Narrow: everything stacked.</summary>
    private void ArrangeDetail(double width)
    {
        var wide = width >= WideBreakpoint;
        if (wide == _isWide && _detail.ColumnDefinitions.Count > 0) return;
        _isWide = wide;
        _detail.ColumnDefinitions.Clear();
        _detail.RowDefinitions.Clear();
        _detail.MaxWidth = wide ? double.PositiveInfinity : 720;
        if (wide)
        {
            _detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Min(Math.Max(width * 0.4, 440), 620)) });
            _detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            _detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_formColumn, 0); Grid.SetRow(_formColumn, 0);
            Grid.SetColumn(_sideColumn, 2); Grid.SetRow(_sideColumn, 0);
            _sideColumn.Margin = new Thickness(0);
        }
        else
        {
            _detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_formColumn, 0); Grid.SetRow(_formColumn, 0);
            Grid.SetColumn(_sideColumn, 0); Grid.SetRow(_sideColumn, 1);
            _sideColumn.Margin = new Thickness(0, 18, 0, 0);
        }
    }

    // MARK: Form

    private FrameworkElement BuildFormColumn()
    {
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        column.Children.Add(BuildDetailHeader());

        var form = new StackPanel();
        form.Children.Add(FormRow("Nome da Câmera", Bind(Field("Ex: Quintal"), nameof(SettingsViewModel.Name))));
        form.Children.Add(FormRow("IP da câmera", Bind(Field("192.168.x.x", mono: true), nameof(SettingsViewModel.Host))));
        form.Children.Add(FormRow("Porta RTSP", Bind(Field("554", mono: true), nameof(SettingsViewModel.PortText))));
        form.Children.Add(FormRow("Caminho RTSP", Bind(Field("/onvif1", mono: true), nameof(SettingsViewModel.Path))));
        form.Children.Add(FormRow("Porta ONVIF (PTZ)", Bind(Field("automática", mono: true), nameof(SettingsViewModel.OnvifPortText))));
        form.Children.Add(FormRow("Usuário", Bind(Field("Nome de usuário do feed"), nameof(SettingsViewModel.Username))));
        form.Children.Add(FormRow("Senha", BuildPasswordField()));
        form.Children.Add(FormRow("Transporte", BuildTransportPicker(), showsDivider: false));
        var card = UI.Card(form, Palette.Panel, 12, Palette.Hairline);
        card.Margin = new Thickness(0, 18, 0, 0);
        column.Children.Add(card);

        var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 12, 4, 0) };
        var lockIcon = UI.Icon(UI.Glyph.Lock, 10, Palette.With(Palette.Accent, 0.8));
        lockIcon.Margin = new Thickness(0, 0, 7, 0);
        note.Children.Add(lockIcon);
        note.Children.Add(UI.Text("A senha fica salva de forma segura no Gerenciador de Credenciais do Windows.", 11, foreground: Palette.White(0.5)));
        ((TextBlock)note.Children[1]).TextWrapping = TextWrapping.Wrap;
        ((TextBlock)note.Children[1]).MaxWidth = 380;
        column.Children.Add(note);
        return column;
    }

    private UIElement BuildDetailHeader()
    {
        var dock = new DockPanel();

        _badgeDot.Margin = new Thickness(0, 0, 7, 0);
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
        badgeRow.Children.Add(_badgeDot);
        badgeRow.Children.Add(_badgeText);
        var badge = UI.Card(badgeRow, Palette.White(0.04), 8, Palette.Hairline, new Thickness(10, 6, 10, 6));
        badge.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(badge, Dock.Right);
        dock.Children.Add(badge);

        var texts = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(UI.Text("Configuração de Conexão", 18, FontWeights.SemiBold));
        var tag = UI.Capsule(UI.Text("RTSP / ONVIF", 10.5, foreground: Palette.Accent), Palette.With(Palette.Accent, 0.1),
                             Palette.With(Palette.Accent, 0.25), new Thickness(8, 2, 8, 2));
        tag.Margin = new Thickness(8, 0, 0, 0);
        tag.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(tag);
        texts.Children.Add(titleRow);
        var sub = UI.Text("Parâmetros de rede e credenciais de stream local", 11.5, foreground: Palette.White(0.5));
        sub.Margin = new Thickness(0, 4, 0, 0);
        texts.Children.Add(sub);
        dock.Children.Add(texts);
        return dock;
    }

    private UIElement BuildPasswordField()
    {
        _passwordBox.PasswordChanged += (_, _) => _model.Password = _passwordBox.Password;
        _passwordText.SetBinding(TextBox.TextProperty, new Binding(nameof(SettingsViewModel.Password)) { Source = _model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        _passwordText.Visibility = Visibility.Collapsed;

        var toggle = new Button { Content = _showIcon, Style = (Style)Application.Current.FindResource("BareButton"), Focusable = false, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Mostrar ou ocultar a senha" };
        toggle.Click += (_, _) => _model.ShowsPassword = !_model.ShowsPassword;
        DockPanel.SetDock(toggle, Dock.Right);

        var dock = new DockPanel();
        dock.Children.Add(toggle);
        var fields = new Grid();
        _passwordBox.HorizontalContentAlignment = HorizontalAlignment.Right;
        fields.Children.Add(_passwordBox);
        fields.Children.Add(_passwordText);
        dock.Children.Add(fields);
        return dock;
    }

    private UIElement BuildTransportPicker()
    {
        var labels = new (RtspTransport Value, string Text, string Tip)[]
        {
            (RtspTransport.Auto, "Auto", "Automático"),
            (RtspTransport.Udp, "UDP", "UDP (Baixa Latência)"),
            (RtspTransport.Tcp, "TCP", "TCP (Confiável)"),
            (RtspTransport.Http, "HTTP", "HTTP Tunneling"),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (value, text, tip) in labels)
        {
            var label = UI.Text(text, 11.5, FontWeights.Medium);
            var face = UI.Card(label, Brushes.Transparent, 7, Brushes.Transparent, new Thickness(12, 5, 12, 5));
            var button = new Button { Content = face, Style = (Style)Application.Current.FindResource("BareButton"), ToolTip = tip, Focusable = false, Margin = new Thickness(2, 0, 0, 0) };
            var transport = value;
            button.Click += (_, _) => _model.Transport = transport;
            _transports[value] = (face, label);
            row.Children.Add(button);
        }
        return UI.Card(row, Palette.Black(0.3), 9, Palette.Hairline, new Thickness(2));
    }

    private static TextBox Field(string placeholder, bool mono = false, bool right = true)
    {
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("FieldTextBox"),
            Tag = placeholder,
            FontSize = 12,
            FontFamily = mono ? Palette.MonoFont : Palette.UiFont,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        return box;
    }

    private TextBox Bind(TextBox box, string property)
    {
        box.SetBinding(TextBox.TextProperty, new Binding(property) { Source = _model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        return box;
    }

    private static UIElement FormRow(string label, UIElement content, bool showsDivider = true)
    {
        // label | flexible gap | content that grows up to 300 px and stays against the right edge
        // (a stretched element with a MaxWidth would be centered instead)
        var grid = new Grid { Margin = new Thickness(16, 9, 16, 9) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.001, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 300 });
        var text = UI.Text(label, 12, FontWeights.Medium, Palette.White(0.78));
        grid.Children.Add(text);
        if (content is FrameworkElement element && element is not (TextBox or DockPanel))
            element.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(content, 2);
        grid.Children.Add(content);

        var stack = new StackPanel();
        stack.Children.Add(grid);
        if (showsDivider) stack.Children.Add(UI.Rule(brush: Palette.White(0.06)));
        return stack;
    }

    // MARK: Preview and signal test

    private FrameworkElement BuildSideColumn()
    {
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

        var previewTitle = UI.Text("PRÉ-VISUALIZAÇÃO", 10.5, FontWeights.Medium, Palette.White(0.45));
        previewTitle.Margin = new Thickness(0, 0, 0, 10);
        var previewBox = new Grid();
        previewBox.Children.Add(_previewFeed);
        previewBox.Children.Add(_previewPlaceholder);
        _previewHost.Child = previewBox;
        _previewHost.CornerRadius = new CornerRadius(12);
        _previewHost.BorderBrush = Palette.Hairline;
        _previewHost.BorderThickness = new Thickness(1);
        UI.RoundClip(_previewHost, 12);
        _previewHost.SizeChanged += (_, e) => _previewHost.Height = Math.Round(e.NewSize.Width * 9 / 16);

        var card = new StackPanel();
        card.Children.Add(previewTitle);
        card.Children.Add(_previewHost);
        column.Children.Add(UI.Card(card, Palette.Panel, 14, Palette.Hairline, new Thickness(12)));
        column.Children.Add(BuildSignalCard());
        ((FrameworkElement)column.Children[1]).Margin = new Thickness(0, 18, 0, 0);
        return column;
    }

    private UIElement BuildPlaceholder()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = UI.Icon(UI.Glyph.Video, 22, Palette.White(0.4));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        var text = UI.Text("Salve a câmera para ver a imagem ao vivo", 11.5, foreground: Palette.White(0.5));
        text.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(text);
        var host = new Grid { Background = Brushes.Black };
        host.Children.Add(stack);
        return host;
    }

    private UIElement BuildSignalCard()
    {
        _signalIconBox.Child = _signalIcon;
        _signalIconBox.Width = _signalIconBox.Height = 32;
        _signalIconBox.CornerRadius = new CornerRadius(8);
        _signalIconBox.BorderThickness = new Thickness(1);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        texts.Children.Add(_signalTitle);
        _signalUrl.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(_signalUrl);

        var dock = new DockPanel();
        DockPanel.SetDock(_signalIconBox, Dock.Left);
        dock.Children.Add(_signalIconBox);
        DockPanel.SetDock(_testButton, Dock.Right);
        dock.Children.Add(_testButton);
        dock.Children.Add(texts);
        return UI.Card(dock, Palette.White(0.03), 12, Palette.White(0.05), new Thickness(12));
    }

    // MARK: Actions

    private Button BuildDelete()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 10, 6) };
        var icon = UI.Icon(UI.Glyph.Delete, 12, Palette.DangerSoft);
        icon.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(icon);
        row.Children.Add(UI.Text("Excluir Câmera", 12, foreground: Palette.DangerSoft));
        var button = new Button { Content = row, Style = (Style)Application.Current.FindResource("GhostButton"), Focusable = false };
        button.Click += (_, _) =>
        {
            var answer = MessageBox.Show(this, "Excluir esta câmera?", "CamBar", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes) _model.DeleteSelected();
        };
        return button;
    }

    private Button BuildSave()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var check = UI.Icon(UI.Glyph.Check, 11, Brushes.White);
        check.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(check);
        row.Children.Add(_saveLabel);
        _saveFace.Child = row;
        _saveFace.Background = Palette.AccentStrong;
        _saveFace.CornerRadius = new CornerRadius(9);
        _saveFace.BorderBrush = Palette.With(Palette.Accent, 0.45);
        _saveFace.BorderThickness = new Thickness(1);
        _saveFace.Padding = new Thickness(18, 7, 18, 7);
        var button = new Button { Content = _saveFace, Style = (Style)Application.Current.FindResource("BareButton"), IsDefault = true, Focusable = false };
        button.Click += (_, _) => _model.Save();
        return button;
    }

    private UIElement BuildActionBar()
    {
        var dock = new DockPanel { Margin = new Thickness(24, 14, 24, 14), LastChildFill = false };
        DockPanel.SetDock(_deleteButton, Dock.Left);
        dock.Children.Add(_deleteButton);
        DockPanel.SetDock(_saveButton, Dock.Right);
        dock.Children.Add(_saveButton);
        var cancel = UI.PillButton("Cancelar", _model.Cancel);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(cancel, Dock.Right);
        dock.Children.Add(cancel);
        return new Border { Child = dock, Background = Palette.With(Palette.TitleBar, 0.6) };
    }

    // MARK: Refresh

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.Draft):
                SyncPassword();
                RefreshAll();
                break;
            case nameof(SettingsViewModel.Password):
                SyncPassword();
                break;
            case nameof(SettingsViewModel.ShowsPassword):
                RefreshPassword();
                break;
            case nameof(SettingsViewModel.Transport):
                RefreshTransport();
                break;
            case nameof(SettingsViewModel.CanSave):
                RefreshActions();
                break;
            case nameof(SettingsViewModel.JustSaved):
                RefreshActions();
                break;
            case nameof(SettingsViewModel.SignalTestState):
            case nameof(SettingsViewModel.DisplayUrl):
                RefreshSignal();
                break;
            case nameof(SettingsViewModel.IsNew):
            case nameof(SettingsViewModel.SavedCamera):
                RefreshActions();
                RefreshPreview();
                RefreshSidebar();
                RefreshBadge();
                break;
            case nameof(SettingsViewModel.SelectedId):
            case nameof(SettingsViewModel.Name):
            case nameof(SettingsViewModel.Host):
            case nameof(SettingsViewModel.Search):
            case nameof(SettingsViewModel.FilteredCameras):
                RefreshSidebar();
                break;
        }
    }

    private void RefreshAll()
    {
        RefreshSidebar();
        RefreshPassword();
        RefreshTransport();
        RefreshActions();
        RefreshSignal();
        RefreshPreview();
        RefreshBadge();
    }

    private void SyncPassword()
    {
        if (_passwordBox.Password != _model.Password) _passwordBox.Password = _model.Password;
    }

    private void RefreshPassword()
    {
        SyncPassword();
        _passwordBox.Visibility = _model.ShowsPassword ? Visibility.Collapsed : Visibility.Visible;
        _passwordText.Visibility = _model.ShowsPassword ? Visibility.Visible : Visibility.Collapsed;
        _showIcon.Text = _model.ShowsPassword ? UI.Glyph.EyeOff : UI.Glyph.Eye;
    }

    private void RefreshTransport()
    {
        foreach (var (transport, (face, label)) in _transports)
        {
            var selected = _model.Transport == transport;
            face.Background = selected ? Palette.White(0.16) : Brushes.Transparent;
            label.Foreground = selected ? Brushes.White : Palette.White(0.6);
        }
    }

    private void RefreshActions()
    {
        _deleteButton.IsEnabled = !_model.IsNew;
        _saveButton.IsEnabled = _model.CanSave;
        _saveFace.Opacity = _model.CanSave ? 1 : 0.45;
        _saveLabel.Text = _model.JustSaved ? "Salvo!" : "Salvar";
        _testButton.IsEnabled = _model.Host.Length > 0 && _model.SignalTestState != SignalTest.Testing;
    }

    private void RefreshSignal()
    {
        var (glyph, title, tint) = _model.SignalTestState switch
        {
            SignalTest.Testing => (UI.Glyph.Bolt, "Testando…", Palette.Warning),
            SignalTest.Reachable => (UI.Glyph.Check, $"A câmera respondeu na porta {_model.PortText}", Palette.Accent),
            SignalTest.Unreachable => (UI.Glyph.Close, "Sem resposta no IP e porta informados", Palette.Danger),
            _ => (UI.Glyph.Bolt, "Pronto para testar", Palette.Accent),
        };
        _signalIcon.Text = glyph;
        _signalIcon.Foreground = tint;
        _signalIconBox.Background = Palette.With(tint, 0.1);
        _signalIconBox.BorderBrush = Palette.With(tint, 0.25);
        _signalTitle.Text = title;
        _signalUrl.Text = _model.DisplayUrl;
        RefreshActions();
    }

    private void RefreshPreview()
    {
        var saved = _model.SavedCamera;
        _previewFeed.Camera = saved;
        _previewPlaceholder.Visibility = saved is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshBadge()
    {
        var online = _env.Reachability.IsOnline(_model.Draft.Id);
        _badgeDot.Fill = online ? Palette.Accent : Palette.Gray;
        _badgeText.Text = online ? "ONLINE" : "OFFLINE";
        _badgeText.Foreground = online ? Palette.Accent : Palette.White(0.5);
    }

    private void RefreshSidebar()
    {
        var store = _env.Store;
        _rows.Children.Clear();
        _sectionCount.Text = $"CÂMERAS REGISTRADAS ({store.Cameras.Count})";
        if (_sectionCount.Parent is Panel old) old.Children.Remove(_sectionCount);
        _sectionCount.Margin = new Thickness(8, 6, 8, 6);
        _rows.Children.Add(_sectionCount);

        if (_model.IsNew)
            _rows.Children.Add(Row(_model.Draft, store.Cameras.Count + 1, isSelected: true, online: null, onClick: null));

        foreach (var camera in _model.FilteredCameras)
        {
            var captured = camera;
            _rows.Children.Add(Row(camera, store.NumberOf(camera) ?? 0, !_model.IsNew && camera.Id == _model.SelectedId,
                                   _env.Reachability.IsOnline(camera.Id), () => _model.Select(captured)));
        }
    }

    private static UIElement Row(Camera camera, int number, bool isSelected, bool? online, Action? onClick)
    {
        var icon = UI.Card(UI.Icon(UI.Glyph.Video, 12, isSelected ? Palette.Accent : Palette.White(0.55)),
                           isSelected ? Palette.With(Palette.Accent, 0.2) : Palette.White(0.05), 7,
                           isSelected ? Palette.With(Palette.Accent, 0.4) : Palette.White(0.05));
        icon.Width = icon.Height = 28;

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var name = UI.Text(camera.Name.Length == 0 ? "Nova câmera" : camera.Name, 12, isSelected ? FontWeights.SemiBold : FontWeights.Medium);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxWidth = 120;
        title.Children.Add(name);
        if (online is { } isOnline)
        {
            var dot = UI.Dot(isOnline ? Palette.Accent : Palette.Gray);
            dot.Margin = new Thickness(6, 0, 0, 0);
            title.Children.Add(dot);
        }
        var texts = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(UI.Text(camera.Host.Length == 0 ? "sem IP" : camera.Endpoint, 10, foreground: isSelected ? Palette.With(Palette.Accent, 0.9) : Palette.White(0.45), mono: true));

        var tag = UI.Card(UI.Text($"cam {number}", 10, foreground: isSelected ? Palette.Accent : Palette.White(0.45), mono: true),
                          isSelected ? Palette.With(Palette.Accent, 0.18) : Palette.White(0.05), 5, Brushes.Transparent, new Thickness(6, 2, 6, 2));
        tag.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(tag, Dock.Right);

        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(tag);
        dock.Children.Add(texts);

        var face = UI.Card(dock, isSelected ? Palette.With(Palette.Accent, 0.13) : Brushes.Transparent, 10,
                           isSelected ? Palette.With(Palette.Accent, 0.3) : Brushes.Transparent, new Thickness(10, 8, 10, 8));
        face.BorderThickness = new Thickness(1);
        var button = new Button { Content = face, Style = (Style)Application.Current.FindResource("BareButton"), Focusable = false, Margin = new Thickness(0, 0, 0, 4) };
        if (onClick is not null) button.Click += (_, _) => onClick();
        return button;
    }
}
