using System.Runtime.Versioning;
using System.Security.Cryptography;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.Services;

/// <summary>
/// Windows data protection (DPAPI) for the signed-in user: the file can only be decrypted
/// by the same Windows account on the same PC.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiProtector : ISecretProtector
{
    // Ties the encrypted data to this app, so another program cannot simply ask Windows to decrypt the file as-is.
    private static readonly byte[] Purpose = "PasswordGenerator.CurrentPassword.v1"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Purpose, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] sealedBytes) => ProtectedData.Unprotect(sealedBytes, Purpose, DataProtectionScope.CurrentUser);
}
