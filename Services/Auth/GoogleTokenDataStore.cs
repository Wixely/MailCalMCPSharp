using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Google <see cref="IDataStore"/> that persists Google OAuth tokens into the same exe-adjacent
/// token folder as the MSAL cache, encoded with <see cref="TokenCodec"/>. This keeps <b>all</b>
/// auth files in one portable folder next to the executable (instead of Google's default
/// <c>%APPDATA%</c>/home location) and applies the same at-rest encoding as Outlook tokens.
/// Files are namespaced per account: <c>google-&lt;alias&gt;-&lt;key&gt;.token</c>.
/// </summary>
public sealed class GoogleTokenDataStore : IDataStore
{
    private readonly string _directory;
    private readonly byte[]? _key;
    private readonly string _alias;

    public GoogleTokenDataStore(string directory, string? encryptionKey, string alias)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        _key = TokenCodec.DeriveKey(encryptionKey);
        _alias = alias;
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
        var path = PathFor(key);
        File.WriteAllText(path, TokenCodec.Encode(System.Text.Encoding.UTF8.GetBytes(json), _key));
        TokenCodec.RestrictToOwner(path);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<T>(default!);
        }

        var bytes = TokenCodec.Decode(File.ReadAllText(path), _key);
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_directory, $"google-{TokenCodec.SafeFileName(_alias)}-*.token"))
        {
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    /// <summary>True if any Google token file exists for this account.</summary>
    public bool HasAny() =>
        Directory.Exists(_directory) &&
        Directory.EnumerateFiles(_directory, $"google-{TokenCodec.SafeFileName(_alias)}-*.token").Any();

    private string PathFor(string key) =>
        Path.Combine(_directory, $"google-{TokenCodec.SafeFileName(_alias)}-{TokenCodec.SafeFileName(key)}.token");
}
