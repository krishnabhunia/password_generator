using System.Numerics;

namespace PasswordGenerator.Core;

public enum StrengthLevel { None, Weak, Fair, Strong, VeryStrong }

/// <param name="Bits">log2 of the number of different passwords the choices allow.</param>
/// <param name="Fraction">How full the meter is, from 0 to 1.</param>
public sealed record StrengthResult(int Bits, StrengthLevel Level, string Label, double Fraction);

public static class Strength
{
    public const int FairBits = 45;
    public const int StrongBits = 70;
    public const int VeryStrongBits = 100;

    /// <summary>
    /// The strength of a password made with these choices. Minimums are counted in: asking for
    /// many of one element leaves fewer possible passwords, and the figure goes down with it.
    /// </summary>
    public static StrengthResult Evaluate(GeneratorOptions options)
    {
        var count = Generator.CountPasswords(options);
        if (count.IsZero) return new StrengthResult(0, StrengthLevel.None, "No password", 0);

        var bits = (int)Math.Floor(BigInteger.Log(count, 2) + 1e-9);
        var (level, label) = bits >= VeryStrongBits ? (StrengthLevel.VeryStrong, "Very strong")
            : bits >= StrongBits ? (StrengthLevel.Strong, "Strong")
            : bits >= FairBits ? (StrengthLevel.Fair, "Fair")
            : (StrengthLevel.Weak, "Weak");
        return new StrengthResult(bits, level, label, Math.Clamp(bits / 128.0, 0.04, 1.0));
    }
}
