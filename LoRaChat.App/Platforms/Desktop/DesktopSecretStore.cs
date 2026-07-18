using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Desktop <see cref="ISecretStore"/>. On Windows it uses DPAPI (matching the original app). On macOS
/// and Linux — where the desktop head also runs — DPAPI is unavailable, so it falls back to AES-GCM
/// with a per-user random key stored in the app data folder with best-effort restrictive permissions.
/// </summary>
public sealed class DesktopSecretStore : ISecretStore
{
    private readonly byte[]? _aesKey; // only used on non-Windows

    public DesktopSecretStore()
    {
        if (!OperatingSystem.IsWindows())
            _aesKey = LoadOrCreateKey();
    }

    public string Protect(string plaintext)
    {
        byte[] data = Encoding.UTF8.GetBytes(plaintext);
        if (OperatingSystem.IsWindows())
        {
            byte[] enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        // AES-GCM: [12-byte nonce][16-byte tag][ciphertext]
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] cipher = new byte[data.Length];
        using var gcm = new AesGcm(_aesKey!, 16);
        gcm.Encrypt(nonce, data, cipher, tag);
        byte[] blob = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, blob, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(blob);
    }

    public string? Unprotect(string protectedBase64)
    {
        try
        {
            byte[] blob = Convert.FromBase64String(protectedBase64);
            if (OperatingSystem.IsWindows())
            {
                byte[] dec = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }

            byte[] nonce = blob[..12];
            byte[] tag = blob[12..28];
            byte[] cipher = blob[28..];
            byte[] plain = new byte[cipher.Length];
            using var gcm = new AesGcm(_aesKey!, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] LoadOrCreateKey()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoRaChat");
        Directory.CreateDirectory(dir);
        string keyPath = Path.Combine(dir, "secret.key");
        if (File.Exists(keyPath))
        {
            try { return File.ReadAllBytes(keyPath); } catch { /* fall through to regenerate */ }
        }
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(keyPath, key);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* if we can't persist, the key is per-run; settings just won't decrypt next launch */ }
        return key;
    }
}
