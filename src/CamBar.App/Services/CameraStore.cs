using CamBar.Core;
using CamBar.Infrastructure;

namespace CamBar.Services;

/// <summary>Something other services need to react to (streams restart on edit, stop on delete).</summary>
public abstract record CameraChange
{
    public sealed record Updated(Camera Camera) : CameraChange;
    public sealed record Removed(Guid Id) : CameraChange;
}

/// <summary>Source of truth for the camera list. Persists through a repository; passwords go to a credential store.</summary>
public sealed class CameraStore : ObservableObject
{
    private readonly ICameraRepository _repository;
    private readonly ICredentialStore _credentials;
    private List<Camera> _cameras;
    private Guid? _selectedId;

    public event Action<CameraChange>? Changed;

    public CameraStore(ICameraRepository repository, ICredentialStore credentials)
    {
        _repository = repository;
        _credentials = credentials;
        _cameras = repository.Load();
        _selectedId = _cameras.FirstOrDefault()?.Id;
    }

    public IReadOnlyList<Camera> Cameras => _cameras;

    public Guid? SelectedId
    {
        get => _selectedId;
        set { if (Set(ref _selectedId, value)) Raise(nameof(Selected)); }
    }

    public Camera? Selected => Find(_selectedId);

    public Camera? Find(Guid? id) => id is null ? null : _cameras.FirstOrDefault(c => c.Id == id);

    /// <summary>1-based position, used for the "cam N" labels.</summary>
    public int? NumberOf(Camera camera)
    {
        var index = _cameras.FindIndex(c => c.Id == camera.Id);
        return index < 0 ? null : index + 1;
    }

    public string PasswordFor(Guid id) => _credentials.GetPassword(id);

    public void Save(Camera camera, string password)
    {
        var copy = camera with { };   // the caller keeps editing its own draft
        _credentials.SetPassword(password, copy.Id);
        var next = _cameras.ToList();
        var index = next.FindIndex(c => c.Id == copy.Id);
        if (index >= 0) next[index] = copy; else next.Add(copy);
        _cameras = next;
        if (_selectedId is null) SelectedId = copy.Id;
        _repository.Save(_cameras);
        Raise(nameof(Cameras));
        Raise(nameof(Selected));
        Changed?.Invoke(new CameraChange.Updated(copy));
    }

    public void Delete(Camera camera)
    {
        _cameras = _cameras.Where(c => c.Id != camera.Id).ToList();
        _credentials.SetPassword("", camera.Id);
        if (_selectedId == camera.Id) SelectedId = _cameras.FirstOrDefault()?.Id;
        _repository.Save(_cameras);
        Raise(nameof(Cameras));
        Raise(nameof(Selected));
        Changed?.Invoke(new CameraChange.Removed(camera.Id));
    }
}
