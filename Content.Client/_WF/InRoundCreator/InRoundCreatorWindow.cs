using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.InRoundCreator;

/// <summary>
/// Holds the character creator during a round. A window, so chat and the game stay usable beside it.
/// </summary>
public sealed class InRoundCreatorWindow : DefaultWindow
{
    /// <summary>Runs instead of closing, so the title-bar X can ask about unsaved edits first.</summary>
    public Action? CloseRequested;

    public override void Close()
    {
        if (CloseRequested != null)
        {
            CloseRequested();
            return;
        }

        base.Close();
    }

    /// <summary>Closes without asking.</summary>
    public void ForceClose()
    {
        base.Close();
    }
}