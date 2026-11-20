using Content.Shared._WF.Xenonids.Mobs;
using Content.Shared.Ghost;

namespace Content.Server._WF.Xenonids.Hud;

/// <summary>Gives every ghost the xeno HUD, as RMC's ghost system does. RMC's toggle action for it is not ported.</summary>
public sealed class XenoGhostHudSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<GhostComponent, ComponentInit>(OnGhostInit);
    }

    private void OnGhostInit(Entity<GhostComponent> ent, ref ComponentInit args)
    {
        EnsureComp<CMGhostXenoHudComponent>(ent);
    }
}
