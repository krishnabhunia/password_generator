namespace PasswordGenerator.Core;

/// <summary>One set of characters the password may draw from, with the fewest it must contain.</summary>
public sealed record CharGroup(string Name, string Chars, int Min);

/// <summary>What the user chose: which elements to use, how many of each at least, and the length.</summary>
public sealed record GeneratorOptions
{
    public const int MinLength = 4;
    public const int MaxLength = 64;
    public const int MaxMinimum = 9;
    public const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
    public const string DigitChars = "0123456789";
    /// <summary>Every special character the app offers, in the order the chips are shown.</summary>
    public const string AllSymbols = "!@#$%^&*()-_=+[]{};:,.?/~|<>";

    public bool Upper { get; init; } = true;
    public bool Lower { get; init; } = true;
    public bool Digits { get; init; } = true;
    public bool Special { get; init; } = true;
    public int MinUpper { get; init; } = 1;
    public int MinLower { get; init; } = 1;
    public int MinDigits { get; init; } = 1;
    public int MinSpecial { get; init; } = 1;
    public int Length { get; init; } = 16;
    /// <summary>The special characters that are switched on.</summary>
    public string Symbols { get; init; } = AllSymbols;

    /// <summary>The element groups that are switched on and have at least one character.</summary>
    public IReadOnlyList<CharGroup> Groups()
    {
        var groups = new List<CharGroup>(4);
        if (Upper) groups.Add(new CharGroup("A–Z", UpperChars, Clamp(MinUpper)));
        if (Lower) groups.Add(new CharGroup("a–z", LowerChars, Clamp(MinLower)));
        if (Digits) groups.Add(new CharGroup("0–9", DigitChars, Clamp(MinDigits)));
        var symbols = CleanSymbols(Symbols);
        if (Special && symbols.Length > 0) groups.Add(new CharGroup(symbols.Length + " special", symbols, Clamp(MinSpecial)));
        return groups;
    }

    /// <summary>The fewest characters the chosen minimums need.</summary>
    public int RequiredLength() => Groups().Sum(g => g.Min);

    /// <summary>The same choices with every value brought inside its allowed range.</summary>
    public GeneratorOptions Normalized()
    {
        var o = this with
        {
            MinUpper = Clamp(MinUpper),
            MinLower = Clamp(MinLower),
            MinDigits = Clamp(MinDigits),
            MinSpecial = Clamp(MinSpecial),
            Symbols = CleanSymbols(Symbols),
            Length = Math.Clamp(Length, MinLength, MaxLength),
        };
        var required = o.RequiredLength();
        return o.Length < required ? o with { Length = required } : o;
    }

    /// <summary>Keeps only offered symbols, once each, in the display order.</summary>
    public static string CleanSymbols(string? symbols) =>
        string.IsNullOrEmpty(symbols) ? "" : new string(AllSymbols.Where(symbols.Contains).ToArray());

    private static int Clamp(int minimum) => Math.Clamp(minimum, 0, MaxMinimum);
}
