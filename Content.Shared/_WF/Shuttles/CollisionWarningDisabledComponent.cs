using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shuttles;

/// <summary>
/// Sits on a grid whose crew have switched the collision warning off. The ship is not swept while this
/// is here, and its consoles say so.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CollisionWarningDisabledComponent : Component;

/// <summary>Pilot switched the collision warning on or off.</summary>
[Serializable, NetSerializable]
public sealed class CollisionWarningToggleMessage : BoundUserInterfaceMessage
{
    public bool Enabled;

    public CollisionWarningToggleMessage(bool enabled)
    {
        Enabled = enabled;
    }
}
