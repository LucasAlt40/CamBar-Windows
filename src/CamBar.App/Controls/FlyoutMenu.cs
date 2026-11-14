using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media;
using CamBar.Theme;

namespace CamBar.Controls;

/// <summary>One line of a <see cref="FlyoutMenu"/>: a section title or a clickable item.</summary>
public sealed record MenuEntry(string Text, Action? Click = null, bool IsHeader = false, bool IsChecked = false,
                               bool IsDanger = false, string? Glyph = null);

/// <summary>A small dark pop-up menu under a control (WPF's own ContextMenu is light and hard to restyle).</summary>
public static class FlyoutMenu
{
    public static void Show(FrameworkElement anchor, IEnumerable<MenuEntry> entries)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };

        var stack = new StackPanel { MinWidth = 210 };
        foreach (var entry in entries)
        {
            if (entry.IsHeader)
            {
                var header = UI.Text(entry.Text, 10.5, FontWeights.Medium, Palette.White(0.45));
                header.Margin = new Thickness(10, 8, 10, 4);
                stack.Children.Add(header);
                continue;
            }
            stack.Children.Add(Row(entry, popup));
        }

        var card = UI.Card(stack, Palette.Card, 10, Palette.White(0.12), new Thickness(4));
        card.Margin = new Thickness(12);   // room for the shadow
        card.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.5, Color = Colors.Black };
        popup.Child = card;
        popup.IsOpen = true;
    }

    private static UIElement Row(MenuEntry entry, Popup popup)
    {
        var foreground = entry.IsDanger ? Palette.DangerSoft : Brushes.White;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (entry.IsChecked) grid.Children.Add(UI.Icon(UI.Glyph.Check, 11, Palette.Accent));
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        if (entry.Glyph is { } glyph)
        {
            var icon = UI.Icon(glyph, 12, Palette.White(0.7));
            icon.Margin = new Thickness(0, 0, 8, 0);
            label.Children.Add(icon);
        }
        label.Children.Add(UI.Text(entry.Text, 12, foreground: foreground));
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var face = new Border
        {
            Child = grid,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 7, 10, 7),
        };
        face.MouseEnter += (_, _) => face.Background = Palette.White(0.1);
        face.MouseLeave += (_, _) => face.Background = Brushes.Transparent;

        var button = new Button { Content = face, Style = (Style)Application.Current.FindResource("BareButton"), Focusable = false };
        button.Click += (_, _) =>
        {
            popup.IsOpen = false;
            entry.Click?.Invoke();
        };
        return button;
    }
}
