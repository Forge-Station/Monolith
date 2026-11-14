using Content.Shared._WF.PlanetNatives;
using Content.Shared.Mind.Components;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    /// <summary>Whether both are mindless planet natives, which leave each other alone whatever their factions.</summary>
    private bool WfNativeTruce(EntityUid ent, EntityUid target)
    {
        return HasComp<WFPlanetNativeComponent>(ent)
               && HasComp<WFPlanetNativeComponent>(target)
               && !(TryComp<MindContainerComponent>(ent, out var own) && own.HasMind)
               && !(TryComp<MindContainerComponent>(target, out var other) && other.HasMind);
    }
}
