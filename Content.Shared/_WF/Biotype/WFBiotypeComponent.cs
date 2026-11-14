using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Biotype;

/// <summary>The profile's biotype has been applied to this mob; applying the same one again does nothing.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFBiotypeComponent : Component
{
    /// <summary>The effective biotype.</summary>
    [DataField, AutoNetworkedField]
    public WFBiotype Biotype;

    /// <summary>The overlay that converted the mob, or null when it kept its species' native biotype.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<WFBiotypePrototype>? Applied;
}

/// <summary>Raised directed on the mob and broadcast after an overlay converted it.</summary>
[ByRefEvent]
public readonly record struct WFBiotypeAppliedEvent(EntityUid Mob, WFBiotype Biotype);
