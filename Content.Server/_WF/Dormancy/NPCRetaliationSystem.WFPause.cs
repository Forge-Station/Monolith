using Content.Server.NPC.Components;

namespace Content.Server.NPC.Systems;

/// <summary>Keeps an NPC's memory of its attackers through a pause, such as its site sleeping while nobody is near.</summary>
public sealed partial class NPCRetaliationSystem
{
    private void WfOnUnpaused(Entity<NPCRetaliationComponent> ent, ref EntityUnpausedEvent args)
    {
        foreach (var attacker in new List<EntityUid>(ent.Comp.AttackMemories.Keys))
        {
            ent.Comp.AttackMemories[attacker] += args.PausedTime;
        }
    }
}
