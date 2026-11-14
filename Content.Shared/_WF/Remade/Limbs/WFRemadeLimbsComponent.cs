using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Remade.Limbs;

/// <summary>What a limb gives its mob. A mob's value for a stat is the sum of what its remaining limbs provide.</summary>
public enum WFRemadeLimbStat : byte
{
    /// <summary>Scales its melee damage.</summary>
    Melee,

    /// <summary>Scales its movement speed.</summary>
    Speed,

    /// <summary>Without any, it can't shout or mutter.</summary>
    Voice,

    /// <summary>Scales how far it sees targets.</summary>
    Sight,

    /// <summary>Without any, an Infector can't turn a corpse.</summary>
    Reanimate,

    /// <summary>Without any, a leaper or hopper can't leap.</summary>
    Leap,

    /// <summary>Without any, an exploder has no pustule to burst.</summary>
    Burst,

    /// <summary>Without any, it can't spit, vomit or fire spines.</summary>
    Fire,

    /// <summary>Without any, a tripod can't lash out with its tongue.</summary>
    Grab,
}

/// <summary>One limb: where it sits on the sprite, how it takes hits, and what its loss costs.</summary>
[DataDefinition]
public sealed partial class WFRemadeLimb
{
    [DataField(required: true)]
    public string Id = default!;

    /// <summary>The name in the popup and on the severed limb.</summary>
    [DataField]
    public LocId Name = "wf-remade-limb-chest";

    /// <summary>Sprite layer map keys it draws with. The first one takes the <see cref="Lying"/> state. Defaults to the id.</summary>
    [DataField]
    public List<string> Layers = new();

    /// <summary>What it takes to tear off, before the hit type's fraction. Not a pool: damage past it just severs.</summary>
    [DataField]
    public float Health = 25f;

    /// <summary>Its share of the hits that land on this mob.</summary>
    [DataField]
    public float Weight = 1f;

    /// <summary>Damage dealt to the mob by a hit that lands on this limb is multiplied by this. The torso takes little.</summary>
    [DataField]
    public float Scale = 1f;

    /// <summary>Whether it can be severed. A torso can't.</summary>
    [DataField]
    public bool Severable = true;

    /// <summary>The lump of damage the mob takes when this comes off.</summary>
    [DataField]
    public float Loss = 15f;

    /// <summary>The body parts a player aims at that hit this limb.</summary>
    [DataField]
    public List<TargetBodyPart> Zones = new();

    /// <summary>What this limb gives the mob while attached.</summary>
    [DataField]
    public Dictionary<WFRemadeLimbStat, float> Provides = new();

    /// <summary>The sprite state of the limb lying on the floor. Used for the severed limb and for a dead mob. None means no severed limb.</summary>
    [DataField]
    public string? Lying;

    /// <summary>The sprite state of the severed limb only, when the dead mob draws from one whole sprite. Defaults to <see cref="Lying"/>.</summary>
    [DataField]
    public string? Dropped;

    /// <summary>The sprite state of the limb growing back while the mob stands. None means nothing shows.</summary>
    [DataField]
    public string? Regen;

    /// <summary>The sprite layers this limb draws with.</summary>
    public IEnumerable<string> LayerKeys => Layers.Count > 0 ? Layers : new[] { Id };
}

/// <summary>A mob built of limbs. Hits are shared between them by weight, and cutting one off costs the mob dearly.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFRemadeLimbsComponent : Component
{
    [DataField(required: true)]
    public List<WFRemadeLimb> Limbs = new();

    /// <summary>The lowest a stat can fall to. A stat no limb provides is 1. Without a floor a stat can reach 0.</summary>
    [DataField]
    public Dictionary<WFRemadeLimbStat, float> Floors = new();

    /// <summary>
    /// A hit severs its limb once the limb's total damage passes this share of its health, by the hit's type (the lowest
    /// of the types in the hit). Low severs easily. DS13's attempt_dismemberment has edges at 0.2, tearing at 0.5 and
    /// blunt and burns at 1; slash and piercing sever best here, blunt and heat worst.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> SeverFractions = new()
    {
        ["Slash"] = 0.35f,
        ["Piercing"] = 0.5f,
        ["Blunt"] = 1f,
        ["Heat"] = 1.25f,
    };

    /// <summary>The fraction for any type not listed.</summary>
    [DataField]
    public float DefaultSeverFraction = 1.5f;

    /// <summary>The damage type of the lump taken when a limb comes off.</summary>
    [DataField]
    public ProtoId<DamageTypePrototype> LossType = "Blunt";

    /// <summary>The chance a player's aim holds when they pick a body part. Otherwise the hit goes by weight.</summary>
    [DataField]
    public float AimedChance = 0.75f;

    /// <summary>The sprite layer of the composited dead state, drawn when no remaining limb has a lying state.</summary>
    [DataField]
    public string DeadLayer = "dead";

    /// <summary>The sprite sheet the severed limbs and lying states come from.</summary>
    [DataField(required: true)]
    public ResPath Rsi;

    /// <summary>What a severed limb spawns as.</summary>
    [DataField]
    public EntProtoId Item = "WFRemadeSeveredLimb";

    /// <summary>Blood spilt when a limb comes off.</summary>
    [DataField]
    public float Blood = 12f;

    [DataField]
    public SoundSpecifier? SeverSound = new SoundCollectionSpecifier("MetalThud");

    /// <summary>Ids of the limbs that have come off.</summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> Severed = new();

    /// <summary>Ids of severed limbs growing back now. The client draws them with their regrowth art.</summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> Growing = new();

    /// <summary>Hit damage each limb has taken.</summary>
    [ViewVariables]
    public Dictionary<string, float> Damage = new();

    /// <summary>The limb the hit being applied was routed to, kept between the modify and changed events.</summary>
    [ViewVariables]
    public string? PendingLimb;

    [ViewVariables]
    public TimeSpan PendingAt;

    /// <summary>Vision radii the mob started with, kept while its sight is cut.</summary>
    [ViewVariables]
    public float? BaseVision;

    [ViewVariables]
    public float? BaseAggroVision;
}

/// <summary>A limb that has come off, lying on the floor. The client draws it from the mob's sprite sheet.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFSeveredLimbComponent : Component
{
    [DataField, AutoNetworkedField]
    public ResPath Rsi;

    [DataField, AutoNetworkedField]
    public string State = string.Empty;

    /// <summary>The mob it came off. Server only.</summary>
    [ViewVariables]
    public EntityUid? Source;

    /// <summary>Which of its limbs this is. Server only.</summary>
    [ViewVariables]
    public string Limb = string.Empty;
}

/// <summary>A severed limb a mender has a tentacle on, being dragged back to it. The client draws the tentacle.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFReclaimTentacleComponent : Component
{
    [AutoNetworkedField]
    public EntityUid Puller;
}
