using Content.Shared.Physics;

namespace Content.Shared._Mono.Weapons.Hitscan.Components;

/// <summary>A hitscan that keeps going through several targets instead of stopping on the first.</summary>
[RegisterComponent]
public sealed partial class HitscanMultiRaycastComponent : Component
{
    [DataField]
    public float MaxDistance = 20f;

    /// <summary>How many targets the beam may strike before it stops.</summary>
    [DataField]
    public float MaxPierce = 3f;

    /// <summary>Collision layers that stop the beam. None lets it continue until the pierce count.</summary>
    [DataField]
    public CollisionGroup PierceCollisionMask = CollisionGroup.MobMask;

    [DataField]
    public CollisionGroup CollisionMask = CollisionGroup.Opaque;
}
