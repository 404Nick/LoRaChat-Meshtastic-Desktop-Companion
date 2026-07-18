namespace LoRaChat.Core.Abstractions;

/// <summary>
/// Encrypts/decrypts small secrets (MQTT password, channel PSKs) at rest, replacing the
/// Windows-only DPAPI (<c>ProtectedData</c>) used by the original app. Implemented per platform:
/// Windows -> DPAPI, macOS -> Keychain, Linux -> libsecret or a machine-derived AES key,
/// Android -> Android Keystore.
/// </summary>
public interface ISecretStore
{
    /// <summary>Protect UTF-8 <paramref name="plaintext"/>, returning a base64 blob that only this
    /// user/device can later unprotect.</summary>
    string Protect(string plaintext);

    /// <summary>Reverse of <see cref="Protect"/>. Returns null if the blob can't be decrypted
    /// (wrong user/device, corrupted, or not actually a protected blob).</summary>
    string? Unprotect(string protectedBase64);
}
