using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WildSlimes;

/// <summary>Heals a mob a little while it stands in a pool or puddle holding one of <see cref="Reagents"/>.</summary>
[RegisterComponent]
public sealed partial class WFReagentBathHealComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<ReagentPrototype>> Reagents = new();

    /// <summary>Applied each <see cref="Interval"/> while bathing; negative heals.</summary>
    [DataField(required: true)]
    public DamageSpecifier Heal = new();

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    [ViewVariables]
    public TimeSpan NextCheck;
}
