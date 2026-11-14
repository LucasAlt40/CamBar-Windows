namespace CamBar.Core;

/// <summary>
/// Splits a concatenated MJPEG byte stream (as ffmpeg writes it) into individual JPEG images.
/// JPEG data escapes 0xFF inside entropy-coded segments, so start/end markers are unambiguous.
/// </summary>
public sealed class MjpegParser
{
    private byte[] _buffer = new byte[256 * 1024];
    private int _length;
    // bytes already searched for the end marker, so big frames arriving in many chunks are scanned once
    private int _scanned;
    private readonly int _maxBufferSize;

    public MjpegParser(int maxBufferSize = 8_000_000) => _maxBufferSize = maxBufferSize;

    /// <summary>Feeds bytes and returns every complete frame found, oldest first.</summary>
    public List<byte[]> Append(ReadOnlySpan<byte> chunk)
    {
        if (_length + chunk.Length > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + chunk.Length));
        chunk.CopyTo(_buffer.AsSpan(_length));
        _length += chunk.Length;

        var frames = new List<byte[]>();
        while (true)
        {
            var start = _buffer.AsSpan(0, _length).IndexOf((byte[])[0xFF, 0xD8]);
            if (start < 0)
            {
                // nothing to keep, except a lone 0xFF that may be the first half of a marker
                var keep = _length > 0 && _buffer[_length - 1] == 0xFF;
                if (keep) _buffer[0] = 0xFF;
                _length = keep ? 1 : 0;
                _scanned = 0;
                break;
            }
            if (start > 0)
            {
                Buffer.BlockCopy(_buffer, start, _buffer, 0, _length - start);
                _length -= start;
                _scanned = 0;
            }

            // step back one byte: the marker may straddle the previous chunk
            var from = Math.Max(2, _scanned - 1);
            var relative = from < _length ? _buffer.AsSpan(from, _length - from).IndexOf((byte[])[0xFF, 0xD9]) : -1;
            if (relative < 0)
            {
                _scanned = _length;
                break;
            }
            var end = from + relative + 2;
            frames.Add(_buffer.AsSpan(0, end).ToArray());
            Buffer.BlockCopy(_buffer, end, _buffer, 0, _length - end);
            _length -= end;
            _scanned = 0;
        }

        // a corrupt stream without end markers must not grow memory forever
        if (_length > _maxBufferSize)
        {
            _length = 0;
            _scanned = 0;
        }
        return frames;
    }
}
