using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using PasswordGenerator.App.Services;
using PasswordGenerator.Core;

namespace PasswordGenerator.App.ViewModels;

/// <summary>One chip of the typing order.</summary>
public sealed class StepVm
{
    public StepVm(TypingStep step, Action<StepVm, int> move, Action<StepVm> remove)
    {
        Step = step;
        Label = TypingOrder.Label(step);
        EarlierCommand = new RelayCommand(() => move(this, -1));
        LaterCommand = new RelayCommand(() => move(this, 1));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public TypingStep Step { get; }
    public string Label { get; }
    public bool IsCurrent => Step == TypingStep.Current;
    public bool IsNew => Step == TypingStep.New;
    public string EarlierLabel => "Move " + Label + " earlier";
    public string LaterLabel => "Move " + Label + " later";
    public string RemoveLabel => "Remove " + Label;
    public ICommand EarlierCommand { get; }
    public ICommand LaterCommand { get; }
    public ICommand RemoveCommand { get; }
}

public sealed record AdderVm(string Label, ICommand Command);

public enum ChangePhase { Idle, Typing }

/// <summary>The Change a password window: current password, new password, typing order, and typing it.</summary>
public sealed class ChangePasswordViewModel : ObservableObject
{
    private const string Mask = "•";
    private readonly MainViewModel _main;
    private readonly SecretFile _store;
    private readonly IKeyboardTyper _typer;
    private string _current;
    private bool _showCurrent;
    private bool _showNew;
    private TargetWindow? _target;
    private ChangePhase _phase = ChangePhase.Idle;
    private string _lastTyped = "";
    private string _error = "";
    private TypingDelays _delays;

    public ChangePasswordViewModel(MainViewModel main, SecretFile store, IKeyboardTyper typer, IEnumerable<string>? savedOrder, TypingDelays? delays = null)
    {
        _main = main;
        _store = store;
        _typer = typer;
        _delays = (delays ?? new TypingDelays()).Normalized();
        _current = store.Load();
        Steps = new ObservableCollection<StepVm>(TypingOrder.Parse(savedOrder).Select(Make));
        Adders = Enum.GetValues<TypingStep>()
            .Select(step => new AdderVm("+ " + TypingOrder.Label(step), new RelayCommand(() => Add(step))))
            .ToArray();

        ToggleCurrentCommand = new RelayCommand(() => ShowCurrent = !ShowCurrent);
        ToggleNewCommand = new RelayCommand(() => ShowNew = !ShowNew);
        ForgetCommand = new RelayCommand(() => { Current = ""; ShowCurrent = false; });
        PasteCommand = new RelayCommand(Paste);
        NewCommand = new RelayCommand(() => { if (IsIdle) _main.Regenerate(); }, () => IsIdle);
        ResetCommand = new RelayCommand(() => SetSteps(TypingOrder.Default));
        TypeCommand = new RelayCommand(Type, () => CanType);
        CharDelayDownCommand = new RelayCommand(() => CharDelayMs -= TypingDelays.CharStepMs);
        CharDelayUpCommand = new RelayCommand(() => CharDelayMs += TypingDelays.CharStepMs);
        TabDelayDownCommand = new RelayCommand(() => TabDelayMs -= TypingDelays.KeyStepMs);
        TabDelayUpCommand = new RelayCommand(() => TabDelayMs += TypingDelays.KeyStepMs);
        _main.PropertyChanged += MainChanged;
        RefreshTarget();
    }

    public ObservableCollection<StepVm> Steps { get; }
    public IReadOnlyList<AdderVm> Adders { get; }
    public ICommand ToggleCurrentCommand { get; }
    public ICommand ToggleNewCommand { get; }
    public ICommand ForgetCommand { get; }
    public ICommand PasteCommand { get; }
    public RelayCommand NewCommand { get; }
    public ICommand ResetCommand { get; }
    public RelayCommand TypeCommand { get; }
    public ICommand CharDelayDownCommand { get; }
    public ICommand CharDelayUpCommand { get; }
    public ICommand TabDelayDownCommand { get; }
    public ICommand TabDelayUpCommand { get; }

