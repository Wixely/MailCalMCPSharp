using System.Security.Cryptography;
using System.Text;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Folder-based <see cref="ITokenStore"/>. One <c>&lt;alias&gt;.token</c> file per account.
///
/// At rest the payload is encoded, never raw:
/// <list type="bullet">
///   <item>No <c>TokenEncryptionKey</c> → <b>basic reversible encoding</b> (Base64, prefix
///   <c>B64:</c>). Portable with no key to carry. This is convenience, not a security boundary.</item>
///   <item><c>TokenEncryptionKey</c> set → <b>AES-GCM</b> (prefix <c>AES1:</c>), key derived from
///   the passphrase via SHA-256. Portable as long as the same key is present on the target.</item>
/// </list>
/// Files are created with owner-only permissions where the OS supports it.
/// </summary>
public sealed class FileTokenStore : ITokenStore
{
    private const string Base64Prefix = "B64:";
    private const string AesPrefix = "AES1:";

    private readonly string _directory;
    private readonly byte[]? _key;

    public FileTokenStore(string directory, string? encryptionKey)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        _key = string.IsNullOrEmpty(encryptionKey)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
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
        return Decode(text);
    }

    public async Task SaveAsync(string alias, byte[] data, CancellationToken ct)
    {
        var path = PathFor(alias);
        var text = Encode(data);
        await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);
        RestrictToOwner(path);
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

    private string PathFor(string alias)
    {
        var safe = string.Concat(alias.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));
        return Path.Combine(_directory, safe + ".token");
    }

    private string Encode(byte[] data)
    {
        if (_key is null)
        {
            return Base64Prefix + Convert.ToBase64String(data);
        }

        // AES-GCM: [12-byte nonce][16-byte tag][ciphertext], Base64-wrapped after the prefix.
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, data, cipher, tag);

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return AesPrefix + Convert.ToBase64String(payload);
    }

    private byte[] Decode(string text)
    {
        if (text.StartsWith(AesPrefix, StringComparison.Ordinal))
        {
            if (_key is null)
            {
                throw new InvalidOperationException(
                    "Token file is AES-encrypted but MailCal:TokenEncryptionKey is not set. " +
                    "Provide the same key that was used to write it.");
            }

            var payload = Convert.FromBase64String(text[AesPrefix.Length..]);
            var nonce = payload.AsSpan(0, 12);
            var tag = payload.AsSpan(12, 16);
            var cipher = payload.AsSpan(28);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        if (text.StartsWith(Base64Prefix, StringComparison.Ordinal))
        {
            return Convert.FromBase64String(text[Base64Prefix.Length..]);
        }

        // Unprefixed legacy content: treat as raw Base64.
        return Convert.FromBase64String(text.Trim());
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception)
            {
                // Best-effort; not fatal if the platform/filesystem rejects it.
            }
        }
    }
}
