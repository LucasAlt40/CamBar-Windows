namespace CamBar.Core;

/// <summary>Stores one password per camera.</summary>
public interface ICredentialStore
{
    string GetPassword(Guid id);
    /// <summary>An empty password removes the entry.</summary>
    void SetPassword(string password, Guid id);
}

/// <summary>In-memory store for checks.</summary>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<Guid, string> _passwords = [];

    public string GetPassword(Guid id) => _passwords.GetValueOrDefault(id, "");

    public void SetPassword(string password, Guid id)
    {
        if (password.Length == 0) _passwords.Remove(id); else _passwords[id] = password;
    }
}
