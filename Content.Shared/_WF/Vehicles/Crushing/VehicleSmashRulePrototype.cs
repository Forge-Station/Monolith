using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Crushing;

/// <summary>Gives the entity prototypes it matches the behaviour of a <see cref="VehicleSmashableComponent"/>.</summary>
[Prototype]
public sealed partial class VehicleSmashRulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Of two rules that match the same entity prototype the higher priority wins; equal priorities are a data error.</summary>
    [DataField]
    public int Priority;

    /// <summary>Matches these prototypes and every prototype that inherits from one of them.</summary>
    [DataField]
    public List<EntProtoId> Parents = new();

    /// <summary>Matches prototypes that carry the components or tags of the list; only components and tags are read.</summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>Never matches these prototypes or any prototype that inherits from one of them.</summary>
    [DataField]
    public List<EntProtoId> ExcludeParents = new();

    /// <summary>Never matches prototypes that carry the components or tags of the list.</summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>Delete the entity after the hit if the damage left it standing.</summary>
    [DataField]
    public bool DeleteOnHit = true;

    /// <summary>Blunt damage of the hit; the server lowers it to what the entity needs to be destroyed.</summary>
    [DataField]
    public double DamageOnHit = 1000;

    /// <summary>Speed multiplier the vehicle gets after smashing it.</summary>
    [DataField]
    public float SlowdownMultiplier = 0.5f;

    /// <summary>Seconds the slowdown lasts.</summary>
    [DataField]
    public float SlowdownDuration;

    /// <summary>Played when the entity is smashed.</summary>
    [DataField]
    public SoundSpecifier? SmashSound;

    /// <summary>A door is smashed only while unpowered; a powered one opens for a driver with access, or holds.</summary>
    [DataField]
    public bool RequiresDoorUnpowered;

    /// <summary>A vehicle lighter than this is stopped and leaves the entity alone.</summary>
    [DataField]
    public VehicleWeightClass? MinDestroyWeightClass;

    /// <summary>A vehicle lighter than this destroys the entity and is stopped; a heavier one drives on.</summary>
    [DataField]
    public VehicleWeightClass? MinContinueWeightClass;

    /// <summary>Only vehicles lighter than this are slowed.</summary>
    [DataField]
    public VehicleWeightClass? SlowdownBelowWeightClass;

    /// <summary>The smash settings this rule gives.</summary>
    public VehicleSmashSettings ToSettings()
    {
        return new VehicleSmashSettings(
            DeleteOnHit,
            DamageOnHit,
            SlowdownMultiplier,
            SlowdownDuration,
            SmashSound,
            RequiresDoorUnpowered,
            MinDestroyWeightClass,
            MinContinueWeightClass,
            SlowdownBelowWeightClass);
    }
}
