using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.RMCCore.Map;
using Content.Shared._WF.Xenonids.Fire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenonids.Fire;

/// <summary>
/// RMC's caste fire immunities and burning weeds on top of upstream <see cref="FlammableComponent"/>.
/// </summary>
public sealed class XenoFireSystem : EntitySystem
{
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private RMCMapSystem _rmcMap = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.5);
    private static readonly Direction[] Cardinals = [Direction.North, Direction.East, Direction.South, Direction.West];

    private EntityQuery<FlammableComponent> _flammableQuery;
    private EntityQuery<XenoWeedsBurnComponent> _weedsBurnQuery;
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        _flammableQuery = GetEntityQuery<FlammableComponent>();
        _weedsBurnQuery = GetEntityQuery<XenoWeedsBurnComponent>();

        SubscribeLocalEvent<RMCImmuneToIgnitionComponent, TryIgniteEvent>(OnImmuneTryIgnite);
        SubscribeLocalEvent<RMCImmuneToFireTileDamageComponent, GetFireProtectionEvent>(OnImmuneGetFireProtection);
    }

    private void OnImmuneTryIgnite(Entity<RMCImmuneToIgnitionComponent> ent, ref TryIgniteEvent args)
    {
        args.Cancelled = true;
    }

    private void OnImmuneGetFireProtection(Entity<RMCImmuneToFireTileDamageComponent> ent, ref GetFireProtectionEvent args)
    {
        args.Multiplier *= ent.Comp.BurnDamageMultiplier;
    }

    public override void Update(float frameTime)
    {
        var time = _timing.CurTime;
        if (time < _nextCheck)
            return;

        _nextCheck = time + CheckEvery;

        // stacks added without Ignite skip TryIgniteEvent
        var immune = EntityQueryEnumerator<RMCImmuneToIgnitionComponent, FlammableComponent>();
        while (immune.MoveNext(out var uid, out _, out var flammable))
        {
            if (flammable.OnFire)
                _flammable.Extinguish(uid, flammable);
        }

        var weeds = EntityQueryEnumerator<XenoWeedsBurnComponent, FlammableComponent, TransformComponent>();
        while (weeds.MoveNext(out var uid, out var burn, out var flammable, out var xform))
        {
            if (!flammable.OnFire)
            {
                burn.SpreadAt = null;
                burn.Spread = false;
                continue;
            }

            if (burn.Spread)
                continue;

            burn.SpreadAt ??= time + burn.SpreadDelay;
            if (time < burn.SpreadAt || !xform.Anchored)
                continue;

            burn.Spread = true;
            foreach (var dir in Cardinals)
            {
                var anchored = _rmcMap.GetAnchoredEntitiesEnumerator(uid, dir);
                while (anchored.MoveNext(out var neighbour))
                {
                    if (!_weedsBurnQuery.HasComp(neighbour) ||
                        !_flammableQuery.TryComp(neighbour, out var other) ||
                        other.OnFire)
                    {
                        continue;
                    }

                    _flammable.SetFireStacks(neighbour, other.MaximumFireStacks, other);
                    _flammable.Ignite(neighbour, uid, other);
                }
            }
        }
    }
}
