namespace Content.Shared._Forge.LobbyHub;

/// <summary>
/// Client-only lobby hub interaction. The server never runs these actions.
/// </summary>
public enum LobbyHubAction : byte
{
    CharacterSetup,
    ReadyOrJoin,
    Observe,
    Options,
    AHelp,
    Sponsor,
    Vote,
    Leave
}
