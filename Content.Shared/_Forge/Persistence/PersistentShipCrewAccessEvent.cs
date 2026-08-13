namespace Content.Shared._Forge.Persistence;

/// <summary>
/// Allows server-side character-bound vessel ACLs to extend normal ship access.
/// </summary>
[ByRefEvent]
public record struct PersistentShipCrewAccessEvent(EntityUid User)
{
    public bool Allowed;
}
