namespace PasswordGenerator.Core;

public enum StrengthLevel { None, Weak, Fair, Strong, VeryStrong }

/// <param name="Bits">Estimated entropy: length × log2(number of possible characters).</param>
/// <param name="Fraction">How full the meter is, from 0 to 1.</param>
public sealed record StrengthResult(int Bits, StrengthLevel Level, string Label, double Fraction);

public static class Strength
{
    public const int FairBits = 45;
    public const int StrongBits = 70;
    public const int VeryStrongBits = 100;

    public static StrengthResult Evaluate(GeneratorOptions options)
    {
        var o = options.Normalized();
        var pool = o.Groups().Sum(g => g.Chars.Length);
        if (pool == 0) return new StrengthResult(0, StrengthLevel.None, "No password", 0);

        var bits = (int)Math.Round(o.Length * Math.Log2(pool));
        var (level, label) = bits >= VeryStrongBits ? (StrengthLevel.VeryStrong, "Very strong")
            : bits >= StrongBits ? (StrengthLevel.Strong, "Strong")
            : bits >= FairBits ? (StrengthLevel.Fair, "Fair")
            : (StrengthLevel.Weak, "Weak");
        return new StrengthResult(bits, level, label, Math.Clamp(bits / 128.0, 0.04, 1.0));
    }
}