    /// <summary>Set by the window: reads text from the clipboard.</summary>
    public Func<Task<string?>>? ClipboardReader { get; set; }
    /// <summary>Raised when the typing order changes, so it can be saved.</summary>
    public event Action<IReadOnlyList<string>>? OrderChanged;
    /// <summary>Raised when a typing delay changes, so it can be saved.</summary>
    public event Action<TypingDelays>? DelaysChanged;

    /// <summary>Pause after every typed character, 0 to 500 ms.</summary>
    public int CharDelayMs
    {
        get => _delays.CharMs;
        set => SetDelays(_delays with { CharMs = value });
    }

    /// <summary>Pause after every Tab or Enter, 0 to 5000 ms.</summary>
    public int TabDelayMs
    {
        get => _delays.KeyMs;
        set => SetDelays(_delays with { KeyMs = value });
    }

    public string CharDelayText => _delays.CharMs + " ms";
    public string TabDelayText => _delays.KeyMs + " ms";
    public TypingDelays Delays => _delays;

    private void SetDelays(TypingDelays wanted)
    {
        var next = wanted.Normalized();
        if (next == _delays) return;
        _delays = next;
        Raise(nameof(CharDelayMs));
        Raise(nameof(TabDelayMs));
        Raise(nameof(CharDelayText));
        Raise(nameof(TabDelayText));
        DelaysChanged?.Invoke(next);
    }

    /// <summary>
    /// The password in use now. Saved encrypted the moment it changes. Only the user changes it
    /// (typing in the box, Paste, Forget it): the app never replaces it on its own.
    /// </summary>
    public string Current
    {
        get => _current;
        set
        {
            value ??= "";
            if (!Set(ref _current, value)) return;
            _store.Save(value);
            _error = "";
            Changed();
        }
    }

    public bool ShowCurrent
    {
        get => _showCurrent;
        set { if (Set(ref _showCurrent, value)) Raise(nameof(ShowCurrentLabel)); }
    }

    public string ShowCurrentLabel => _showCurrent ? "Hide" : "Show";

    public bool ShowNew
    {
        get => _showNew;
        set { if (Set(ref _showNew, value)) { Raise(nameof(ShowNewLabel)); Raise(nameof(NewDisplay)); } }
    }

    public string ShowNewLabel => _showNew ? "Hide" : "Show";
    /// <summary>The generator's password. Typing it does not replace it; it stays until New or Regenerate is pressed.</summary>
    public string NewPassword => _main.Password;
    public string NewDisplay => _showNew ? NewPassword : string.Concat(Enumerable.Repeat(Mask, NewPassword.Length));
    public string NewHint => _main.Password.Length == 0 ? "no elements are switched on" : $"from the generator, {_main.Password.Length} characters";
    public string StoreNote => _store.CanStore
        ? "Kept after a restart, locked with your Windows sign-in. It changes only when you change it."
        : "On this system it is kept only until the app closes. It changes only when you change it.";

    public string TargetTitle => !_typer.Available ? "Typing into other windows works on Windows only"
        : _target?.Title ?? "No other window is open";
    public bool HasTarget => _target is not null;

    public ChangePhase Phase
    {
        get => _phase;
        private set
        {
            if (!Set(ref _phase, value)) return;
            Raise(nameof(IsIdle));
            Raise(nameof(IsTyping));
            NewCommand.RaiseCanExecuteChanged();
            Changed();
        }
    }

    public bool IsIdle => _phase == ChangePhase.Idle;
    public bool IsTyping => _phase == ChangePhase.Typing;

    /// <summary>What was typed last and where, or empty before the first typing.</summary>
    public string LastTyped
    {
        get => _lastTyped;
        private set { if (Set(ref _lastTyped, value)) Changed(); }
    }

    public bool HasLastTyped => _lastTyped.Length > 0;

    /// <summary>What stands in the way of typing, or what went wrong last time. Empty when all is well.</summary>
    public string Problem =>
        _error.Length > 0 ? _error
        : !_typer.Available ? "Typing into other windows works on Windows only."
        : TypingOrder.Problem(CurrentSteps, _current, NewPassword)
          ?? (_target is null ? "Open the website or app first, and click into its first password box." : "");

