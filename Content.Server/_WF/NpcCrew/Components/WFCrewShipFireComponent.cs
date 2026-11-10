namespace Content.Server._WF.NpcCrew.Components;

/// <summary>Preserves the autonomous gunner responsible for a cannon burst or launched projectile.</summary>
[RegisterComponent]
public sealed partial class WFCrewShipFireComponent : Component
{
    /// <summary>The crew member who issued the firing command.</summary>
    public EntityUid Crew;

    /// <summary>Launched rounds retain their autonomous origin after a cannon or gunner changes control.</summary>
    public bool Launched;

    /// <summary>When the weapon's attribution to its last crew command lapses.</summary>
    public TimeSpan Until;
}
