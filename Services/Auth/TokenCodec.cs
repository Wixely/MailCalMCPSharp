using System.Security.Cryptography;
using System.Text;

namespace MailCalMCPSharp.Services.Auth;

/// <summary>
/// Encodes/decodes token blobs at rest. Shared by <see cref="FileTokenStore"/> (MSAL cache) and
/// <see cref="GoogleTokenDataStore"/> (Google tokens) so every auth file in the store folder is
/// treated identically:
/// <list type="bullet">
///   <item>No key → basic reversible Base64 (prefix <c>B64:</c>). Portable, no key to carry.</item>
///   <item>Key set → AES-GCM (prefix <c>AES1:</c>), key = SHA-256 of the passphrase.</item>
/// </list>
/// </summary>
public static class TokenCodec
{
    public const string Base64Prefix = "B64:";
    public const string AesPrefix = "AES1:";

    public static byte[]? DeriveKey(string? passphrase) =>
        string.IsNullOrEmpty(passphrase) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));

    public static string Encode(byte[] data, byte[]? key)
    {
        if (key is null)
        {
            return Base64Prefix + Convert.ToBase64String(data);
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, data, cipher, tag);

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return AesPrefix + Convert.ToBase64String(payload);
    }

    public static byte[] Decode(string text, byte[]? key)
    {
        if (text.StartsWith(AesPrefix, StringComparison.Ordinal))
        {
            if (key is null)
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
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        if (text.StartsWith(Base64Prefix, StringComparison.Ordinal))
        {
            return Convert.FromBase64String(text[Base64Prefix.Length..]);
        }

        return Convert.FromBase64String(text.Trim());
    }

    /// <summary>Replace filesystem-invalid characters in a key so it is safe as a file name.</summary>
    public static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));

    /// <summary>Best-effort owner-only permissions on Unix; no-op elsewhere.</summary>
    public static void RestrictToOwner(string path)
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
