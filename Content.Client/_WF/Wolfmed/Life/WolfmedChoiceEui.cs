using Content.Client.Eui;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Eui;
using JetBrains.Annotations;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client._WF.Wolfmed.Life;

/// <summary>The client half of the server's <c>WolfmedChoiceEui</c>. Closing the window is a no.</summary>
[UsedImplicitly]
public sealed class WolfmedChoiceEui : BaseEui
{
    private readonly WolfmedChoiceWindow _window = new();
    private bool _answered;

    /// <summary>The window, for the layout test.</summary>
    public WolfmedChoiceWindow Window => _window;

    public WolfmedChoiceEui()
    {
        _window.AcceptChoice.OnPressed += _ => Answer(true);
        _window.DenyChoice.OnPressed += _ => Answer(false);
        _window.OnClose += () => Answer(false);
    }

    public override void Opened()
    {
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
    }

    /// <summary>
    /// Opens, or re-centres, once the text is in: centred on an empty body the window grew off the bottom of the
    /// screen when the words arrived (playtest 1).
    /// </summary>
    public override void HandleState(EuiStateBase state)
    {
        if (state is not WolfmedChoiceEuiState choice || _answered)
            return;

        _window.SetChoice(choice.Title, choice.Text, choice.Accept, choice.Deny);
        _window.OpenCentered();

        // OpenCentered measures before the window has a parent, at whatever UI scale it last had; measured again
        // in place, the centre is the player's own scale's.
        _window.Measure(Vector2Helpers.Infinity);
        _window.RecenterWindow(new Vector2(0.5f, 0.5f));
    }

    public override void Closed()
    {
        base.Closed();
        _answered = true;
        _window.Close();
    }

    private void Answer(bool accepted)
    {
        if (_answered)
            return;

        _answered = true;
        SendMessage(new WolfmedChoiceMessage(accepted));
        _window.Close();
    }
}
