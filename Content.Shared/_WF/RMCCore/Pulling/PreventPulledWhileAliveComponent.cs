using Content.Shared.StatusEffect;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RMCCore.Pulling;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCPullingSystem))]
public sealed partial class PreventPulledWhileAliveComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;

    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<StatusEffectPrototype>> ExceptEffects = new();
}
