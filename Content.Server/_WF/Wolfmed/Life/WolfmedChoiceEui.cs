using Content.Server.EUI;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Eui;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// A yes/no dialog with exact text (M1a, plan §5.4): the Succumb dialog and the "left alive but empty" one.
/// The answer is acted on once; closing the window is a no.
/// </summary>
public sealed class WolfmedChoiceEui : BaseEui
{
    private readonly WolfmedChoiceEuiState _state;
    private readonly Action<bool> _answer;
    private bool _answered;

    public WolfmedChoiceEui(WolfmedChoiceEuiState state, Action<bool> answer)
    {
        _state = state;
        _answer = answer;
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState() => _state;

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is WolfmedChoiceMessage choice)
            Answer(choice.Accepted);

        CloseOnce();
    }

    public override void Closed()
    {
        base.Closed();
        Answer(false);
    }

    /// <summary>Closes the window without an answer, when the choice no longer applies.</summary>
    public void Withdraw()
    {
        _answered = true;
        CloseOnce();
    }

    private void Answer(bool accepted)
    {
        if (_answered)
            return;

        _answered = true;
        _answer(accepted);
    }

    private void CloseOnce()
    {
        if (!IsShutDown)
            Close();
    }
}
