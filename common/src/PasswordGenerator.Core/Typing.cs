namespace PasswordGenerator.Core;

/// <summary>One item of the typing order in the Change a password window.</summary>
public enum TypingStep { Current, New, Tab, Enter, Wait }

public enum TypingKey { Tab, Enter }

/// <summary>One thing the typer does: type some text, press a key, or pause.</summary>
public abstract record TypingAction
{
    public sealed record Text(string Value) : TypingAction;
    public sealed record Key(TypingKey Which) : TypingAction;
    public sealed record Pause(int Milliseconds) : TypingAction;
}

public static class TypingOrder
{
    public const int MaxSteps = 12;
    public const int WaitMilliseconds = 1000;

    /// <summary>Fits a form with old, new and confirm boxes.</summary>
    public static readonly IReadOnlyList<TypingStep> Default = new[]
    {
        TypingStep.Current, TypingStep.Tab, TypingStep.New, TypingStep.Tab, TypingStep.New, TypingStep.Enter,
    };

    public static string Label(TypingStep step) => step switch
    {
        TypingStep.Current => "Current",
        TypingStep.New => "New",
        TypingStep.Tab => "Tab",
        TypingStep.Enter => "Enter",
        _ => "Wait 1 s",
    };

    /// <summary>Reads a saved order. Unknown names are dropped; a missing order gives the default.</summary>
    public static IReadOnlyList<TypingStep> Parse(IEnumerable<string>? names)
    {
        if (names is null) return Default;
        var steps = new List<TypingStep>();
        foreach (var name in names)
            if (Enum.TryParse<TypingStep>(name, ignoreCase: true, out var step) && Enum.IsDefined(step) && steps.Count < MaxSteps)
                steps.Add(step);
        return steps;
    }

    public static IReadOnlyList<string> Names(IEnumerable<TypingStep> steps) => steps.Take(MaxSteps).Select(s => s.ToString()).ToArray();

    public static string Describe(IEnumerable<TypingStep> steps) => string.Join(", ", steps.Select(Label));

    /// <summary>Why this order cannot be typed right now, or null when it can.</summary>
    public static string? Problem(IReadOnlyList<TypingStep> steps, string current, string @new)
    {
        if (!steps.Any(s => s is TypingStep.Current or TypingStep.New)) return "Add Current or New to the typing order.";
        if (steps.Contains(TypingStep.Current) && current.Length == 0) return "Paste your current password first.";
        if (steps.Contains(TypingStep.New) && @new.Length == 0) return "There is no new password. Switch on at least one element in the main window.";
        return null;
    }

    /// <summary>Turns the order into what the typer does. Throws when <see cref="Problem"/> reports one.</summary>
    public static IReadOnlyList<TypingAction> Plan(IReadOnlyList<TypingStep> steps, string current, string @new)
    {
        if (Problem(steps, current, @new) is { } problem) throw new InvalidOperationException(problem);
        return steps.Select<TypingStep, TypingAction>(step => step switch
        {
            TypingStep.Current => new TypingAction.Text(current),
            TypingStep.New => new TypingAction.Text(@new),
            TypingStep.Tab => new TypingAction.Key(TypingKey.Tab),
            TypingStep.Enter => new TypingAction.Key(TypingKey.Enter),
            _ => new TypingAction.Pause(WaitMilliseconds),
        }).ToArray();
    }
}
