namespace Content.Server._Forge.Persistence;

/// <summary>
/// Runtime cache of character-bound access loaded from hangar metadata.
/// </summary>
[RegisterComponent]
public sealed partial class PersistentShipCrewComponent : Component
{
    public HashSet<(Guid PlayerUserId, int CharacterSlot)> Members = [];
}
