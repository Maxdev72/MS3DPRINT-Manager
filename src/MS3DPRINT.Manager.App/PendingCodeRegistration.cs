using MS3DPRINT.Manager.Core.Naming;
using MS3DPRINT.Manager.Core.Storage;

namespace MS3DPRINT.Manager.App;

public sealed class PendingCodeRegistration
{
    private readonly string _clientName;
    private readonly string _code;

    public PendingCodeRegistration(string clientName, string code)
    {
        _clientName = NameNormalizer.Normalize(clientName);
        _code = NameNormalizer.Normalize(code);
        if (_clientName.Length == 0 || _code.Length == 0)
            throw new ArgumentException("Le nom du client et son code doivent contenir une lettre ou un chiffre.");
    }

    public bool IsPending { get; private set; } = true;

    public void Complete(ClientCodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!IsPending) return;

        var existingCode = registry.GetCode(_clientName);
        if (existingCode is not null)
        {
            if (existingCode != _code)
                throw new FolderConflictException($"Un autre code est déjà enregistré pour le client {_clientName}.");
        }
        else
        {
            registry.Add(_clientName, _code);
        }

        IsPending = false;
    }
}
