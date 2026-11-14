using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CamBar.Core;

/// <summary>A published release: version, page to open and its notes.</summary>
public sealed record ReleaseInfo(string Version, string PageUrl, string Notes);

public enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

public sealed record UpdateResult(UpdateStatus Status, ReleaseInfo? Release = null);

/// <summary>Asks GitHub for the latest release and compares it with the running version. Sends nothing about the user.</summary>
public static class UpdateChecker
{
    public const string Repository = "LucasAlt40/CamBar-Windows";

    /// <summary>"v1.2.0" → "1.2.0"; build metadata ("+abc123") and pre-release suffixes are dropped.</summary>
    public static string Normalize(string version)
    {
        var v = version.Trim().TrimStart('v', 'V');
        var cut = v.IndexOfAny(['+', '-', ' ']);
        return cut >= 0 ? v[..cut] : v;
    }

    /// <summary>Numeric comparison per component, so 0.10.0 is newer than 0.9.0 and 1.0 equals 1.0.0.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        static int[] Parts(string v) => Normalize(v).Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var a = Parts(candidate);
        var b = Parts(current);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>Reads GitHub's "latest release" JSON.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } name) return null;
            var url = root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "";
            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return new ReleaseInfo(Normalize(name), url, notes);
        }
        catch (JsonException) { return null; }
    }

    public static UpdateResult Compare(ReleaseInfo? latest, string currentVersion) =>
        latest is null ? new UpdateResult(UpdateStatus.NoReleases)
        : IsNewer(latest.Version, currentVersion) ? new UpdateResult(UpdateStatus.Available, latest)
        : new UpdateResult(UpdateStatus.UpToDate, latest);

    public static async Task<UpdateResult> CheckAsync(string currentVersion, string repository = Repository)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CamBar", Normalize(currentVersion)));   // GitHub requires one
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        try
        {
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateResult(UpdateStatus.NoReleases);
            if (!response.IsSuccessStatusCode) return new UpdateResult(UpdateStatus.Failed);
            return Compare(Parse(await response.Content.ReadAsStringAsync()), currentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return new UpdateResult(UpdateStatus.Failed); }
    }
}
