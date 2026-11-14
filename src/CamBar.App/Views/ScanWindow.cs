using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamBar.Core;
using CamBar.Theme;

namespace CamBar.Views;

/// <summary>
/// "Procurar câmeras na rede": explains what will be scanned and only starts when the user agrees,
/// then lists what answered with a button to add each one.
/// </summary>
public sealed class ScanWindow : DarkWindow
{
    private readonly AppEnvironment _env;
    private readonly Action<DiscoveredDevice> _add;
    private readonly List<LocalNetwork> _networks = NetworkScanner.LocalNetworks();
    private readonly Dictionary<string, DiscoveredDevice> _devices = [];
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = UI.Text("", 12, FontWeights.Medium);
    private readonly Border _barFill = new() { Background = Palette.Accent, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Grid _bar = new() { Height = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly UIElement _intro;
    private readonly ScrollViewer _results = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private CancellationTokenSource? _scan;
    private double _fraction;

    public ScanWindow(AppEnvironment env, Action<DiscoveredDevice> add)
    {
        _env = env;
        _add = add;
        Title = "Procurar câmeras na rede";
        Width = 600;
        Height = 560;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _bar.Children.Add(new Border { Background = Palette.White(0.08), CornerRadius = new CornerRadius(2) });
        _bar.Children.Add(_barFill);
        _bar.SizeChanged += (_, _) => _barFill.Width = _bar.ActualWidth * _fraction;
        _intro = BuildIntro();
        _results.Content = _list;

        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 0) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UI.Icon(UI.Glyph.Search, 14, Palette.Accent);
        icon.Margin = new Thickness(0, 0, 10, 0);
        titleRow.Children.Add(icon);
        titleRow.Children.Add(UI.Text("Procurar câmeras na rede", 17, FontWeights.SemiBold));
        header.Children.Add(titleRow);
        _status.Margin = new Thickness(0, 12, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(_status);
        header.Children.Add(_bar);

        var body = new Grid { Margin = new Thickness(24, 16, 24, 0) };
        body.Children.Add(_intro);
        body.Children.Add(_results);

        var footer = new Border { Child = _buttons, Background = Palette.With(Palette.TitleBar, 0.6), Padding = new Thickness(24, 14, 24, 14) };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var rule = UI.Rule();
        DockPanel.SetDock(rule, Dock.Bottom);
        root.Children.Add(rule);
        root.Children.Add(body);
        Content = root;

        Closed += (_, _) => _scan?.Cancel();
        ShowIntro();
    }

    // MARK: States

    private void ShowIntro()
    {
        _intro.Visibility = Visibility.Visible;
        _results.Visibility = Visibility.Collapsed;
        _bar.Visibility = Visibility.Collapsed;
        _status.Text = _networks.Count == 0
            ? "Nenhuma rede local foi encontrada. Conecte o computador à mesma rede das câmeras e tente de novo."
            : "Nada é feito até você clicar em Iniciar busca.";
        SetButtons(("Cancelar", Close, false), ("Iniciar busca", Start, _networks.Count > 0));
    }

    private async void Start()
    {
        _intro.Visibility = Visibility.Collapsed;
        _results.Visibility = Visibility.Visible;
        _bar.Visibility = Visibility.Visible;
        _devices.Clear();
        _list.Children.Clear();
        SetProgress(0);
        _status.Text = "Procurando…";
        SetButtons(("Parar", () => _scan?.Cancel(), false));

        var source = _scan = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            SetProgress(p.Total == 0 ? 1 : (double)p.Checked / p.Total);
            _status.Text = $"Verificando {p.Checked} de {p.Total} portas… {Found(_devices.Count)}";
        });
        try
        {
            await NetworkScanner.ScanAsync(_networks, progress, device => Dispatcher.BeginInvoke(() => Show(device)), source.Token);
            SetProgress(1);
            _status.Text = _devices.Count == 0
                ? "Busca concluída: nada respondeu como câmera. Confira se as câmeras estão ligadas e se o RTSP/ONVIF está ativo no app delas."
                : $"Busca concluída: {Found(_devices.Count)}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = $"Busca interrompida. {Found(_devices.Count)}";
        }
        SetButtons(("Fechar", Close, false), ("Buscar de novo", Start, true));
    }

