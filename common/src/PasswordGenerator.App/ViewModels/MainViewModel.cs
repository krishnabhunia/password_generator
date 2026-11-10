using System.Windows.Input;
using Avalonia.Threading;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IRandomSource _random;
    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _loading;
    private int _length;
    private string _password = "";
    private IReadOnlyList<CharVm> _chars = Array.Empty<CharVm>();
    private StrengthResult _strength = new(0, StrengthLevel.None, "", 0);
    private string _recipe = "";
    private string _symbolCountText = "";
    private bool _copied;
    private string? _updateText;
    private bool _updateBusy;

    /// <param name="random">Only the self-test passes one, so its pictures never hold a real password.</param>
    public MainViewModel(GeneratorOptions options, string versionText, IRandomSource? random = null)
    {
        _random = random ?? new CryptoRandomSource();
        var o = options.Normalized();
        VersionText = versionText;
        _loading = true;
        Upper = new ElementVm("Uppercase", "Uppercase", "A–Z", o.Upper, o.MinUpper, Regenerate);
        Lower = new ElementVm("Lowercase", "Lowercase", "a–z", o.Lower, o.MinLower, Regenerate);
        Digits = new ElementVm("Digits", "Digits", "0–9", o.Digits, o.MinDigits, Regenerate);
        Special = new ElementVm("Special characters", "Special", "#$&", o.Special, o.MinSpecial, Regenerate);
        Elements = new[] { Upper, Lower, Digits };
        AllElements = new[] { Upper, Lower, Digits, Special };
        Symbols = GeneratorOptions.AllSymbols.Select(c => new SymbolVm(c, o.Symbols.Contains(c), SymbolClicked)).ToArray();
        _length = o.Length;
        _loading = false;

        CopyCommand = new RelayCommand(Copy);
        RegenerateCommand = new RelayCommand(Regenerate);
        LengthIncCommand = new RelayCommand(() => Length++);
        LengthDecCommand = new RelayCommand(() => Length--);
        SelectAllSymbolsCommand = new RelayCommand(() => SetAllSymbols(true));
        ClearSymbolsCommand = new RelayCommand(() => SetAllSymbols(false));
        UpdateCommand = new RelayCommand(() => UpdateRequested?.Invoke(), () => !UpdateBusy);
        InfoCommand = new RelayCommand(() => InfoRequested?.Invoke());
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
        ChangePasswordCommand = new RelayCommand(() => ChangePasswordRequested?.Invoke());
        _copiedTimer.Tick += (_, _) => Copied = false;
        Regenerate();
    }

    public ElementVm Upper { get; }
    public ElementVm Lower { get; }
    public ElementVm Digits { get; }
    public ElementVm Special { get; }
    /// <summary>Uppercase, lowercase and digits (special characters have their own block).</summary>
    public IReadOnlyList<ElementVm> Elements { get; }
    public IReadOnlyList<ElementVm> AllElements { get; }
    public IReadOnlyList<SymbolVm> Symbols { get; }

    public ICommand CopyCommand { get; }
    public ICommand RegenerateCommand { get; }
    public ICommand LengthIncCommand { get; }
    public ICommand LengthDecCommand { get; }
    public ICommand SelectAllSymbolsCommand { get; }
    public ICommand ClearSymbolsCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public ICommand InfoCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand ChangePasswordCommand { get; }

    /// <summary>Set by the window: puts text on the clipboard.</summary>
    public Func<string, Task>? CopyHandler { get; set; }
    public event Action? UpdateRequested;
    public event Action? InfoRequested;
    public event Action? SettingsRequested;
    public event Action? ChangePasswordRequested;
    /// <summary>Raised whenever the choices change, so they can be saved.</summary>
    public event Action? OptionsChanged;

    public string VersionText { get; }
    public static int MinLength => GeneratorOptions.MinLength;
    public static int MaxLength => GeneratorOptions.MaxLength;

    public int Length
    {
        get => _length;
        set
        {
            var v = Math.Clamp(value, GeneratorOptions.MinLength, GeneratorOptions.MaxLength);
            if (v == _length) { Raise(nameof(LengthValue)); return; }
            _length = v;
            Regenerate();
        }
    }

    /// <summary>The same length for the slider, which works in decimals.</summary>
    public double LengthValue
    {
        get => _length;
        set => Length = (int)Math.Round(value);
    }

    public string Password { get => _password; private set => Set(ref _password, value); }
    public IReadOnlyList<CharVm> Chars { get => _chars; private set => Set(ref _chars, value); }
    public bool IsEmpty => _password.Length == 0;
    public string StrengthLabel => _strength.Label;
    public string BitsText => _strength.Level == StrengthLevel.None ? "" : _strength.Bits + " bits";
    public double MeterFraction => _strength.Fraction;
    public bool IsWeak => _strength.Level == StrengthLevel.Weak;
    public bool IsFair => _strength.Level == StrengthLevel.Fair;
    public bool IsStrong => _strength.Level == StrengthLevel.Strong;
    public bool IsVeryStrong => _strength.Level == StrengthLevel.VeryStrong;
    public string Recipe { get => _recipe; private set => Set(ref _recipe, value); }
    public string SymbolCountText { get => _symbolCountText; private set => Set(ref _symbolCountText, value); }

    public bool Copied
    {
        get => _copied;
        private set
        {
            if (!Set(ref _copied, value)) return;
            Raise(nameof(CopyLabel));
            Raise(nameof(CopyShortLabel));
            if (!value) _copiedTimer.Stop();
        }
    }

    public string CopyLabel => _copied ? "Copied" : "Copy password";
    public string CopyShortLabel => _copied ? "Copied" : "Copy";

    /// <summary>"Update to v1.2.3" when an update was found, otherwise null and the button is hidden.</summary>
    public string? UpdateText
    {
        get => _updateText;
        set { if (Set(ref _updateText, value)) Raise(nameof(UpdateVisible)); }
    }

    public bool UpdateVisible => !string.IsNullOrEmpty(_updateText);

    public bool UpdateBusy
    {
        get => _updateBusy;
        set { if (Set(ref _updateBusy, value)) UpdateCommand.RaiseCanExecuteChanged(); }
    }

    public GeneratorOptions CurrentOptions() => new()
    {
        Upper = Upper.IsOn, Lower = Lower.IsOn, Digits = Digits.IsOn, Special = Special.IsOn,
        MinUpper = Upper.Min, MinLower = Lower.Min, MinDigits = Digits.Min, MinSpecial = Special.Min,
        Length = _length,
        Symbols = string.Concat(Symbols.Where(s => s.IsOn).Select(s => s.Char)),
    };

    public void Regenerate()
    {
        if (_loading) return;
        var o = CurrentOptions().Normalized();
        if (o.Length != _length) _length = o.Length; // the minimums need more room
        var password = Generator.Generate(o, _random);
        _strength = Strength.Evaluate(o);

        foreach (var s in Symbols) s.IsActive = Special.IsOn && s.IsOn;
        var on = Symbols.Count(s => s.IsOn);
        SymbolCountText = $"{on} of {Symbols.Count} selected";
        var groups = o.Groups();
        Recipe = groups.Count == 0 ? "No elements selected" : $"{o.Length} characters from {string.Join(", ", groups.Select(g => g.Name))}";

        Password = password;
        Chars = password.Select(c => new CharVm(c.ToString(), Generator.KindOf(c) == CharKind.Digit, Generator.KindOf(c) == CharKind.Symbol)).ToArray();
        Copied = false;
        foreach (var name in new[]
                 {
                     nameof(Length), nameof(LengthValue), nameof(IsEmpty), nameof(StrengthLabel), nameof(BitsText), nameof(MeterFraction),
                     nameof(IsWeak), nameof(IsFair), nameof(IsStrong), nameof(IsVeryStrong),
                 })
            Raise(name);
        OptionsChanged?.Invoke();
    }

    private void SymbolClicked(SymbolVm symbol)
    {
        // With special characters switched off, the first click switches them back on.
        if (!Special.IsOn) { Special.IsOn = true; return; }
        symbol.IsOn = !symbol.IsOn;
        Regenerate();
    }

    private void SetAllSymbols(bool on)
    {
        _loading = true;
        foreach (var s in Symbols) s.IsOn = on;
        if (on) Special.IsOn = true;
        _loading = false;
        Regenerate();
    }

    private async void Copy()
    {
        if (IsEmpty || CopyHandler is null) return;
        try
        {
            await CopyHandler(_password);
            Copied = true;
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }
        catch (Exception)
        {
            // The clipboard can be busy for a moment; the label simply stays "Copy password".
        }
    }
}
