using PasswordGenerator.Core;
using Xunit;

namespace PasswordGenerator.Core.Tests;

public class GeneratorTests
{
    /// <summary>Counts up, so results are repeatable.</summary>
    private sealed class CountingSource : IRandomSource
    {
        private int _n;
        public int Next(int exclusiveMax) => _n++ % exclusiveMax;
    }

    private static int Count(string s, string set) => s.Count(set.Contains);

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(64)]
    public void Password_has_the_requested_length(int length)
    {
        var p = Generator.Generate(new GeneratorOptions { Length = length });
        Assert.Equal(length, p.Length);
    }

    [Fact]
    public void Length_is_kept_between_4_and_64()
    {
        Assert.Equal(4, Generator.Generate(new GeneratorOptions { Length = 1, MinUpper = 0, MinLower = 0, MinDigits = 0, MinSpecial = 0 }).Length);
        Assert.Equal(64, Generator.Generate(new GeneratorOptions { Length = 500 }).Length);
    }

    [Fact]
    public void Minimum_of_each_element_is_met_every_time()
    {
        var o = new GeneratorOptions { Length = 12, MinUpper = 3, MinLower = 2, MinDigits = 4, MinSpecial = 3 };
        for (var i = 0; i < 300; i++)
        {
            var p = Generator.Generate(o);
            Assert.Equal(12, p.Length);
            Assert.True(Count(p, GeneratorOptions.UpperChars) >= 3, p);
            Assert.True(Count(p, GeneratorOptions.LowerChars) >= 2, p);
            Assert.True(Count(p, GeneratorOptions.DigitChars) >= 4, p);
            Assert.True(Count(p, GeneratorOptions.AllSymbols) >= 3, p);
        }
    }

    [Fact]
    public void Length_grows_to_fit_the_minimums()
    {
        var o = new GeneratorOptions { Length = 4, MinUpper = 9, MinLower = 9, MinDigits = 9, MinSpecial = 9 };
        Assert.Equal(36, o.Normalized().Length);
        Assert.Equal(36, Generator.Generate(o).Length);
    }

    [Fact]
    public void Only_switched_on_elements_are_used()
    {
        var o = new GeneratorOptions { Upper = false, Lower = false, Special = false, Length = 40 };
        var p = Generator.Generate(o);
        Assert.All(p, c => Assert.Contains(c, GeneratorOptions.DigitChars));
    }

    [Fact]
    public void Only_selected_special_characters_are_used()
    {
        var o = new GeneratorOptions { Upper = false, Lower = false, Digits = false, Symbols = "#@", Length = 50 };
        for (var i = 0; i < 50; i++)
            Assert.All(Generator.Generate(o), c => Assert.Contains(c, "#@"));
    }

    [Fact]
    public void Minimum_of_a_switched_off_element_is_ignored()
    {
        var o = new GeneratorOptions { Digits = false, MinDigits = 9, Length = 8 };
        var p = Generator.Generate(o);
        Assert.Equal(8, p.Length);
        Assert.Equal(0, Count(p, GeneratorOptions.DigitChars));
    }

    [Fact]
    public void Nothing_switched_on_gives_an_empty_password()
    {
        var o = new GeneratorOptions { Upper = false, Lower = false, Digits = false, Special = false };
        Assert.Equal("", Generator.Generate(o));
    }

    [Fact]
    public void Special_on_with_every_symbol_cleared_counts_as_off()
    {
        var o = new GeneratorOptions { Upper = false, Lower = false, Digits = false, Special = true, Symbols = "" };
        Assert.Equal("", Generator.Generate(o));
        Assert.Empty(o.Groups());
    }

    [Fact]
    public void Unknown_and_repeated_symbols_are_dropped()
    {
        Assert.Equal("!#", GeneratorOptions.CleanSymbols("##!!ab é"));
        Assert.Equal("", GeneratorOptions.CleanSymbols(null));
    }

    [Fact]
    public void Same_random_numbers_give_the_same_password()
    {
        var o = new GeneratorOptions { Length = 20 };
        Assert.Equal(Generator.Generate(o, new CountingSource()), Generator.Generate(o, new CountingSource()));
    }

    [Fact]
    public void Two_passwords_in_a_row_differ()
    {
        var o = new GeneratorOptions { Length = 32 };
        Assert.NotEqual(Generator.Generate(o), Generator.Generate(o));
    }

    [Fact]
    public void Every_offered_character_can_appear()
    {
        var seen = new HashSet<char>();
        var o = new GeneratorOptions { Length = 64 };
        for (var i = 0; i < 400; i++) seen.UnionWith(Generator.Generate(o));
        var all = GeneratorOptions.UpperChars + GeneratorOptions.LowerChars + GeneratorOptions.DigitChars + GeneratorOptions.AllSymbols;
        Assert.Equal(all.Length, seen.Count);
    }

    [Fact]
    public void Count_of_possible_passwords_is_exact()
    {
        // Digits only, 4 long: 10^4.
        var digits = new GeneratorOptions { Upper = false, Lower = false, Special = false, Length = 4 };
        Assert.Equal(10_000, (int)Generator.CountPasswords(digits));
        // Digits and '#', at least one of each: 11^4 minus all-digit (10^4) minus all-'#' (1).
        var two = new GeneratorOptions { Upper = false, Lower = false, Symbols = "#", Length = 4 };
        Assert.Equal(4_640, (int)Generator.CountPasswords(two));
        // No minimums: simply 11^4.
        Assert.Equal(14_641, (int)Generator.CountPasswords(two with { MinDigits = 0, MinSpecial = 0 }));
        // At least three '#': C(4,3)·10 + 1.
        Assert.Equal(41, (int)Generator.CountPasswords(two with { MinDigits = 0, MinSpecial = 3 }));
        Assert.Equal(0, (int)Generator.CountPasswords(two with { Digits = false, Special = false }));
    }

    [Fact]
    public void Count_matches_a_brute_force_count()
    {
        // Lowercase (26), digits (10) and "#@": every 4-character string checked one by one.
        var o = new GeneratorOptions { Upper = false, Symbols = "#@", Length = 4, MinLower = 1, MinDigits = 2, MinSpecial = 1 };
        var pool = GeneratorOptions.LowerChars + GeneratorOptions.DigitChars + "#@";
        var expected = 0;
        foreach (var a in pool) foreach (var b in pool) foreach (var c in pool) foreach (var d in pool)
        {
            var s = new string(new[] { a, b, c, d });
            if (Count(s, GeneratorOptions.LowerChars) >= 1 && Count(s, GeneratorOptions.DigitChars) >= 2 && Count(s, "#@") >= 1) expected++;
        }
        Assert.Equal(expected, (int)Generator.CountPasswords(o));
    }

    [Fact]
    public void Every_allowed_password_is_equally_likely()
    {
        // Digits and '#', at least one of each, 4 long: 4,640 passwords, 4,000 of them with exactly one '#'.
        var o = new GeneratorOptions { Upper = false, Lower = false, Symbols = "#", Length = 4 };
        const int runs = 40_000;
        var hashes = new int[5];
        var firstIsHash = 0;
        for (var i = 0; i < runs; i++)
        {
            var p = Generator.Generate(o);
            hashes[p.Count(c => c == '#')]++;
            if (p[0] == '#') firstIsHash++;
        }
        Assert.Equal(0, hashes[0]);
        Assert.Equal(0, hashes[4]);
        Assert.InRange(hashes[1] / (double)runs, 4000 / 4640.0 - 0.015, 4000 / 4640.0 + 0.015);
        Assert.InRange(hashes[2] / (double)runs, 600 / 4640.0 - 0.015, 600 / 4640.0 + 0.015);
        Assert.InRange(hashes[3] / (double)runs, 40 / 4640.0 - 0.005, 40 / 4640.0 + 0.005);
        // '#' in the first position: (1000 + 300 + 30) / 4640.
        Assert.InRange(firstIsHash / (double)runs, 1330 / 4640.0 - 0.015, 1330 / 4640.0 + 0.015);
    }

    [Fact]
    public void Large_minimums_still_give_valid_passwords_quickly()
    {
        var o = new GeneratorOptions { Lower = false, Digits = false, Symbols = "!", Length = 64, MinUpper = 9, MinSpecial = 9 };
        for (var i = 0; i < 200; i++)
        {
            var p = Generator.Generate(o);
            Assert.Equal(64, p.Length);
            Assert.True(Count(p, "!") >= 9 && Count(p, GeneratorOptions.UpperChars) >= 9, p);
        }
    }

    [Theory]
    [InlineData('a', CharKind.Letter)]
    [InlineData('Z', CharKind.Letter)]
    [InlineData('7', CharKind.Digit)]
    [InlineData('#', CharKind.Symbol)]
    public void Characters_are_sorted_into_kinds(char c, CharKind kind) => Assert.Equal(kind, Generator.KindOf(c));

    [Fact]
    public void The_28_special_characters_are_all_different()
    {
        Assert.Equal(28, GeneratorOptions.AllSymbols.Length);
        Assert.Equal(28, GeneratorOptions.AllSymbols.Distinct().Count());
    }
}

