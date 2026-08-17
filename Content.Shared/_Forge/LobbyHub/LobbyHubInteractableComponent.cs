namespace Content.Shared._Forge.LobbyHub;

/// <summary>
/// Marks a client-side lobby hub object that opens a piece of existing lobby UI.
/// </summary>
[RegisterComponent]
public sealed partial class LobbyHubInteractableComponent : Component
{
    [DataField]
    public LobbyHubAction Action;

    [DataField]
    public LocId Prompt = "lobby-hub-prompt-generic";
}
