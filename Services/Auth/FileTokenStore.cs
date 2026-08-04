namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Folder-based <see cref="ITokenStore"/> for the MSAL token cache — one
/// <c>&lt;alias&gt;.token</c> file per account, in the shared exe-adjacent token folder.
/// Encoding at rest is delegated to <see cref="TokenCodec"/> (Base64 by default, AES-GCM when a
/// key is configured).
/// </summary>
public sealed class FileTokenStore : ITokenStore
{
    private readonly string _directory;
    private readonly byte[]? _key;

    public FileTokenStore(string directory, string? encryptionKey)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        _key = TokenCodec.DeriveKey(encryptionKey);
    }

    public bool Exists(string alias) => File.Exists(PathFor(alias));

    public async Task<byte[]?> LoadAsync(string alias, CancellationToken ct)
    {
        var path = PathFor(alias);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return TokenCodec.Decode(text, _key);
    }

    public async Task SaveAsync(string alias, byte[] data, CancellationToken ct)
    {
        var path = PathFor(alias);
        await File.WriteAllTextAsync(path, TokenCodec.Encode(data, _key), ct).ConfigureAwait(false);
        TokenCodec.RestrictToOwner(path);
    }

    public bool Delete(string alias)
    {
        var path = PathFor(alias);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private string PathFor(string alias) => Path.Combine(_directory, TokenCodec.SafeFileName(alias) + ".token");
}
