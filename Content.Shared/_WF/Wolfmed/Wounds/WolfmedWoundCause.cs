using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>What dealt the damage a wound is about to be created from; flags, since one hit can be several.</summary>
// A buckshot pellet is Projectile and Fragment; an animal's swipe is Unarmed and Bite. Derived by
// WolfmedWoundRuleSystem.GetCause from the hit's origin, tool and explosion flag, and overridden per entity by
// WolfmedDamageCauseComponent.
[Flags, Serializable, NetSerializable]
public enum WolfmedWoundCause : ushort
{
    None = 0,

    /// <summary>A bullet, pellet or bolt: the tool carries ProjectileComponent.</summary>
    Projectile = 1 << 0,

    /// <summary>A projectile that leaves debris behind: buckshot, flechettes, nails. Set by data.</summary>
    Fragment = 1 << 1,

    /// <summary>A beam: the hit's tool is a hitscan entity, not a projectile. P6 derives it.</summary>
    Hitscan = 1 << 2,

    Explosion = 1 << 3,

    /// <summary>A held weapon swung by someone.</summary>
    Melee = 1 << 4,

    /// <summary>The attacker's own body: fists, claws, a simple mob's attack.</summary>
    Unarmed = 1 << 5,

    /// <summary>Teeth. Set by data on the biting mob.</summary>
    Bite = 1 << 6,

    /// <summary>A thrown item landing on someone.</summary>
    Thrown = 1 << 7,

    /// <summary>No attacker and no tool: fire, pressure, chemistry, falls. The fallback.</summary>
    Environmental = 1 << 8,

    /// <summary>Deliberate surgical damage. Set by data on the surgery tool.</summary>
    Surgery = 1 << 9,
}
