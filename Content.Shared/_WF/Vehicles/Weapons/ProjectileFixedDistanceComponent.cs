using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>On a round fired by a <see cref="ShootAtFixedPointComponent"/> gun. It stops when <see cref="FlyEndTime"/> passes.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ProjectileFixedDistanceComponent : Component
{
    /// <summary>When the round stops. The system takes the component off then and raises <see cref="ProjectileFixedDistanceStopEvent"/>.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FlyEndTime;

    /// <summary>The round ignores every collision except impassable fixtures.</summary>
    [DataField, AutoNetworkedField]
    public bool ArcProj;
}

/// <summary>The furthest this round flies when it is fired by a <see cref="ShootAtFixedPointComponent"/> gun, in tiles.</summary>
[RegisterComponent]
public sealed partial class ProjectileMaxFixedRangeComponent : Component
{
    [DataField(required: true)]
    public float MaxFixedRange;
}

/// <summary>Raised on a round when it reaches the end of its fixed distance.</summary>
[ByRefEvent]
public readonly record struct ProjectileFixedDistanceStopEvent;
