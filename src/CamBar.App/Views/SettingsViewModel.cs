using CamBar.Core;
using CamBar.Infrastructure;
using CamBar.Services;

namespace CamBar.Views;

public enum SignalTest { Idle, Testing, Reachable, Unreachable }

/// <summary>Editing state of the settings window: which camera is selected, the draft being edited and the signal test.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly IReachabilityChecker _checker;
    private CancellationTokenSource? _test;
    private Camera _draft = new();
    private string _password = "";
    private string _search = "";
    private bool _showsPassword;
    private Guid? _selectedId;
    private bool _isNew;
    private SignalTest _signalTest;
    private bool _justSaved;

    public SettingsViewModel(CameraStore store, IReachabilityChecker checker)
    {
        Store = store;
        _checker = checker;
    }

    public CameraStore Store { get; }

    public Camera Draft
    {
        get => _draft;
        private set { _draft = value; RaiseAll(); }
    }

    // The form edits the draft through these, so bindings stay simple and the record stays a plain value.
    public string Name { get => _draft.Name; set { _draft.Name = value; Raise(); RaiseDerived(); } }
    public string Host { get => _draft.Host; set { _draft.Host = value; Raise(); RaiseDerived(); } }
    public string Path { get => _draft.Path; set { _draft.Path = value; Raise(); RaiseDerived(); } }
    public string Username { get => _draft.Username; set { _draft.Username = value; Raise(); } }

    public string PortText
    {
        get => _draft.Port.ToString();
        set
        {
            if (int.TryParse(value, out var port) && port is > 0 and <= 65535) _draft.Port = port;
            Raise();
            RaiseDerived();
        }
    }

    /// <summary>Empty means automatic.</summary>
    public string OnvifPortText
    {
        get => _draft.OnvifPort?.ToString() ?? "";
        set
        {
            _draft.OnvifPort = int.TryParse(value, out var port) && port is > 0 and <= 65535 ? port : null;
            Raise();
        }
    }

    public RtspTransport Transport { get => _draft.Transport; set { _draft.Transport = value; Raise(); } }

    public string Password { get => _password; set => Set(ref _password, value); }
    public string Search { get => _search; set { if (Set(ref _search, value)) Raise(nameof(FilteredCameras)); } }
    public bool ShowsPassword { get => _showsPassword; set => Set(ref _showsPassword, value); }
    public Guid? SelectedId { get => _selectedId; private set => Set(ref _selectedId, value); }
    public bool IsNew { get => _isNew; private set { if (Set(ref _isNew, value)) Raise(nameof(SavedCamera)); } }
    public SignalTest SignalTestState { get => _signalTest; private set => Set(ref _signalTest, value); }
    public bool JustSaved { get => _justSaved; private set => Set(ref _justSaved, value); }

    public bool CanSave => Name.Trim().Length > 0 && Host.Trim().Length > 0;
    public string DisplayUrl => _draft.DisplayUrl;

    /// <summary>The saved camera being edited (null while creating a new one).</summary>
    public Camera? SavedCamera => IsNew ? null : Store.Find(SelectedId);

    public IReadOnlyList<Camera> FilteredCameras
    {
        get
        {
            var query = Search.Trim();
            return query.Length == 0
                ? Store.Cameras
                : Store.Cameras.Where(c => c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                           || c.Host.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
    }

    // MARK: Actions

    public void Start()
    {
        if ((Store.Selected ?? Store.Cameras.FirstOrDefault()) is { } camera) Select(camera);
        else StartNewCamera();
    }

    public void Select(Camera camera)
    {
        SelectedId = camera.Id;
        Draft = camera with { };   // edit a copy; nothing changes until Save
        Password = Store.PasswordFor(camera.Id);
        IsNew = false;
        ResetTransientState();
        Store.SelectedId = camera.Id;
    }

    public void StartNewCamera()
    {
        Draft = new Camera();
        SelectedId = _draft.Id;
        Password = "";
        IsNew = true;
        ResetTransientState();
    }

    public void Cancel()
    {
        if ((SavedCamera ?? Store.Cameras.FirstOrDefault()) is { } camera) Select(camera);
        else StartNewCamera();
    }

    public async void Save()
    {
        if (!CanSave) return;
        // a stray space in the host or path (common when pasting) breaks the RTSP URL
        _draft.Name = _draft.Name.Trim();
        _draft.Host = _draft.Host.Trim();
        _draft.Path = _draft.Path.Trim();
        _draft.Username = _draft.Username.Trim();
        Store.Save(_draft, _password);
        IsNew = false;
        SelectedId = _draft.Id;
        RaiseAll();
        JustSaved = true;
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        JustSaved = false;
    }

    public void DeleteSelected()
    {
        if (SavedCamera is not { } camera) return;
        Store.Delete(camera);
        if (Store.Cameras.FirstOrDefault() is { } first) Select(first);
        else StartNewCamera();
    }

    public async void TestSignal()
    {
        SignalTestState = SignalTest.Testing;
        _test?.Cancel();
        var source = _test = new CancellationTokenSource();
        var reachable = await _checker.IsReachableAsync(_draft.Host, _draft.Port, TimeSpan.FromSeconds(3));
        if (source.IsCancellationRequested) return;
        SignalTestState = reachable ? SignalTest.Reachable : SignalTest.Unreachable;
    }

    private void ResetTransientState()
    {
        ShowsPassword = false;
        _test?.Cancel();
        SignalTestState = SignalTest.Idle;
    }

    private void RaiseDerived()
    {
        Raise(nameof(CanSave));
        Raise(nameof(DisplayUrl));
    }

    private void RaiseAll()
    {
        foreach (var name in new[] { nameof(Draft), nameof(Name), nameof(Host), nameof(Path), nameof(Username), nameof(PortText),
                                     nameof(OnvifPortText), nameof(Transport), nameof(CanSave), nameof(DisplayUrl), nameof(SavedCamera) })
            Raise(name);
    }
}
