using Robust.Shared.Network;

namespace Content.Shared._WF.Access;

/// <summary>
/// Stamped on an ID card when its owner spawns, so services can tell whose ID it is.
/// </summary>
[RegisterComponent]
public sealed partial class IdCardOwnerComponent : Component
{
    /// <summary>
    /// Session the ID was issued to.
    /// </summary>
    [ViewVariables]
    public NetUserId UserId;

    /// <summary>
    /// Character the ID was issued to, for display.
    /// </summary>
    [ViewVariables]
    public string CharacterName = string.Empty;
}
