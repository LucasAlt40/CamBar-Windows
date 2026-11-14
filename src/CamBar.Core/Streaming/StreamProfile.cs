namespace CamBar.Core;

/// <summary>
/// How much of the camera's stream is decoded. The camera always sends its native frame rate;
/// the profile decides the output size and, in standby, decodes keyframes only (~1 fps), which is where the CPU goes.
/// </summary>
public enum StreamProfile
{
    /// <summary>Nobody is looking: keep the connection alive as cheaply as possible.</summary>
    Standby,
    /// <summary>Popover, settings preview and overview tiles.</summary>
    Normal,
    /// <summary>Expanded window.</summary>
    Hd,
}

public static class StreamProfileExtensions
{
    public static int MaxWidth(this StreamProfile p) => p switch
    {
        StreamProfile.Standby => 480,
        StreamProfile.Normal => 720,
        _ => 1280,
    };

    /// <summary>ffmpeg -q:v for MJPEG (lower is better).</summary>
    public static int JpegQuality(this StreamProfile p) => p switch
    {
        StreamProfile.Standby => 8,
        StreamProfile.Normal => 7,
        _ => 5,
    };

    public static bool DecodesKeyframesOnly(this StreamProfile p) => p == StreamProfile.Standby;
}