public class StrengthTests
{
    private static int Bits(GeneratorOptions o) => Strength.Evaluate(o).Bits;

    [Fact]
    public void Default_choices_are_very_strong()
    {
        var s = Strength.Evaluate(new GeneratorOptions());
        Assert.Equal(StrengthLevel.VeryStrong, s.Level);
        Assert.InRange(s.Bits, 100, 103); // just under 16 × log2(90) = 103.9, because one of each element is required
    }

    [Fact]
    public void Without_minimums_it_is_length_times_log2_of_the_pool()
    {
        var o = new GeneratorOptions { MinUpper = 0, MinLower = 0, MinDigits = 0, MinSpecial = 0 };
        Assert.Equal(103, Bits(o));                                 // 16 × log2(90) = 103.87
        Assert.Equal(13, Bits(o with { Upper = false, Lower = false, Special = false, Length = 4 })); // 4 × log2(10) = 13.29
    }

    [Fact]
    public void Four_digits_are_weak()
    {
        var s = Strength.Evaluate(new GeneratorOptions { Upper = false, Lower = false, Special = false, Length = 4 });
        Assert.Equal(StrengthLevel.Weak, s.Level);
        Assert.Equal(13, s.Bits);
    }

    [Fact]
    public void Nothing_switched_on_has_no_strength()
    {
        var s = Strength.Evaluate(new GeneratorOptions { Upper = false, Lower = false, Digits = false, Special = false });
        Assert.Equal(StrengthLevel.None, s.Level);
        Assert.Equal(0, s.Fraction);
    }

