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

public enum ChangePhase { Idle, Typing, Done }

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
    private string _doneText = "";
    private string _error = "";
    private string? _typedNew;
    private bool _answerReminder;

    public ChangePasswordViewModel(MainViewModel main, SecretFile store, IKeyboardTyper typer, IEnumerable<string>? savedOrder)
    {
        _main = main;
        _store = store;
        _typer = typer;
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
        YesCommand = new RelayCommand(Worked);
        NoCommand = new RelayCommand(NotWorked);
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
    public ICommand YesCommand { get; }
    public ICommand NoCommand { get; }

    /// <summary>Set by the window: reads text from the clipboard.</summary>
    public Func<Task<string?>>? ClipboardReader { get; set; }
    /// <summary>Raised when the typing order changes, so it can be saved.</summary>
    public event Action<IReadOnlyList<string>>? OrderChanged;

    /// <summary>The password in use now. Saved encrypted the moment it changes.</summary>
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
    /// <summary>
    /// The new password this window is about. Once it has been typed into the other window it is held here
    /// until the user answers "Did the change work?", whatever the generator does in the meantime.
    /// </summary>
    public string NewPassword => _typedNew ?? _main.Password;
    public string NewDisplay => _showNew ? NewPassword : string.Concat(Enumerable.Repeat(Mask, NewPassword.Length));
    public string NewHint => _typedNew is not null ? "the one that was just typed"
        : _main.Password.Length == 0 ? "no elements are switched on" : $"from the generator, {_main.Password.Length} characters";
    public string StoreNote => _store.CanStore
        ? "Kept after a restart, locked with your Windows sign-in."
        : "On this system it is kept only until the app closes.";

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
            Raise(nameof(IsDone));
            NewCommand.RaiseCanExecuteChanged();
            Changed();
        }
    }

    public bool IsIdle => _phase == ChangePhase.Idle;
    public bool IsTyping => _phase == ChangePhase.Typing;
    public bool IsDone => _phase == ChangePhase.Done;
    public string DoneText { get => _doneText; private set => Set(ref _doneText, value); }

    /// <summary>Shown when the user tries to close the window before answering "Did the change work?".</summary>
    public bool AnswerReminder { get => _answerReminder; private set => Set(ref _answerReminder, value); }

    /// <summary>True while the typed password would be lost by closing. The window refuses to close then.</summary>
    public bool MustAnswerFirst()
    {
        if (!IsDone) return false;
        AnswerReminder = true;
        return true;
    }

    /// <summary>What stands in the way of typing, or what went wrong last time. Empty when all is well.</summary>
    public string Problem =>
        _error.Length > 0 ? _error
        : !_typer.Available ? "Typing into other windows works on Windows only."
        : TypingOrder.Problem(CurrentSteps, _current, NewPassword)
          ?? (_target is null ? "Open the website or app first, and click into its first password box." : "");

    public bool HasProblem => Problem.Length > 0;
    public bool ShowHint => !HasProblem;
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

    /// <summary>Only for the self-test: shows the "Did the change work?" state without typing anything.</summary>
    public void ShowDoneForSelfTest()
    {
        Hold(_main.Password);
        DoneText = "Typed into “Sample window”: " + TypingOrder.Describe(CurrentSteps) + ".";
        Phase = ChangePhase.Done;
    }

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
        var typed = _main.Password; // captured now: this exact text is what reaches the other window
        var plan = TypingOrder.Plan(steps, _current, typed);
        _error = "";
        Phase = ChangePhase.Typing;
        var result = await _typer.TypeAsync(target.Handle, plan);
        if (result.Ok)
        {
            Hold(typed);
            DoneText = $"Typed into “{target.Title}”: {TypingOrder.Describe(steps)}.";
            Phase = ChangePhase.Done;
        }
        else
        {
            _error = result.Message;
            Phase = ChangePhase.Idle;
        }
    }

    /// <summary>Keeps the password that was typed, so the generator cannot replace it before the user answers.</summary>
    private void Hold(string? typed)
    {
        _typedNew = typed;
        Raise(nameof(NewPassword));
        Raise(nameof(NewDisplay));
        Raise(nameof(NewHint));
    }

    /// <summary>The change worked: the password that was typed becomes the current one, and a fresh one is generated.</summary>
    private void Worked()
    {
        var typed = NewPassword;
        ShowCurrent = false;
        ShowNew = false;
        AnswerReminder = false;
        Current = typed;
        Hold(null);
        _main.Regenerate();
        Phase = ChangePhase.Idle;
    }

    /// <summary>The change did not work: the current password stays as it is.</summary>
    private void NotWorked()
    {
        AnswerReminder = false;
        Hold(null);
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
        Raise(nameof(CanType));
        TypeCommand.RaiseCanExecuteChanged();
    }
}
