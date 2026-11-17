using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Jittering;
using Content.Shared.Stunnable;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed class GeneticEpilepsySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticEpilepsyComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<GeneticEpilepsyComponent> ent, ref ComponentStartup args)
    {
        ScheduleNext(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticEpilepsyComponent>();
        while (query.MoveNext(out var uid, out var epilepsy))
        {
            if (epilepsy.NextIncident > _timing.CurTime)
                continue;

            _stun.TryParalyze(uid, epilepsy.SeizureDuration, true);
            _jitter.DoJitter(uid, epilepsy.SeizureDuration, true);
            ScheduleNext((uid, epilepsy));
        }
    }

    private void ScheduleNext(Entity<GeneticEpilepsyComponent> ent)
    {
        var delay = _random.NextFloat(ent.Comp.MinInterval, ent.Comp.MaxInterval);
        ent.Comp.NextIncident = _timing.CurTime + TimeSpan.FromSeconds(delay);
    }
}
