using Content.Server._WF.Remade.Corruption;
using Content.Server.Decals;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Counterplay;

/// <summary>Corruption that fire burns away leaves its tile scorched, and corruption can't grow back onto a scorched tile.</summary>
public sealed class WFScorchSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private static readonly string[] Marks = { "burnt1", "burnt2", "burnt3" };
    private static readonly TimeSpan SweepRate = TimeSpan.FromSeconds(10);
    private const string Heat = "Heat";

    /// <summary>How long a tile stays scorched.</summary>
    public TimeSpan ScorchFor = TimeSpan.FromMinutes(10);

    private readonly List<Vector2i> _expired = new();
    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCorruptionComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<WFScorchedGridComponent, WFCorruptionGrowAttemptEvent>(OnGrowAttempt);
    }

    /// <summary>Corruption that took mostly heat damage when it was destroyed was burned away.</summary>
    private void OnDestroyed(Entity<WFCorruptionComponent> ent, ref DestructionEventArgs args)
    {
        if (!TryComp<DamageableComponent>(ent, out var damageable)
            || !damageable.Damage.DamageDict.TryGetValue(Heat, out var heat)
            || heat.Float() * 2f < damageable.TotalDamage.Float())
        {
            return;
        }

        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        Scorch(gridUid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
    }

    private void OnGrowAttempt(Entity<WFScorchedGridComponent> ent, ref WFCorruptionGrowAttemptEvent args)
    {
        if (ent.Comp.Tiles.TryGetValue(args.Tile, out var until) && _timing.CurTime < until)
            args.Cancelled = true;
    }

    /// <summary>Scorches a tile for <see cref="ScorchFor"/> and marks it with a burn.</summary>
    public void Scorch(EntityUid grid, Vector2i tile)
    {
        EnsureComp<WFScorchedGridComponent>(grid).Tiles[tile] = _timing.CurTime + ScorchFor;
        _decals.TryAddDecal(_random.Pick(Marks), new EntityCoordinates(grid, tile), out _, cleanable: true);
    }

    public bool IsScorched(EntityUid grid, Vector2i tile)
    {
        return TryComp<WFScorchedGridComponent>(grid, out var scorched)
               && scorched.Tiles.TryGetValue(tile, out var until)
               && _timing.CurTime < until;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepRate;
        var query = EntityQueryEnumerator<WFScorchedGridComponent>();
        while (query.MoveNext(out var uid, out var scorched))
        {
            _expired.Clear();
            foreach (var (tile, until) in scorched.Tiles)
            {
                if (now >= until)
                    _expired.Add(tile);
            }

            foreach (var tile in _expired)
            {
                scorched.Tiles.Remove(tile);
            }

            if (scorched.Tiles.Count == 0)
                RemCompDeferred<WFScorchedGridComponent>(uid);
        }
    }
}
