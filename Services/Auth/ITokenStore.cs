namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Portable per-account token storage. One file per alias in a plain folder so the store is
/// copy-portable (mint on one machine, mount into Docker). Payloads are opaque bytes owned by
/// the provider authenticator (serialized MSAL cache / Google token JSON). Encoding at rest is
/// the store's concern — see <see cref="FileTokenStore"/>.
/// </summary>
public interface ITokenStore
{
    /// <summary>True if a token blob exists for the alias.</summary>
    bool Exists(string alias);

    /// <summary>Load and decode the token blob, or null if none is stored.</summary>
    Task<byte[]?> LoadAsync(string alias, CancellationToken ct);

    /// <summary>Encode and persist the token blob for the alias.</summary>
    Task SaveAsync(string alias, byte[] data, CancellationToken ct);

    /// <summary>Delete the alias's token blob if present. Returns true if a file was removed.</summary>
    bool Delete(string alias);
}