    public bool HasProblem => Problem.Length > 0;
    public bool ShowHint => !HasProblem && !HasLastTyped;
    public bool ShowLastTyped => !HasProblem && HasLastTyped;
    public bool CanType => IsIdle && _typer.Available && _target is not null && TypingOrder.Problem(CurrentSteps, _current, NewPassword) is null;

    private IReadOnlyList<TypingStep> CurrentSteps => Steps.Select(s => s.Step).ToArray();

    /// <summary>Looks again for the window used last. Called when this window comes to the front.</summary>
    public void RefreshTarget()
    {
        if (!IsIdle) return;
        var found = _typer.FindTarget();
        if (found?.Handle == _target?.Handle && found?.Title == _target?.Title) return;
        _target = found;
        _error = "";
        Raise(nameof(TargetTitle));
        Raise(nameof(HasTarget));
        Changed();
    }

    public void Detach() => _main.PropertyChanged -= MainChanged;

    private async void Type()
    {
        if (!CanType || _target is not { } target) return;

        // Type only into the window whose name is on screen. If a different window is in front of it now
        // (or it has closed), show the new one and let the user decide; never switch recipients silently.
        var found = _typer.FindTarget();
        if (found?.Handle != target.Handle)
        {
            _target = found;
            _error = found is null
                ? "The window to type into is no longer there. Nothing was typed."
                : $"The window to type into changed to “{found.Title}”. Check it, then press the button again. Nothing was typed.";
            Raise(nameof(TargetTitle));
            Raise(nameof(HasTarget));
            Changed();
            return;
        }

        var steps = CurrentSteps;
        var plan = TypingOrder.Plan(steps, _current, _main.Password);
        _error = "";
        Phase = ChangePhase.Typing;
        var result = await _typer.TypeAsync(target.Handle, plan, _delays);
        // Nothing else happens after typing: the current password and the new password both stay as they are.
        if (result.Ok) LastTyped = $"Typed into “{target.Title}”: {TypingOrder.Describe(steps)}. Your current password was not changed.";
        else _error = result.Message;
        Phase = ChangePhase.Idle;
    }

    private async void Paste()
    {
        if (ClipboardReader is null) return;
        try
        {
            var text = await ClipboardReader();
            if (!string.IsNullOrEmpty(text)) Current = text.Trim('\r', '\n');
        }
        catch (Exception)
        {
            // The clipboard can be busy or hold something that is not text; the box simply stays as it is.
        }
    }

    private StepVm Make(TypingStep step) => new(step, Move, Remove);

    private void Add(TypingStep step)
    {
        if (Steps.Count >= TypingOrder.MaxSteps) return;
        Steps.Add(Make(step));
        OrderEdited();
    }

    private void Move(StepVm step, int by)
    {
        var from = Steps.IndexOf(step);
        var to = from + by;
        if (from < 0 || to < 0 || to >= Steps.Count) return;
        Steps.Move(from, to);
        OrderEdited();
    }

    private void Remove(StepVm step)
    {
        if (Steps.Remove(step)) OrderEdited();
    }

    private void SetSteps(IEnumerable<TypingStep> steps)
    {
        Steps.Clear();
        foreach (var step in steps) Steps.Add(Make(step));
        OrderEdited();
    }

    private void OrderEdited()
    {
        _error = "";
        OrderChanged?.Invoke(TypingOrder.Names(CurrentSteps));
        Changed();
    }

    private void MainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Password)) return;
        Raise(nameof(NewDisplay));
        Raise(nameof(NewHint));
        Changed();
    }

    private void Changed()
    {
        Raise(nameof(Problem));
        Raise(nameof(HasProblem));
        Raise(nameof(ShowHint));
        Raise(nameof(HasLastTyped));
        Raise(nameof(ShowLastTyped));
        Raise(nameof(CanType));
        TypeCommand.RaiseCanExecuteChanged();
    }
}
