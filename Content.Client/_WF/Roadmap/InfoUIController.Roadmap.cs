namespace Content.Client.UserInterface.Systems.Info;

public sealed partial class InfoUIController
{
    /// <summary>Raised when the server's rules decision arrives, after any rules popup it asks for is up.</summary>
    public event Action? RulesInformationReceived;

    /// <summary>Raised once the player accepts the rules popup.</summary>
    public event Action? RulesAccepted;

    /// <summary>Whether the rules popup is up and waiting to be accepted.</summary>
    public bool RulesPopupOpen => _rulesPopup != null;
}
