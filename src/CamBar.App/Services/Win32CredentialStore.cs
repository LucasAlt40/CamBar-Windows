using CamBar.Core;
using CamBar.Interop;

namespace CamBar.Services;

/// <summary>Camera passwords in the Windows Credential Manager (Control Panel → Credential Manager → Windows Credentials).</summary>
public sealed class CredentialManagerStore : ICredentialStore
{
    private readonly string _service;

    public CredentialManagerStore(string service = "CamBar") => _service = service;

    private string Target(Guid id) => $"{_service}/{id}";

    public string GetPassword(Guid id) => Native.ReadCredential(Target(id)) ?? "";

    public void SetPassword(string password, Guid id)
    {
        if (password.Length == 0) Native.DeleteCredential(Target(id));
        else Native.WriteCredential(Target(id), id.ToString(), password);
    }
}
