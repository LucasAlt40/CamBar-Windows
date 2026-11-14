namespace CamBar.Core;

/// <summary>
/// Turns chunks of little-endian 16-bit mono PCM into float samples in -1..1.
/// Chunks can split a sample in half; the odd byte is carried over to the next call.
/// </summary>
public sealed class PcmConverter
{
    private byte? _carry;

    public float[] Convert(ReadOnlySpan<byte> chunk)
    {
        var bytes = new byte[chunk.Length + (_carry.HasValue ? 1 : 0)];
        var offset = 0;
        if (_carry is { } carry) bytes[offset++] = carry;
        chunk.CopyTo(bytes.AsSpan(offset));

        var usable = bytes.Length & ~1;
        _carry = usable < bytes.Length ? bytes[usable] : null;

        var samples = new float[usable / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8) / 32768f;
        return samples;
    }
}
