using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CamBar.Core;

namespace CamBar.Services;

/// <summary>"Buscar atualizações": asks GitHub and, when there is a new version, offers to open its download page.</summary>
public static class Updates
{
    public static string CurrentVersion =>
        UpdateChecker.Normalize(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0");

    private static bool _checking;

    public static async void CheckInteractively()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var current = CurrentVersion;
            var result = await UpdateChecker.CheckAsync(current);
            switch (result.Status)
            {
                case UpdateStatus.Available when result.Release is { } release:
                    var answer = MessageBox.Show(
                        $"A versão {release.Version} do CamBar está disponível (você tem a {current}).\n\nAbrir a página de download?",
                        "CamBar — Atualização disponível", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (answer == MessageBoxResult.Yes && release.PageUrl.StartsWith("https://github.com/", StringComparison.Ordinal))
                        Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true });
                    break;
                case UpdateStatus.UpToDate:
                case UpdateStatus.NoReleases:
                    MessageBox.Show($"Você já está na versão mais recente ({current}).", "CamBar", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                default:
                    MessageBox.Show("Não foi possível verificar agora. Confira a conexão com a internet e tente de novo.",
                                    "CamBar", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        finally { _checking = false; }
    }
}
