using System.Security.Cryptography;

namespace PasswordGenerator.Core;

public interface IRandomSource
{
    /// <summary>A uniformly random whole number from 0 up to, but not including, <paramref name="exclusiveMax"/>.</summary>
    int Next(int exclusiveMax);
}

/// <summary>Random numbers from the operating system's cryptographic generator.</summary>
public sealed class CryptoRandomSource : IRandomSource
{
    public int Next(int exclusiveMax) => RandomNumberGenerator.GetInt32(exclusiveMax);
}

public enum CharKind { Letter, Digit, Symbol }

public static class Generator
{
    /// <summary>
    /// Makes one password. Returns an empty string when no element is switched on.
    /// The minimum of each element is placed first, the rest is drawn from all chosen characters,
    /// and the result is shuffled so the guaranteed characters are not at the start.
    /// </summary>
    public static string Generate(GeneratorOptions options, IRandomSource? random = null)
    {
        random ??= new CryptoRandomSource();
        var o = options.Normalized();
        var groups = o.Groups();
        if (groups.Count == 0) return "";

        var chars = new List<char>(o.Length);
        foreach (var group in groups)
            for (var i = 0; i < group.Min; i++)
                chars.Add(group.Chars[random.Next(group.Chars.Length)]);

        var pool = string.Concat(groups.Select(g => g.Chars));
        while (chars.Count < o.Length) chars.Add(pool[random.Next(pool.Length)]);

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    public static CharKind KindOf(char c) =>
        c is >= '0' and <= '9' ? CharKind.Digit
        : c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') ? CharKind.Letter
        : CharKind.Symbol;
}
