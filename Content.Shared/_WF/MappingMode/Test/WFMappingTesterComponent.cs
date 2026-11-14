using Robust.Shared.GameStates;

namespace Content.Shared._WF.MappingMode.Test;

/// <summary>
/// The character a mapper walks their map in during a live test. The server shields it from edits and refuses the
/// mapper every edit while they carry it; the client shows the test banner over the gameplay screen.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFMappingTesterComponent : Component
{
    /// <summary>The session's name, for the banner.</summary>
    [AutoNetworkedField]
    public string MapName = string.Empty;

    /// <summary>Game time the test ends on its own.</summary>
    [AutoNetworkedField]
    public TimeSpan Ends;
}
