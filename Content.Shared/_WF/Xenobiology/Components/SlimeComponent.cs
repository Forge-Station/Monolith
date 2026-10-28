using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenobiology.Components;

/// <summary>
/// A xenobiology slime: its breed, what it can mutate into, its tamer and the target it is latched onto.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SlimeComponent : Component
{
    /// <summary>
    /// The mob offspring spawn as.
    /// </summary>
    [DataField]
    public EntProtoId DefaultSlimeProto = "MobSlimeXenobioBaby";

    [DataField, AutoNetworkedField]
    public Color SlimeColor = Color.White;

    [DataField(required: true), AutoNetworkedField]
    public ProtoId<BreedPrototype> Breed = "GreyMutation";

    /// <summary>
    /// Extract used when the breed can't be found.
    /// </summary>
    [DataField]
    public EntProtoId DefaultExtract = "GreySlimeExtract";

    /// <summary>
    /// Breeds an offspring can mutate into.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<BreedPrototype>> PotentialMutations = new();

    /// <summary>
    /// Holds what the slime has eaten until it splits.
    /// </summary>
    [ViewVariables]
    public Container Stomach = default!;

    [DataField]
    public int MaxContainedEntities = 1;

    [DataField]
    public TimeSpan LatchDoAfterDuration = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whoever tamed this slime; it won't hunt them unless hungry and grown.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Tamer;

    [DataField]
    public EntProtoId TameEffect = "EffectHearts";

    [DataField, AutoNetworkedField]
    public EntityUid? LatchedTarget;

    [DataField, AutoNetworkedField]
    public int MaxOffspring = 4;

    [DataField, AutoNetworkedField]
    public int ExtractsProduced = 1;

    /// <summary>
    /// Chance, per offspring, of mutating.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MutationChance = 0.45f;

    /// <summary>
    /// Hunger needed to split.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MitosisHunger = 125f;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public TimeSpan NextUpdateTime;

    /// <summary>
    /// How close to <see cref="MitosisHunger"/> the slime starts shaking.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float JitterDifference = 25f;

    [DataField, AutoNetworkedField]
    public bool ShouldHaveShader;

    [DataField, AutoNetworkedField]
    public string? Shader;

    [DataField]
    public SoundSpecifier MitosisSound = new SoundPathSpecifier("/Audio/Effects/Fluids/splat.ogg");

    [DataField]
    public SoundSpecifier EatSound = new SoundPathSpecifier("/Audio/Voice/Talk/slime.ogg");
}

/// <summary>
/// Marks an entity a slime is latching onto so other slimes leave it alone.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BeingLatchedComponent : Component;

/// <summary>
/// Damage a latched slime deals each second to the target it feeds on.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlimeDamageOvertimeComponent : Component
{
    [DataField]
    public EntityUid? SourceEntityUid;

    [DataField]
    public Content.Shared.Damage.DamageSpecifier Damage = new()
    {
        DamageDict = new() { { "Caustic", 2.5 } },
    };
}

/// <summary>
/// A spawner that replaces itself with a slime of <see cref="Breed"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PendingSlimeSpawnComponent : Component
{
    [DataField]
    public EntProtoId BasePrototype = "MobSlimeXenobioBaby";

    [DataField]
    public ProtoId<BreedPrototype> Breed = "GreyMutation";
}

/// <summary>
/// A slime extract. Its reactions live on its <c>Reactive</c> component.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SlimeExtractComponent : Component
{
    /// <summary>
    /// Set once the extract has reacted and stopped reacting.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Spent;
}
