using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Behaviors;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Shrapnel;

/// <summary>
/// Flings a ring of projectiles out from the entity, like a shrapnel grenade.
/// </summary>
[DataDefinition]
public sealed partial class ShrapnelBehavior : IThresholdBehavior
{
    /// <summary>
    /// Projectile fired as shrapnel.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Proto;

    /// <summary>
    /// How many projectiles to fire, one per even slice of the circle.
    /// </summary>
    [DataField]
    public int Count = 30;

    /// <summary>
    /// Distance from the entity's centre the shrapnel starts at, clear of its own tile and anything left there.
    /// </summary>
    [DataField]
    public float SpawnRadius = 0.9f;

    /// <summary>
    /// Projectile speed.
    /// </summary>
    [DataField]
    public float Speed = 20f;

    /// <summary>
    /// Smallest random velocity added to each projectile, so the ring comes out uneven.
    /// </summary>
    [DataField]
    public float MinJitter = 2f;

    /// <summary>
    /// Largest random velocity added to each projectile.
    /// </summary>
    [DataField]
    public float MaxJitter = 6f;

    public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
    {
        system.EntityManager.System<ShrapnelSystem>().Fling(owner, this);
    }
}
