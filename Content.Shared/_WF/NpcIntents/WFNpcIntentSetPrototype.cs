using Content.Shared.Body.Part;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.NpcIntents;

/// <summary>How a fight with a player ends, from the NPC's side.</summary>
public enum WFNpcIntentKind : byte
{
    /// <summary>Aims, demands a sum and backs off once paid; fights if refused.</summary>
    HoldUp,

    /// <summary>Fights until the target is down, then takes cash and a valuable and leaves.</summary>
    Mug,

    /// <summary>Fights until the target is down, says its piece and leaves.</summary>
    BeatDown,

    /// <summary>Fights until the target is down, takes a limb or an organ and leaves; the victim lives.</summary>
    Harvest,

    /// <summary>Warns, fires past the target, then chases it out; stops once it is gone or down.</summary>
    WarnOff,

    /// <summary>Fights to the death. The default for anything with no intent.</summary>
    Lethal,
}

/// <summary>One endpoint a faction can roll, and how likely it is.</summary>
[DataDefinition]
public sealed partial class WFNpcIntentChoice
{
    [DataField(required: true)]
    public WFNpcIntentKind Kind;

    [DataField]
    public float Weight = 1f;
}

/// <summary>A faction's weighted endpoints, rolled once per engagement, and the numbers and lines they use.</summary>
[Prototype("wfNpcIntentSet")]
public sealed partial class WFNpcIntentSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The endpoints and their weights.</summary>
    [DataField(required: true)]
    public List<WFNpcIntentChoice> Intents = new();

    /// <summary>Seconds a player is left alone after an endpoint, unless they attack first.</summary>
    [DataField]
    public float Cooldown = 600f;

    /// <summary>What a refused hold-up turns into.</summary>
    [DataField]
    public WFNpcIntentKind Refusal = WFNpcIntentKind.Mug;

    /// <summary>The fewest thalers a hold-up asks for.</summary>
    [DataField]
    public int DemandMin = 50;

    /// <summary>The most thalers a hold-up asks for.</summary>
    [DataField]
    public int DemandMax = 200;

    /// <summary>Seconds a held-up player has to pay before it turns into a fight.</summary>
    [DataField]
    public float DemandSeconds = 25f;

    /// <summary>Tiles a held-up player may back off before it counts as running.</summary>
    [DataField]
    public float RunRange = 9f;

    /// <summary>Seconds a warned player has to leave.</summary>
    [DataField]
    public float WarnSeconds = 8f;

    /// <summary>Tiles away that counts as having left.</summary>
    [DataField]
    public float WarnRange = 14f;

    /// <summary>Chance a mugger takes the victim's weapon before another valuable.</summary>
    [DataField]
    public float TakeWeaponChance = 0.3f;

    /// <summary>Chance a harvest takes a limb rather than an organ.</summary>
    [DataField]
    public float HarvestLimbChance;

    /// <summary>Organ slots a harvest may take from.</summary>
    [DataField]
    public List<string> HarvestOrgans = new() { "kidneys", "liver" };

    /// <summary>Limbs a harvest may take.</summary>
    [DataField]
    public List<BodyPartType> HarvestLimbs = new() { BodyPartType.Arm, BodyPartType.Leg };

    [DataField]
    public List<LocId> DemandLines = new() { "wf-npc-intent-demand-1", "wf-npc-intent-demand-2", "wf-npc-intent-demand-3" };

    [DataField]
    public List<LocId> PaidLines = new() { "wf-npc-intent-paid-1", "wf-npc-intent-paid-2" };

    [DataField]
    public List<LocId> ShortLines = new() { "wf-npc-intent-short-1", "wf-npc-intent-short-2" };

    [DataField]
    public List<LocId> RefusedLines = new() { "wf-npc-intent-refused-1", "wf-npc-intent-refused-2" };

    [DataField]
    public List<LocId> MugLines = new() { "wf-npc-intent-mug-1", "wf-npc-intent-mug-2" };

    [DataField]
    public List<LocId> BeatLines = new() { "wf-npc-intent-beat-1", "wf-npc-intent-beat-2" };

    [DataField]
    public List<LocId> HarvestLines = new() { "wf-npc-intent-harvest-1", "wf-npc-intent-harvest-2" };

    [DataField]
    public List<LocId> WarnLines = new() { "wf-npc-intent-warn-1", "wf-npc-intent-warn-2" };

    [DataField]
    public List<LocId> WarnOffLines = new() { "wf-npc-intent-warn-off-1", "wf-npc-intent-warn-off-2" };
}
