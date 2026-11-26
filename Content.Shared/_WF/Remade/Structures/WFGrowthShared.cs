using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Remade.Structures;

[Serializable, NetSerializable]
public enum WFGrowthVisuals : byte
{
    State,
}

/// <summary>What a growth's sprite shows: seed, resting, or doing its work.</summary>
[Serializable, NetSerializable]
public enum WFGrowthState : byte
{
    Growing,
    Idle,
    Active,
}

/// <summary>Where the Director likes to put a growth.</summary>
public enum WFRemadeSiteKind : byte
{
    /// <summary>The edge of the corruption nearest the crew.</summary>
    Frontier,

    /// <summary>Corridors and doorways a few steps from the crew.</summary>
    Choke,

    /// <summary>Deep in the corruption, away from the crew.</summary>
    Rear,

    /// <summary>Anywhere on corruption close to the crew.</summary>
    Near,
}

/// <summary>One kind of growth: what the Director pays for it and where it goes.</summary>
[Prototype("wfRemadeGrowth")]
public sealed partial class WFRemadeGrowthPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Entity;

    [DataField]
    public float Cost = 20f;

    [DataField]
    public WFMonolithStage MinStage = WFMonolithStage.Harvest;

    [DataField]
    public WFRemadeSiteKind Site = WFRemadeSiteKind.Near;

    /// <summary>Weight in the Director's pick.</summary>
    [DataField]
    public float Weight = 1f;

    /// <summary>The most of it one Monolith keeps at its first stage.</summary>
    [DataField]
    public int MaxAlive = 2;

    /// <summary>Extra allowed for each stage past the first.</summary>
    [DataField]
    public int AlivePerStage = 1;

    /// <summary>Metres kept clear of others of its kind.</summary>
    [DataField]
    public float SameDistance;

    [DataField]
    public bool RequireWall;

    /// <summary>It may go where a crewman can see.</summary>
    [DataField]
    public bool InSight;

    /// <summary>Seconds an Infector works to raise it.</summary>
    [DataField]
    public float WorkSeconds = 4f;

    /// <summary>Seconds it takes to swell up with no Infector's help.</summary>
    [DataField]
    public float GrowSeconds = 75f;
}

/// <summary>Finishes pulling a crewman out of a maw.</summary>
[Serializable, NetSerializable]
public sealed partial class WFMawFreeDoAfterEvent : SimpleDoAfterEvent;
