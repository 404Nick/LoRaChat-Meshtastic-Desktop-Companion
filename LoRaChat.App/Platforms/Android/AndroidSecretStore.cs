using System;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using LoRaChat.Core.Abstractions;

namespace LoRaChat.Host;

/// <summary>
/// Android <see cref="ISecretStore"/> backed by the hardware-backed Android Keystore. A per-app
/// AES-256/GCM key is generated inside the Keystore (never leaves it); secrets are stored as
/// base64 <c>[iv_len][iv][ciphertext+tag]</c>. Replaces the Windows-only DPAPI used by the original app.
/// </summary>
internal sealed class AndroidSecretStore : ISecretStore
{
    private const string KeyStoreName = "AndroidKeyStore";
    private const string Alias = "lorachat_settings_key";
    private const string Transformation = "AES/GCM/NoPadding";

    public string Protect(string plaintext)
    {
        var key = GetOrCreateKey();
        var cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(CipherMode.EncryptMode, key);
        byte[] iv = cipher.GetIV()!;
        byte[] ct = cipher.DoFinal(System.Text.Encoding.UTF8.GetBytes(plaintext))!;

        byte[] blob = new byte[1 + iv.Length + ct.Length];
        blob[0] = (byte)iv.Length;
        Buffer.BlockCopy(iv, 0, blob, 1, iv.Length);
        Buffer.BlockCopy(ct, 0, blob, 1 + iv.Length, ct.Length);
        return Convert.ToBase64String(blob);
    }

    public string? Unprotect(string protectedBase64)
    {
        try
        {
            byte[] blob = Convert.FromBase64String(protectedBase64);
            int ivLen = blob[0];
            byte[] iv = new byte[ivLen];
            Buffer.BlockCopy(blob, 1, iv, 0, ivLen);
            byte[] ct = new byte[blob.Length - 1 - ivLen];
            Buffer.BlockCopy(blob, 1 + ivLen, ct, 0, ct.Length);

            var key = GetOrCreateKey();
            var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(CipherMode.DecryptMode, key, new GCMParameterSpec(128, iv));
            byte[] plain = cipher.DoFinal(ct)!;
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    private static IKey GetOrCreateKey()
    {
        var ks = KeyStore.GetInstance(KeyStoreName)!;
        ks.Load(null);
        if (ks.ContainsAlias(Alias))
            return ks.GetKey(Alias, null)!;

        var kg = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreName)!;
        var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)!
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
            .SetKeySize(256)!
            .Build();
        kg.Init(spec);
        return kg.GenerateKey()!;
    }
}