    private static string Found(int count) => count switch
    {
        0 => "Nenhuma câmera encontrada até agora.",
        1 => "1 possível câmera encontrada",
        _ => $"{count} possíveis câmeras encontradas",
    };

    private void SetProgress(double fraction)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        _barFill.Width = _bar.ActualWidth * _fraction;
    }

    private void SetButtons(params (string Label, Action Action, bool Primary)[] buttons)
    {
        _buttons.Children.Clear();
        foreach (var (label, action, primary) in buttons)
        {
            Button button;
            if (primary)
            {
                var face = UI.Card(UI.Text(label, 12, FontWeights.SemiBold), Palette.AccentStrong, 9, Palette.With(Palette.Accent, 0.45), new Thickness(18, 7, 18, 7));
                button = new Button { Content = face, Style = (Style)Application.Current.FindResource("BareButton"), IsDefault = true };
                button.Click += (_, _) => action();
            }
            else button = UI.PillButton(label, action);
            button.Margin = new Thickness(8, 0, 0, 0);
            _buttons.Children.Add(button);
        }
    }

    // MARK: Pieces

    private UIElement BuildIntro()
    {
        var stack = new StackPanel();
        void Paragraph(string text, Brush? color = null, double top = 0)
        {
            var block = UI.Text(text, 12.5, foreground: color ?? Palette.White(0.8));
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, top, 0, 0);
            stack.Children.Add(block);
        }

        var networks = _networks.Count == 0 ? "—" : string.Join(", ", _networks.Select(n => n.Description));
        Paragraph("O CamBar pode procurar aparelhos na sua rede local que pareçam câmeras. Para isso ele vai:");
        Paragraph("•  enviar uma pergunta ONVIF (WS-Discovery) para a rede, que as câmeras compatíveis respondem;", top: 10);
        Paragraph($"•  testar as portas RTSP 554 e 8554 em cada endereço de {networks} (até 254 por rede).", top: 4);
        Paragraph("Nada sai da sua rede local e nenhuma senha é enviada. Alguns antivírus ou firewalls podem avisar sobre a busca; isso é esperado.",
                  Palette.White(0.55), 14);
        Paragraph("Só faça a busca em uma rede que é sua ou em que você tem permissão.", Palette.WarningText, 10);
        return UI.Card(stack, Palette.Panel, 12, Palette.Hairline, new Thickness(18));
    }

    private void Show(DiscoveredDevice device)
    {
        _devices[device.Host] = device;
        _list.Children.Clear();
        foreach (var d in _devices.Values.OrderBy(d => d.Host, Comparer<string>.Create(NetworkScanner.CompareHosts)))
            _list.Children.Add(Row(d));
    }

    private UIElement Row(DiscoveredDevice device)
    {
        var icon = UI.Card(UI.Icon(UI.Glyph.Video, 12, Palette.Accent), Palette.With(Palette.Accent, 0.15), 7, Palette.With(Palette.Accent, 0.3));
        icon.Width = icon.Height = 30;
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);

        var texts = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = UI.Text(device.Title, 12.5, FontWeights.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(title);
        texts.Children.Add(UI.Text(device.Host, 11, foreground: Palette.Accent, mono: true));
        var evidence = UI.Text(device.Evidence, 10.5, foreground: Palette.White(0.5));
        evidence.TextWrapping = TextWrapping.Wrap;
        texts.Children.Add(evidence);

        UIElement action;
        var existing = _env.Store.Cameras.FirstOrDefault(c => c.Host == device.Host);
        if (existing is not null)
            action = UI.Capsule(UI.Text($"Já cadastrada: {existing.Name}", 10.5, foreground: Palette.White(0.55)), Palette.White(0.06), Brushes.Transparent, new Thickness(10, 4, 10, 4));
        else
            action = UI.PillButton("Adicionar", () => { _add(device); Owner?.Activate(); }, UI.Glyph.Add);
        DockPanel.SetDock(action, Dock.Right);

        var dock = new DockPanel();
        dock.Children.Add(icon);
        dock.Children.Add(action);
        dock.Children.Add(texts);
        var card = UI.Card(dock, Palette.Panel, 10, Palette.Hairline, new Thickness(12, 10, 12, 10));
        card.Margin = new Thickness(0, 0, 0, 8);
        return card;
    }
}
