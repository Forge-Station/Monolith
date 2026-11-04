using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Minigames.Games;

/// <summary>Lockbox: past guesses with their feedback and a code entry.</summary>
public sealed class LockboxPanel : MinigamePanel
{
    private readonly Label _tries = new() { HorizontalAlignment = HAlignment.Center };
    private readonly BoxContainer _history = new() { Orientation = LayoutOrientation.Vertical };
    private readonly LineEdit _entry = new() { MinWidth = 120 };
    private readonly Button _try;
    private int _digits = MinigameRules.LockboxDigits;

    public LockboxPanel()
    {
        AddChild(new Label { Text = Loc.GetString("wf-minigame-lock-rules"), HorizontalAlignment = HAlignment.Center });
        AddChild(_tries);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_history);
        AddChild(scroll);

        var row = Row();
        _entry.PlaceHolder = Loc.GetString("wf-minigame-lock-placeholder");
        _entry.IsValid = text => text.Length <= _digits && IsDigits(text);
        _entry.OnTextEntered += _ => Submit();
        _try = MakeButton(Loc.GetString("wf-minigame-lock-try"), Submit);
        row.AddChild(_entry);
        row.AddChild(_try);
        AddChild(row);
    }

    private static bool IsDigits(string text)
    {
        foreach (var c in text)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }

    private void Submit()
    {
        if (_entry.Text.Length != _digits || !int.TryParse(_entry.Text, out var code))
            return;

        _entry.Text = string.Empty;
        Move(code);
    }

    public override void UpdateState(MinigameState state)
    {
        if (state.View is not LockboxView view)
            return;

        _digits = view.Digits;
        _tries.Text = view.Code is { } code
            ? Loc.GetString("wf-minigame-lock-code", ("code", code.ToString("D" + view.Digits)))
            : Loc.GetString("wf-minigame-lock-tries", ("left", view.TriesLeft));

        _history.RemoveAllChildren();
        foreach (var guess in view.Guesses)
        {
            _history.AddChild(new Label
            {
                Text = Loc.GetString("wf-minigame-lock-guess",
                    ("code", guess.Code.ToString("D" + view.Digits)), ("hits", guess.Hits), ("near", guess.Near)),
                HorizontalAlignment = HAlignment.Center,
            });
        }

        var playing = state.Status == MinigameStatus.Playing;
        _entry.Editable = playing;
        _try.Disabled = !playing;
    }
}
