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
    [Fact]
    public void Default_choices_are_very_strong()
    {
        var s = Strength.Evaluate(new GeneratorOptions());
        Assert.Equal(StrengthLevel.VeryStrong, s.Level);
        Assert.Equal(104, s.Bits); // 16 × log2(90)
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

    [Theory]
    [InlineData(8, StrengthLevel.Fair)]    // 8 × log2(62) = 48
    [InlineData(12, StrengthLevel.Strong)] // 71
    [InlineData(17, StrengthLevel.VeryStrong)] // 101
    public void Levels_follow_the_bit_thresholds(int length, StrengthLevel level)
    {
        var s = Strength.Evaluate(new GeneratorOptions { Special = false, Length = length });
        Assert.Equal(level, s.Level);
        Assert.InRange(s.Fraction, 0.04, 1.0);
    }
}
