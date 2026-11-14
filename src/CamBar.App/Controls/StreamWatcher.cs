using System.ComponentModel;
using CamBar.Streaming;

namespace CamBar.Controls;

/// <summary>
/// Subscribes to a long-lived <see cref="CameraStream"/> only while a view is on screen,
/// so closed windows do not stay alive through the stream's event.
/// </summary>
public sealed class StreamWatcher
{
    private readonly System.Windows.FrameworkElement _owner;
    private readonly Action<string?> _changed;
    private CameraStream? _stream;
    private bool _hooked;

    public StreamWatcher(System.Windows.FrameworkElement owner, Action<string?> changed)
    {
        _owner = owner;
        _changed = changed;
        owner.Loaded += (_, _) => Hook();
        owner.Unloaded += (_, _) => Unhook();
    }

    public CameraStream? Stream => _stream;

    /// <summary>Switches to another stream (or none). Fires one change with a null property name so the view redraws everything.</summary>
    public void Bind(CameraStream? stream)
    {
        if (ReferenceEquals(stream, _stream)) return;
        Unhook();
        _stream = stream;
        if (_owner.IsLoaded) Hook();
        _changed(null);
    }

    private void Hook()
    {
        if (_hooked || _stream is null) return;
        _stream.PropertyChanged += OnChanged;
        _hooked = true;
        _changed(null);   // things may have changed while unhooked
    }

    private void Unhook()
    {
        if (!_hooked || _stream is null) return;
        _stream.PropertyChanged -= OnChanged;
        _hooked = false;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => _changed(e.PropertyName);
}