    [Fact]
    public void Large_minimums_lower_the_strength()
    {
        // Uppercase plus the single symbol "!", 16 long. Requiring nine "!" leaves far fewer passwords
        // than 27^16 (76 bits): fewer than 2^47 of them.
        var o = new GeneratorOptions { Lower = false, Digits = false, Symbols = "!", Length = 16, MinUpper = 0, MinSpecial = 0 };
        Assert.Equal(76, Bits(o));
        var strict = Strength.Evaluate(o with { MinSpecial = 9 });
        Assert.InRange(strict.Bits, 40, 46);
        Assert.NotEqual(StrengthLevel.Strong, strict.Level);
        // Only "!" switched on, 4 long: exactly one password is possible, so 0 bits.
        Assert.Equal(0, Bits(new GeneratorOptions { Upper = false, Lower = false, Digits = false, Symbols = "!", Length = 4 }));
    }

    [Fact]
    public void More_length_never_lowers_the_strength()
    {
        var previous = 0;
        for (var length = 4; length <= 64; length++)
        {
            var bits = Bits(new GeneratorOptions { Length = length });
            Assert.True(bits >= previous, $"length {length}");
            previous = bits;
        }
    }

    [Theory]
    [InlineData(8, StrengthLevel.Fair)]
    [InlineData(12, StrengthLevel.Strong)]
    [InlineData(17, StrengthLevel.VeryStrong)]
    public void Levels_follow_the_bit_thresholds(int length, StrengthLevel level)
    {
        var s = Strength.Evaluate(new GeneratorOptions { Special = false, Length = length });
        Assert.Equal(level, s.Level);
        Assert.InRange(s.Fraction, 0.04, 1.0);
    }
}
