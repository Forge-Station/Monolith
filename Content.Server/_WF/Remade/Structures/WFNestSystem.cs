using Content.Server._WF.Remade.Director;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Structures;

/// <summary>A nest makes the Remade its Director picked, one at a time and slowly, and never more than a couple alive.</summary>
public sealed partial class WFNestSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    private static readonly TimeSpan CheckRate = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryRate = TimeSpan.FromSeconds(10);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFNestComponent, WFGrowthComponent>();
        while (query.MoveNext(out var uid, out var nest, out var growth))
        {
            if (now < nest.NextCheck)
                continue;

            nest.NextCheck = now + CheckRate;
            if (growth.Active && growth.Source is { } monolith)
                Tick((uid, nest), monolith, now);
        }
    }

    private void Tick(Entity<WFNestComponent> ent, EntityUid monolith, TimeSpan now)
    {
        var nest = ent.Comp;
        nest.Alive.RemoveWhere(uid => TerminatingOrDeleted(uid) || _mobState.IsDead(uid));

        nest.Produces ??= _director.PickNestMob(monolith);
        if (nest.Produces == null)
            return;

        // The clock only runs while there is room.
        if (nest.Alive.Count >= nest.MaxAlive)
        {
            nest.NextMake = TimeSpan.Zero;
            return;
        }

        if (nest.NextMake == TimeSpan.Zero)
            nest.NextMake = now + nest.Interval;

        if (now < nest.NextMake)
            return;

        if (_director.TryProduce(monolith, nest.Produces.Value, Transform(ent).Coordinates) is { } remade)
        {
            nest.Alive.Add(remade);
            nest.Made++;
            nest.NextMake = now + nest.Interval;
        }
        else
        {
            nest.NextMake = now + RetryRate;
        }
    }
}
