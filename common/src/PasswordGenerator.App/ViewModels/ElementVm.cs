using System.Windows.Input;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.ViewModels;

/// <summary>One element the password can use (uppercase, lowercase, digits or special characters).</summary>
public sealed class ElementVm : ObservableObject
{
    private readonly Action _changed;
    private bool _isOn;
    private int _min;

    public ElementVm(string label, string shortLabel, string sample, bool isOn, int min, Action changed)
    {
        Label = label;
        ShortLabel = shortLabel;
        Sample = sample;
        _isOn = isOn;
        _min = min;
        _changed = changed;
        IncCommand = new RelayCommand(() => Min++);
        DecCommand = new RelayCommand(() => Min--);
    }

    public string Label { get; }
    public string ShortLabel { get; }
    public string Sample { get; }
    public string ToggleLabel => "Use " + Label.ToLowerInvariant();
    public string IncLabel => "More " + Label.ToLowerInvariant() + " at minimum";
    public string DecLabel => "Fewer " + Label.ToLowerInvariant() + " at minimum";
    public ICommand IncCommand { get; }
    public ICommand DecCommand { get; }

    public bool IsOn
    {
        get => _isOn;
        set { if (Set(ref _isOn, value)) _changed(); }
    }

    public int Min
    {
        get => _min;
        set { if (Set(ref _min, Math.Clamp(value, 0, GeneratorOptions.MaxMinimum))) _changed(); }
    }
}

/// <summary>One special character chip.</summary>
public sealed class SymbolVm : ObservableObject
{
    private bool _isOn;
    private bool _isActive;

    public SymbolVm(char c, bool isOn, Action<SymbolVm> clicked)
    {
        Char = c.ToString();
        _isOn = isOn;
        ToggleCommand = new RelayCommand(() => clicked(this));
    }

    public string Char { get; }
    public string Label => "Special character " + Char;
    public ICommand ToggleCommand { get; }

    /// <summary>The user's choice for this character.</summary>
    public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }

    /// <summary>Chosen and special characters are switched on, so it can appear in the password.</summary>
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

/// <summary>One character of the generated password, for colouring.</summary>
public sealed record CharVm(string Text, bool IsDigit, bool IsSymbol);
