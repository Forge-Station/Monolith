using Content.Server._WF.Remade.Director;
using Content.Server.Botany.Components;
using Content.Server.Nutrition.Components;
using Content.Shared._WF.Remade.Structures;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Structures;

/// <summary>
/// A harvester warms up for a minute on corruption, then pays its Director biomass every second for the growing plants, food
/// and bodies within its range, and glows while it does. Kill it or burn the corruption under it to stop it.
/// </summary>
public sealed partial class WFHarvesterSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private WFGrowthSystem _growth = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ScanRate = TimeSpan.FromSeconds(10);

    private TimeSpan _nextTick;
    private readonly HashSet<Entity<PlantHolderComponent>> _plants = new();
    private readonly HashSet<Entity<FoodComponent>> _food = new();
    private readonly HashSet<Entity<MobStateComponent>> _bodies = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickRate;
        var query = EntityQueryEnumerator<WFHarvesterComponent, WFGrowthComponent>();
        while (query.MoveNext(out var uid, out var harvester, out var growth))
        {
            Tick((uid, harvester), growth, now);
        }
    }

    private void Tick(Entity<WFHarvesterComponent> ent, WFGrowthComponent growth, TimeSpan now)
    {
        var harvester = ent.Comp;
        if (!growth.Active)
        {
            if (harvester.Running)
                Stop(ent, growth);

            harvester.ActiveAt = null;
            return;
        }

        harvester.ActiveAt ??= now + harvester.WarmUp;
        if (!harvester.Running)
        {
            if (now < harvester.ActiveAt)
                return;

            harvester.Running = true;
            harvester.NextScan = now;
            harvester.LastPay = now;
            _light.SetEnabled(ent, true);
            _growth.SetState((ent, growth), WFGrowthState.Active);
        }

        if (now >= harvester.NextScan)
        {
            harvester.NextScan = now + ScanRate;
            harvester.Rate = Count(ent);
        }

        var seconds = (float) Math.Min((now - harvester.LastPay).TotalSeconds, 5.0);
        harvester.LastPay = now;
        if (TryComp<WFRemadeDirectorComponent>(growth.Source, out var director))
            director.Biomass += harvester.Rate * seconds;
    }

    private void Stop(Entity<WFHarvesterComponent> ent, WFGrowthComponent growth)
    {
        ent.Comp.Running = false;
        ent.Comp.Rate = 0f;
        _light.SetEnabled(ent, false);
        _growth.SetState((ent, growth), WFGrowthState.Idle);
    }

    /// <summary>Biomass a second from what stands within range.</summary>
    private float Count(Entity<WFHarvesterComponent> ent)
    {
        var harvester = ent.Comp;
        var at = Transform(ent).Coordinates;

        _plants.Clear();
        _lookup.GetEntitiesInRange(at, harvester.Range, _plants);
        var plants = 0;
        foreach (var plant in _plants)
        {
            if (plant.Comp.Seed != null && !plant.Comp.Dead)
                plants++;
        }

        _food.Clear();
        _lookup.GetEntitiesInRange(at, harvester.Range, _food);

        _bodies.Clear();
        _lookup.GetEntitiesInRange(at, harvester.Range, _bodies);
        var corpses = 0;
        foreach (var body in _bodies)
        {
            if (_mobState.IsDead(body))
                corpses++;
        }

        return plants * harvester.PlantRate
               + Math.Min(_food.Count, harvester.FoodCap) * harvester.FoodRate
               + Math.Min(corpses, harvester.CorpseCap) * harvester.CorpseRate;
    }
}
