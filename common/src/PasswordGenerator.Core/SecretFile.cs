using System.Text;

namespace PasswordGenerator.Core;

/// <summary>Encrypts and decrypts bytes so that only this user on this PC can read them.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] sealedBytes);
}

/// <summary>
/// Keeps one secret (the current password) in a file. The text never touches the disk in the clear:
/// it goes through an <see cref="ISecretProtector"/> first. Without a protector nothing is stored at all.
/// </summary>
public sealed class SecretFile(string path, ISecretProtector? protector)
{
    private static readonly byte[] Header = "PWG1"u8.ToArray();

    /// <summary>False when this system has no way to encrypt, so the secret lives only while the app runs.</summary>
    public bool CanStore => protector is not null;

    /// <summary>Saves the secret, or removes the file when it is empty. Returns false when it could not be stored.</summary>
    public bool Save(string secret)
    {
        if (string.IsNullOrEmpty(secret)) { Delete(); return true; }
        if (protector is null) return false;
        try
        {
            var plain = Encoding.UTF8.GetBytes(secret);
            var sealedBytes = protector.Protect(plain);
            Array.Clear(plain);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, Header.Concat(sealedBytes).ToArray());
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (IsExpected(e))
        {
            return false;
        }
    }

    /// <summary>The stored secret, or an empty string when there is none or it cannot be read on this PC.</summary>
    public string Load()
    {
        if (protector is null) return "";
        try
        {
            if (!File.Exists(path)) return "";
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length <= Header.Length || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header)) return "";
            var plain = protector.Unprotect(bytes[Header.Length..]);
            var text = Encoding.UTF8.GetString(plain);
            Array.Clear(plain);
            return text;
        }
        catch (Exception e) when (IsExpected(e))
        {
            return "";
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the next save or delete tries again.
        }
    }

    private static bool IsExpected(Exception e) =>
        e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException
            or NotSupportedException or PlatformNotSupportedException or ArgumentException;
}
