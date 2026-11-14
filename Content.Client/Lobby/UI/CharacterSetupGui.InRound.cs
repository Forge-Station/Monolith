namespace Content.Client.Lobby.UI;

public sealed partial class CharacterSetupGui
{
    /// <summary>
    /// The creator is hosted in its own window during a round, so the lobby close button stays hidden.
    /// </summary>
    public void SetInRound()
    {
        CloseButton.Visible = false;
    }
}
