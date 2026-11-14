using System.Windows;
using System.Windows.Media;
using CamBar.Interop;
using CamBar.Theme;

namespace CamBar.Views;

/// <summary>Base of the app's regular windows: dark title bar, dark background, app font.</summary>
public class DarkWindow : Window
{
    public DarkWindow()
    {
        Background = Palette.Window;
        Foreground = Brushes.White;
        FontFamily = Palette.UiFont;
        FontSize = 12;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SourceInitialized += (_, _) => WindowEffects.DarkTitleBar(this, Palette.TitleBarColor);
    }
}
