using System.IO;
using System.Windows.Media.Imaging;

namespace CamBar.Services;

/// <summary>Saves stills to Pictures\CamBar.</summary>
public static class Snapshot
{
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CamBar");

    /// <summary>Writes a JPEG and returns its location.</summary>
    public static string Save(BitmapSource image, string cameraName, DateTime? date = null)
    {
        // "/" or ":" in a camera name (e.g. "Portão/Garagem") would otherwise break the path
        var safeName = string.Concat(cameraName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        if (safeName.Length == 0) safeName = "Camera";

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(image));

        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"{safeName} {(date ?? DateTime.Now):yyyy-MM-dd HH.mm.ss}.jpg");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
