using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// Strips the upstream Critical actions (Succumb, Fake Death, Last Words) from a wound host (M1a, plan §5.4).
/// On a wound host Critical also covers a faint and plain unconsciousness, and upstream Succumb neither kills
/// nor tells the truth. Wolfmed's own Succumb and Last Words are granted while Dying only.
/// </summary>
/// <remarks>
/// <see cref="MobStateActionsComponent"/> is not networked and its system grants from the per-entity list on
/// every state change, so the list is edited on both sides at startup. That runs after the mob state's own
/// init grant, while the body is still Alive, so nothing Critical has been granted yet.
/// </remarks>
public sealed class WolfmedCritActionsSystem : EntitySystem
{
    [Dependency] private SharedWolfmedConsciousnessSystem _consciousness = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Pair checked free: nothing else subscribes MobStateActionsComponent + ComponentStartup.
        SubscribeLocalEvent<MobStateActionsComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<MobStateActionsComponent> ent, ref ComponentStartup args)
    {
        if (!_consciousness.OwnsMobState(ent) || !ent.Comp.Actions.ContainsKey(MobState.Critical))
            return;

        // A copy, so no other entity's list can ever be touched through a shared reference.
        var actions = new Dictionary<MobState, List<string>>(ent.Comp.Actions);
        actions.Remove(MobState.Critical);
        ent.Comp.Actions = actions;
    }
}
