using Content.Server.Fluids.EntitySystems;
using Content.Shared.Mobs;

namespace Content.Server._WF.WildSlimes;

/// <summary>Spills a mob's <see cref="WFSpillOnDeathComponent"/> solution where it dies.</summary>
public sealed class WFSpillOnDeathSystem : EntitySystem
{
    [Dependency] private PuddleSystem _puddle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSpillOnDeathComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<WFSpillOnDeathComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || ent.Comp.Spilled)
            return;

        ent.Comp.Spilled = true;
        _puddle.TrySpillAt(ent.Owner, ent.Comp.Solution.Clone(), out _);
    }
}
