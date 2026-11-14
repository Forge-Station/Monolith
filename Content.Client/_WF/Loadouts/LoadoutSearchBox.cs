using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WF.Loadouts;

/// <summary>
/// Search field whose escape key clears the query before it gives up focus. A plain LineEdit drops focus on
/// escape without asking, so a handler can't hold on to it.
/// </summary>
public sealed class LoadoutSearchBox : LineEdit
{
    /// <summary>Raised when escape empties the field.</summary>
    public event Action? OnCleared;

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.TextReleaseFocus && HasKeyboardFocus() && Text.Length > 0)
        {
            Clear();
            OnCleared?.Invoke();
            args.Handle();
            return;
        }

        base.KeyBindDown(args);
    }
}
