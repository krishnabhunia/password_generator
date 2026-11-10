using System.Numerics;
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
    /// Every password that fits the choices (length, elements, minimum of each) is equally likely,
    /// so the strength figure from <see cref="CountPasswords"/> is exact, not an estimate.
    /// </summary>
    public static string Generate(GeneratorOptions options, IRandomSource? random = null)
    {
        random ??= new CryptoRandomSource();
        var o = options.Normalized();
        var groups = o.Groups();
        if (groups.Count == 0) return "";

        // 1. How many characters each element gets, weighted by how many passwords each split allows.
        var table = Table(groups, o.Length);
        var counts = new int[groups.Count];
        var remaining = o.Length;
        for (var i = groups.Count; i > 1; i--)
        {
            var group = groups[i - 1];
            var pick = RandomBelow(table[i][remaining], random);
            for (var c = group.Min; c <= remaining; c++)
            {
                var weight = Binomial(remaining, c) * BigInteger.Pow(group.Chars.Length, c) * table[i - 1][remaining - c];
                if (pick < weight) { counts[i - 1] = c; break; }
                pick -= weight;
            }
            remaining -= counts[i - 1];
        }
        counts[0] = remaining;

        // 2. Which positions each element takes: a uniform shuffle of the element labels.
        var slots = new List<int>(o.Length);
        for (var g = 0; g < groups.Count; g++) slots.AddRange(Enumerable.Repeat(g, counts[g]));
        for (var i = slots.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }

        // 3. A uniformly random character of that element in every position.
        var chars = new char[o.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            var set = groups[slots[i]].Chars;
            chars[i] = set[random.Next(set.Length)];
        }
        return new string(chars);
    }

    /// <summary>How many different passwords the choices allow. Zero when nothing is switched on.</summary>
    public static BigInteger CountPasswords(GeneratorOptions options)
    {
        var o = options.Normalized();
        var groups = o.Groups();
        return groups.Count == 0 ? BigInteger.Zero : Table(groups, o.Length)[groups.Count][o.Length];
    }

    public static CharKind KindOf(char c) =>
        c is >= '0' and <= '9' ? CharKind.Digit
        : c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') ? CharKind.Letter
        : CharKind.Symbol;

    /// <summary>
    /// table[i][j] = number of ways to fill j positions using only the first i elements,
    /// with each of those elements appearing at least its minimum number of times.
    /// </summary>
    private static BigInteger[][] Table(IReadOnlyList<CharGroup> groups, int length)
    {
        var table = new BigInteger[groups.Count + 1][];
        table[0] = new BigInteger[length + 1];
        table[0][0] = BigInteger.One;
        for (var i = 1; i <= groups.Count; i++)
        {
            var group = groups[i - 1];
            table[i] = new BigInteger[length + 1];
            for (var j = 0; j <= length; j++)
            {
                var sum = BigInteger.Zero;
                for (var c = group.Min; c <= j; c++)
                {
                    var before = table[i - 1][j - c];
                    if (!before.IsZero) sum += Binomial(j, c) * BigInteger.Pow(group.Chars.Length, c) * before;
                }
                table[i][j] = sum;
            }
        }
        return table;
    }

    private static BigInteger Binomial(int n, int k)
    {
        if (k < 0 || k > n) return BigInteger.Zero;
        k = Math.Min(k, n - k);
        var result = BigInteger.One;
        for (var i = 1; i <= k; i++) result = result * (n - k + i) / i;
        return result;
    }

    /// <summary>A uniformly random whole number from 0 up to, but not including, <paramref name="limit"/>.</summary>
    private static BigInteger RandomBelow(BigInteger limit, IRandomSource random)
    {
        if (limit <= int.MaxValue) return random.Next((int)limit);
        var bits = (int)(limit - 1).GetBitLength();
        var size = (bits + 7) / 8;
        var mask = (byte)(0xFF >> (size * 8 - bits));
        var buffer = new byte[size + 1]; // the extra zero byte keeps the number positive
        while (true)
        {
            for (var i = 0; i < size; i++) buffer[i] = (byte)random.Next(256);
            buffer[size - 1] &= mask;
            var value = new BigInteger(buffer);
            if (value < limit) return value;
        }
    }
}
