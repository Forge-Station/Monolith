using Content.Server._WF.NpcCrew.Components;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Connects real suit internals before crew work and when their current tile loses safe air.</summary>
public sealed class WFCrewEvaSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedGasTankSystem _tanks = default!;
    [Dependency] private SharedInternalsSystem _internals = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    private readonly Dictionary<EntityUid, TimeSpan> _prepared = new();
    private readonly Dictionary<EntityUid, EntityCoordinates> _safetyDestinations = new();
    private readonly Dictionary<EntityUid, (TimeSpan Until, EntityUid Grid, bool Safe)> _safetyChecked = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextTankSearch = new();
    private readonly List<EntityUid> _stale = new();
    private const float ReservePressure = 300;
    private static readonly TimeSpan SafetyCacheTime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TankSearchBackoff = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TankReachBackoff = TimeSpan.FromSeconds(1);
    private float _timer;
    private TimeSpan _nextPrune;

    /// <summary>The slots of a void kit, in the order they have to go on: the tank hangs off the suit.</summary>
    private static readonly string[] VoidKitSlots = { "outerClothing", "head", "mask", "suitstorage" };

    /// <summary>
    /// Dresses a crewman for a post with no air: a pressure suit, helmet, mask and a tank that is kept full for as
    /// long as he wears it. What his loadout had in those slots is replaced.
    /// </summary>
    public void GiveVoidKit(EntityUid mob)
    {
        var kit = EnsureComp<WFCrewVoidKitComponent>(mob);
        if (!Ready(mob) && _prototypes.TryIndex(kit.Gear, out var gear))
        {
            var coordinates = Transform(mob).Coordinates;
            foreach (var slot in VoidKitSlots)
            {
                if (!gear.Equipment.TryGetValue(slot, out var proto))
                    continue;
                if (_inventory.TryGetSlotEntity(mob, slot, out var old) && _inventory.TryUnequip(mob, slot, silent: true, force: true))
                    QueueDel(old);
                var item = Spawn(proto, coordinates);
                if (!_inventory.TryEquip(mob, item, slot, silent: true, force: true))
                    QueueDel(item);
            }
        }
        TopUp(mob, kit);
        Ready(mob);
    }

    /// <summary>Whether a mob neither needs pressure nor breathes, so bad air cannot hurt him.</summary>
    public bool IsImmune(EntityUid mob) => HasComp<PressureImmunityComponent>(mob) && HasComp<BreathingImmunityComponent>(mob);

    /// <summary>Refills the oxygen a void kit's tank has lost.</summary>
    private void TopUp(EntityUid mob, WFCrewVoidKitComponent kit)
    {
        if (!_inventory.TryGetSlotEntity(mob, "suitstorage", out var tank) || !TryComp<GasTankComponent>(tank, out var gas))
            return;
        var air = gas.Air;
        if (air.Volume <= 0f || air.Pressure >= kit.TankPressure * 0.9f)
            return;
        if (air.TotalMoles <= 0f)
            air.Temperature = Content.Shared.Atmos.Atmospherics.T20C;
        var missing = (kit.TankPressure - air.Pressure) * air.Volume / (Content.Shared.Atmos.Atmospherics.R * air.Temperature);
        air.AdjustMoles(Content.Shared.Atmos.Gas.Oxygen, missing);
    }

    /// <summary>Whether a crewman can work in bad air: immune to it, or on working internals.</summary>
    public bool Prepare(EntityUid mob) => IsImmune(mob) || Ready(mob);

    /// <summary>Requires a pressure suit, helmet, breathing interface and an actual nonempty oxygen tank.</summary>
    private bool Ready(EntityUid mob)
    {
        TryReplaceLowTank(mob);
        if (!_inventory.TryGetSlotEntity(mob, "outerClothing", out var suit)
            || !TryComp<PressureProtectionComponent>(suit, out var suitPressure) || suitPressure.LowPressureMultiplier < 100
            || !_inventory.TryGetSlotEntity(mob, "head", out var helmet)
            || !TryComp<PressureProtectionComponent>(helmet, out var helmetPressure) || helmetPressure.LowPressureMultiplier < 100
            || !_inventory.TryGetSlotEntity(mob, "suitstorage", out var tank)
            || !TryComp<GasTankComponent>(tank, out var gas) || gas.Air.Pressure <= ReservePressure || gas.Air.GetMoles(Content.Shared.Atmos.Gas.Oxygen) <= 0)
            return false;
        if (!gas.IsConnected)
            _tanks.ConnectToInternals((tank.Value, gas), mob);
        if (!_internals.AreInternalsWorking(mob))
            return false;
        _prepared[mob] = _timing.CurTime + TimeSpan.FromSeconds(5);
        _safetyDestinations.Remove(mob);
        return true;
    }

    /// <summary>Low-air crew walk to safe air or a replacement tank through the normal crew work branch.</summary>
    public bool TryGetSafetyDestination(EntityUid mob, out EntityCoordinates coordinates)
    {
        if (!_safetyDestinations.TryGetValue(mob, out coordinates))
            return false;
        if (coordinates.IsValid(EntityManager) && !TerminatingOrDeleted(coordinates.EntityId))
            return true;
        _safetyDestinations.Remove(mob);
        coordinates = default;
        return false;
    }

    /// <summary>Sends a crewman refused work for want of air to the nearest usable spare tank, if there is one.</summary>
    public void SeekSpare(EntityUid mob)
    {
        if (TryComp<WFCrewComponent>(mob, out var crew))
            FindSafety(mob, crew, true);
    }

    /// <summary>A loose tank worth swapping to: not a jetpack, charged, and holding a mix that is breathable at regulator pressure.</summary>
    public bool IsUsableSpare(EntityUid uid, GasTankComponent gas)
    {
        return !HasComp<Content.Shared.Movement.Components.JetpackComponent>(uid)
            && gas.Air.Pressure > ReservePressure * 2
            && WFCrewPlannerSystem.IsBreathable(gas.Air, Content.Shared.Atmos.Atmospherics.OneAtmosphere);
    }

    private void TryReplaceLowTank(EntityUid mob)
    {
        if (!_inventory.TryGetSlotEntity(mob, "suitstorage", out var old)
            || !TryComp<GasTankComponent>(old, out var oldGas) || oldGas.Air.Pressure > ReservePressure)
            return;
        var now = _timing.CurTime;
        if (_nextTankSearch.TryGetValue(mob, out var next) && now < next)
            return;
        if (Transform(mob).GridUid is not { } grid)
            return;
        EntityUid? spare = null;
        var anySpare = false;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var candidate))
        {
            if (candidate == old || !TryComp<GasTankComponent>(candidate, out var gas) || !IsUsableSpare(candidate, gas))
                continue;
            anySpare = true;
            if (!_interaction.InRangeUnobstructed(mob, candidate))
                continue;
            spare = candidate;
            break;
        }
        if (spare is not { } replacement)
        {
            _nextTankSearch[mob] = now + (anySpare ? TankReachBackoff : TankSearchBackoff);
            return;
        }
        _nextTankSearch.Remove(mob);
        if (oldGas.IsConnected)
            _tanks.DisconnectFromInternals((old.Value, oldGas), mob);
        if (!_inventory.TryUnequip(mob, "suitstorage", silent: true))
            return;
        if (!_inventory.TryEquip(mob, replacement, "suitstorage", silent: true))
            _inventory.TryEquip(mob, old.Value, "suitstorage", silent: true);
    }

    private void FindSafety(EntityUid mob, WFCrewComponent crew, bool safe)
    {
        var grid = crew.Post?.EntityId ?? Transform(mob).GridUid;
        if (grid is not { } home || TerminatingOrDeleted(home) || !TryComp<MapGridComponent>(home, out var map))
        {
            _safetyDestinations.Remove(mob);
            _safetyChecked.Remove(mob);
            return;
        }
        var now = _timing.CurTime;
        if (_safetyChecked.TryGetValue(mob, out var last) && last.Grid == home && last.Safe == safe && now < last.Until)
            return;
        _safetyChecked[mob] = (now + SafetyCacheTime, home, safe);
        EntityCoordinates? best = null;
        var distance = float.MaxValue;
        var here = _transform.GetWorldPosition(mob);
        if (!safe)
        {
            var tiles = _maps.GetAllTilesEnumerator(home, map);
            while (tiles.MoveNext(out var tile))
            {
                if (!_planner.IsSafePost(home, tile.Value.GridIndices, map))
                    continue;
                var point = _maps.GridTileToLocal(home, map, tile.Value.GridIndices);
                var squared = (_transform.ToWorldPosition(point) - here).LengthSquared();
                if (squared < distance) { best = point; distance = squared; }
            }
        }
        else
        {
            var children = Transform(home).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!TryComp<GasTankComponent>(child, out var gas) || !IsUsableSpare(child, gas))
                    continue;
                var xform = Transform(child);
                if (!_planner.IsSafePost(home, _maps.TileIndicesFor(home, map, xform.Coordinates), map))
                    continue;
                var squared = (_transform.GetWorldPosition(xform) - here).LengthSquared();
                if (squared < distance) { best = xform.Coordinates; distance = squared; }
            }
        }
        if (best is { } destination)
            _safetyDestinations[mob] = destination;
        else
            _safetyDestinations.Remove(mob);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 0.5f)
            return;
        _timer = 0;
        if (_timing.CurTime >= _nextPrune)
        {
            _nextPrune = _timing.CurTime + TimeSpan.FromSeconds(1);
            Prune(_timing.CurTime);
        }
        var work = EntityManager.System<WFCrewWorkSystem>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                continue;
            if (TryComp<WFCrewVoidKitComponent>(uid, out var kit))
                TopUp(uid, kit);
            var safe = xform.GridUid is { } grid && TryComp<MapGridComponent>(grid, out var map)
                && _planner.IsSafePost(grid, _maps.TileIndicesFor(grid, map, xform.Coordinates), map);
            // Crew who suit up when the air goes bad dress before anything else looks at their tank.
            if (!safe && HasComp<WFCrewSuitUpComponent>(uid) && !HasComp<WFCrewVoidKitComponent>(uid))
                GiveVoidKit(uid);
            // Immune crew have no air to lose: no suit tank to fret over, and no work dropped for want of it.
            var immune = IsImmune(uid);
            var low = !immune && _inventory.TryGetSlotEntity(uid, "suitstorage", out var worn)
                && TryComp<GasTankComponent>(worn, out var wornGas) && wornGas.Air.Pressure <= ReservePressure;
            var seeking = _safetyDestinations.ContainsKey(uid);
            var attempted = false;
            // A pilot or gunner in breathable air keeps the post; only crew in bad air or about to work need a good tank.
            if (low && (!safe || seeking || work.HasJob(uid)))
            {
                attempted = true;
                if (!Ready(uid))
                {
                    // Only a worker in bad air drops his job; in breathable air the work system ends one that needs the suit.
                    if (!safe)
                    {
                        work.CancelWorker(uid);
                        EntityManager.System<WFCrewSpeechSystem>().Say(uid, "air");
                    }
                    FindSafety(uid, crew, safe);
                    if (!safe && _inventory.TryGetSlotEntity(uid, "suitstorage", out var wornTank)
                        && TryComp<GasTankComponent>(wornTank, out var connectable) && !connectable.IsConnected)
                        _tanks.ConnectToInternals((wornTank.Value, connectable), uid);
                }
            }
            else if (!low && seeking)
                _safetyDestinations.Remove(uid);
            if (!safe)
            {
                if (!attempted)
                    Ready(uid);
            }
            else if ((!_prepared.TryGetValue(uid, out var until) || _timing.CurTime >= until)
                && _inventory.TryGetSlotEntity(uid, "suitstorage", out var tank)
                && TryComp<GasTankComponent>(tank, out var gas) && gas.IsConnected)
            {
                _tanks.DisconnectFromInternals((tank.Value, gas), uid);
                _prepared.Remove(uid);
            }
        }
    }

    /// <summary>Drops bookkeeping for deleted crew and elapsed timers.</summary>
    private void Prune(TimeSpan now)
    {
        _stale.Clear();
        foreach (var (uid, until) in _prepared)
        {
            if (now >= until || TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }
        foreach (var uid in _stale)
            _prepared.Remove(uid);

        _stale.Clear();
        foreach (var (uid, coordinates) in _safetyDestinations)
        {
            if (TerminatingOrDeleted(uid) || !coordinates.IsValid(EntityManager) || TerminatingOrDeleted(coordinates.EntityId))
                _stale.Add(uid);
        }
        foreach (var uid in _stale)
            _safetyDestinations.Remove(uid);

        _stale.Clear();
        foreach (var (uid, entry) in _safetyChecked)
        {
            if (now >= entry.Until || TerminatingOrDeleted(uid) || TerminatingOrDeleted(entry.Grid))
                _stale.Add(uid);
        }
        foreach (var uid in _stale)
            _safetyChecked.Remove(uid);

        _stale.Clear();
        foreach (var (uid, next) in _nextTankSearch)
        {
            if (now >= next || TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }
        foreach (var uid in _stale)
            _nextTankSearch.Remove(uid);
    }
}
