using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>
/// Drug levels on a body, one per profile, with the time spent high and any running crashes. Added on the first dose
/// and removed once everything has worn off.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFIntoxicationComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>Level of each profile, in effective units: the drug in the blood plus <see cref="Fed"/>.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> Levels = new();

    /// <summary>Level fed directly, by a metabolised dose or a neuroware program, which decays on its own.</summary>
    [DataField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> Fed = new();

    /// <summary>The rush of each profile: extra level a fresh dose in the blood gives, which fades fast.</summary>
    [DataField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> Rush = new();

    /// <summary>Each profile's level from the blood on the last tick, to tell a fresh dose from what was there.</summary>
    [DataField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> BloodSeen = new();

    /// <summary>Seconds each profile has spent at or above its first tier this high.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> TimeAbove = new();

    /// <summary>Seconds left on each running crash.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, float> Crashes = new();

    /// <summary>Damage a <see cref="WFDrugTrait.Damper"/> tier has held off, repaid as Poison during the crash.</summary>
    [DataField]
    public float Debt;

    /// <summary>The phase each profile was last announced in.</summary>
    [DataField]
    public Dictionary<ProtoId<WFDrugProfilePrototype>, WFDrugPhase> Phases = new();

    /// <summary>Tier traits whose components this component added, so it removes only those.</summary>
    [DataField]
    public HashSet<WFDrugTrait> Granted = new();

    /// <summary>When the current opioid nod began; zero while awake.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan NodStart;

    /// <summary>When the current nod ends; zero while awake.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan NodEnd;

    /// <summary>When the next nod comes; zero while none is due.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextNod;
}
