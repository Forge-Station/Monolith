using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server.Cargo.Systems;
using Content.Server.Destructible;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.ShipRepair;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Drydock berth console: sells repairs and refits for the ship held in its berth area.
/// </summary>
public sealed partial class WFDrydockSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private WFShipRefitSystem _refits = default!;
    [Dependency] private WFHullSectionSystem _hull = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private DestructibleSystem _destructible = default!;

    /// <summary>Upper bound on ship tiles kept for ambient sparks.</summary>
    private const int MaxSparkTiles = 512;

    private List<Entity<MapGridComponent>> _grids = new();

    private TimeSpan _nextConsoleRefresh;

    /// <summary>Cached estimated price and construction material value per entity prototype.</summary>
    private readonly Dictionary<string, (double Estimate, double Materials)> _estimates = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDrydockConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WFDrydockConsoleComponent, WFDrydockRepairMessage>(OnRepair);
        SubscribeLocalEvent<WFDrydockConsoleComponent, WFDrydockUpgradeMessage>(OnUpgrade);
        SubscribeLocalEvent<WFDrydockCraneComponent, MapInitEvent>(OnCraneMapInit);
        SubscribeLocalEvent<WFDrydockCraneComponent, ComponentShutdown>(OnCraneShutdown);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ =>
        {
            _estimates.Clear();
            _constructionMaterials.Clear();
        });

        InitializeBerthLights();
        InitializeBooking();
        InitializeBreak();
        InitializeLinking();
        InitializeUpgrades();
    }

    /// <summary>
    /// The ship grid with the most tiles in the console's berth area, or null. Any tile counts, so a hull bigger than the
    /// berth, or with its centre out of the cranes' reach, is still found.
    /// </summary>
    public EntityUid? GetBerthedShip(EntityUid console, WFDrydockConsoleComponent comp)
    {
        if (!TryGetBerth(console, comp, out var berth))
            return null;

        var stationXform = Transform(berth.Grid);
        var worldAabb = _transform.GetWorldMatrix(stationXform).TransformBox(berth.Area);
        var (stationPos, stationRot) = _transform.GetWorldPositionRotation(stationXform);
        var worldArea = new Box2Rotated(berth.Area.Translated(stationPos), stationRot, stationPos);

        _grids.Clear();
        _mapManager.FindGridsIntersecting(stationXform.MapID, worldAabb, ref _grids);

        EntityUid? best = null;
        var bestTiles = 0;
        foreach (var grid in _grids)
        {
            if (grid.Owner == berth.Grid || !HasComp<ShuttleComponent>(grid) || HasComp<MapComponent>(grid))
                continue;

            var tiles = _map.GetTilesIntersecting(grid, grid.Comp, worldArea).Count();
            if (tiles <= bestTiles)
                continue;

            best = grid;
            bestTiles = tiles;
        }

        return best;
    }

    /// <summary>Whether the centre of a ship's bounds lies in the console's berth area.</summary>
    private bool CentredInBerth(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship)
    {
        if (!TryGetBerth(console, comp, out var berth) || !TryComp<MapGridComponent>(ship, out var grid))
            return false;

        var world = Vector2.Transform(grid.LocalAABB.Center, _transform.GetWorldMatrix(ship));
        return berth.Area.Contains(Vector2.Transform(world, _transform.GetInvWorldMatrix(berth.Grid)));
    }

    /// <summary>Resolves a console's berth from its linked cranes: the area covering their reach.</summary>
    public bool TryGetBerth(EntityUid console, WFDrydockConsoleComponent comp, out WFDrydockBerth berth)
    {
        berth = default;
        if (Transform(console).GridUid is not { } grid)
            return false;

        var cranes = LinkedCranes(console);
        if (cranes.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var arms = Vector2.Zero;
        foreach (var uid in cranes)
        {
            var crane = Comp<WFDrydockCraneComponent>(uid);
            var xform = Transform(uid);
            var rotation = Matrix3x2.CreateRotation((float) xform.LocalRotation.Theta);
            var toGrid = rotation * Matrix3x2.CreateTranslation(xform.LocalPosition);

            var a = crane.ArmToLocal(0f, crane.BerthGap);
            var b = crane.ArmToLocal(crane.MaxExtension, crane.MaxArm);
            var corners = new[] { new Vector2(a.X, a.Y), new Vector2(b.X, a.Y), new Vector2(a.X, b.Y), new Vector2(b.X, b.Y) };
            foreach (var corner in corners)
            {
                var point = Vector2.Transform(corner, toGrid);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            arms += Vector2.Transform(new Vector2(0, -1), rotation);
        }

        // Ships come in along the arms' average direction, snapped to the nearest grid axis.
        var approach = arms.LengthSquared() < 1e-6f
            ? new Vector2(0, -1)
            : Math.Abs(arms.X) >= Math.Abs(arms.Y)
                ? new Vector2(MathF.Sign(arms.X), 0)
                : new Vector2(0, MathF.Sign(arms.Y));

        // The area covers every linked crane; only unblocked cranes take jobs.
        cranes.RemoveAll(uid => Comp<WFDrydockCraneComponent>(uid).Blocked);
        var label = !string.IsNullOrEmpty(comp.Berth) ? comp.Berth : MetaData(console).EntityName;
        berth = new WFDrydockBerth(grid, label, new Box2(min, max), approach, cranes);
        return true;
    }

    /// <summary>The cranes linked to a console that stand on its grid, in entity order.</summary>
    public List<EntityUid> LinkedCranes(EntityUid console)
    {
        var result = new List<EntityUid>();
        if (Transform(console).GridUid is not { } grid)
            return result;

        var query = EntityQueryEnumerator<WFDrydockCraneComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crane, out var xform))
        {
            if (crane.LinkedConsole == console && xform.GridUid == grid)
                result.Add(uid);
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    /// <summary>The name a console's berth goes by in messages.</summary>
    private string BerthLabel(EntityUid console, WFDrydockConsoleComponent comp)
    {
        return TryGetBerth(console, comp, out var berth) ? berth.Label : comp.Berth;
    }

    /// <summary>Lists the work needed to restore the ship: hull tiles, missing entities, heals, then owned refits.</summary>
    public List<WFDrydockWorkItem> BuildRepairItems(EntityUid ship, WFDrydockConsoleComponent comp)
    {
        var items = new List<WFDrydockWorkItem>();
        if (!TryComp<ShipRepairDataComponent>(ship, out var data) || !TryComp<MapGridComponent>(ship, out var grid))
            return items;

        AddMissingTiles(ship, grid, data, comp, items);
        AddMissingEntities(ship, data, comp, items);
        MarkBroken(ship, items);
        AddHeals(ship, grid, comp, items);
        AddRefitRepairs(ship, comp, items);
        CapRepair(ship, comp, items);
        return items;
    }

    /// <summary>
    /// The ship's value for fees and caps: its shipyard price when it was bought at one, else the price of rebuilding
    /// it whole from its repair snapshot. Vessel is false for the rebuild price; (0, false) without a snapshot.
    /// </summary>
    public (int Value, bool Vessel) ShipValue(EntityUid ship, WFDrydockConsoleComponent comp)
    {
        if (TryComp<VesselComponent>(ship, out var vessel) &&
            _prototype.TryIndex<VesselPrototype>(vessel.VesselId, out var proto) && proto.Price > 0)
        {
            return (proto.Price, true);
        }

        if (!TryComp<ShipRepairDataComponent>(ship, out var data))
            return (0, false);

        var value = 0d;
        foreach (var chunk in data.Chunks.Values)
        {
            foreach (var tile in chunk.Tiles)
            {
                if (tile != Tile.Empty.TypeId)
                    value += comp.TilePrice;
            }

            foreach (var spec in chunk.Entities.Values)
            {
                value += RebuildPrice(comp, _prototype.Index<EntityPrototype>(data.EntityPalette[spec.ProtoIndex].Id));
            }
        }

        return ((int) Math.Min(value, int.MaxValue), false);
    }

    /// <summary>The repair fee: a share of the ship's value, at least the console's minimum.</summary>
    public int RepairFee(EntityUid ship, WFDrydockConsoleComponent comp)
    {
        var (value, _) = ShipValue(ship, comp);
        return Math.Max(comp.MinFee, (int) Math.Round(value * comp.FeeFraction / 10) * 10);
    }

    /// <summary>Scales items down so the repair costs no more than its cap. Refit shares are charged in full, outside it.</summary>
    private void CapRepair(EntityUid ship, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        var (value, vessel) = ShipValue(ship, comp);
        var cap = (long) (value * (vessel ? comp.CapFraction : comp.SnapshotCapFraction));
        var total = items.Sum(i => (long) (i.Cost - i.RefitCost));
        if (cap <= 0 || total <= cap)
            return;

        var scale = (double) cap / total;
        foreach (var item in items)
        {
            var capped = item.Cost - item.RefitCost;
            if (capped <= 0)
                continue;

            item.Cost = Math.Max(1, (int) (capped * scale)) + item.RefitCost;
            item.Share *= (float) scale;
        }
    }

    /// <summary>Adds a tile item for each stored hull tile the ship no longer has.</summary>
    private void AddMissingTiles(EntityUid ship, MapGridComponent grid, ShipRepairDataComponent data, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        var size = data.ChunkSize;
        foreach (var (chunkPos, chunk) in data.Chunks)
        {
            for (var i = 0; i < chunk.Tiles.Length; i++)
            {
                var stored = chunk.Tiles[i];
                if (stored == Tile.Empty.TypeId)
                    continue;

                var index = new Vector2i(chunkPos.X * size + i % size, chunkPos.Y * size + i / size);
                if (_map.GetTileRef(ship, grid, index).Tile.TypeId == stored)
                    continue;

                items.Add(new WFDrydockWorkItem
                {
                    Kind = WFDrydockWorkKind.Tile,
                    TileIndex = index,
                    TileType = stored,
                    Cost = comp.TilePrice,
                    LocalPos = new Vector2(index.X + 0.5f, index.Y + 0.5f),
                });
            }
        }
    }

    /// <summary>Marks the tiles and rebuilds that restore what a hull break stripped.</summary>
    private void MarkBroken(EntityUid ship, List<WFDrydockWorkItem> items)
    {
        if (!TryComp<WFHulkComponent>(ship, out var hulk) || hulk.Broken.Count == 0)
            return;

        foreach (var item in items)
        {
            item.Broken = hulk.Broken.Contains(item.Kind == WFDrydockWorkKind.Tile ? item.TileIndex : item.LocalPos.Floored());
        }
    }

    /// <summary>Adds a rebuild item for each snapshot entity the ship is missing.</summary>
    private void AddMissingEntities(EntityUid ship, ShipRepairDataComponent data, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        foreach (var (chunkPos, chunk) in data.Chunks)
        {
            foreach (var (specId, spec) in chunk.Entities)
            {
                if (!IsMissing(ship, spec))
                    continue;

                var proto = _prototype.Index<EntityPrototype>(data.EntityPalette[spec.ProtoIndex].Id);
                items.Add(new WFDrydockWorkItem
                {
                    Kind = WFDrydockWorkKind.Entity,
                    ChunkPos = chunkPos,
                    SpecId = specId,
                    Cost = RebuildPrice(comp, proto),
                    Prototype = proto.ID,
                    Mounted = proto.TryGetComponent<HardpointAnchorableOnlyComponent>(out _, Factory),
                    LocalPos = spec.LocalPosition,
                });
            }
        }
    }

    /// <summary>Adds a heal item for each anchored part of the ship that has damage.</summary>
    private void AddHeals(EntityUid ship, MapGridComponent grid, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        var damaged = new HashSet<Entity<DamageableComponent>>();
        _lookup.GetLocalEntitiesIntersecting(ship, grid.LocalAABB, damaged);
        foreach (var ent in damaged)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            var xform = Transform(ent);
            if (xform.ParentUid != ship || !xform.Anchored || HasComp<MobStateComponent>(ent))
                continue;

            var total = ent.Comp.TotalDamage;
            if (total <= FixedPoint2.Zero)
                continue;

            // Patching a part costs its rebuild price times the share of its health lost, never more than rebuilding it.
            var proto = MetaData(ent).EntityPrototype;
            int? rebuild = proto != null ? RebuildPrice(comp, proto) : null;
            int cost;
            if (rebuild is { } full && _destructible.TryGetDestroyedAt(ent.Owner, out var destroyedAt) && destroyedAt > FixedPoint2.Zero)
                cost = (int) Math.Ceiling(full * Math.Min(1f, total.Float() / destroyedAt.Value.Float()));
            else
                cost = Math.Min((int) Math.Ceiling(total.Float() * comp.DamagePrice), rebuild ?? int.MaxValue);
            cost = Math.Max(1, cost);

            items.Add(new WFDrydockWorkItem
            {
                Kind = WFDrydockWorkKind.Heal,
                Target = ent,
                Cost = cost,
                Prototype = proto?.ID,
                Share = rebuild is > 0 ? Math.Min(1f, cost / (float) rebuild.Value) : 1f,
                LocalPos = xform.LocalPosition,
            });
        }
    }

    /// <summary>
    /// Price of rebuilding one entity, plus labor: its estimated price times the factor, or the value of its construction
    /// materials times theirs, whichever is more.
    /// </summary>
    public int RebuildPrice(WFDrydockConsoleComponent comp, EntityPrototype proto)
    {
        if (!_estimates.TryGetValue(proto.ID, out var prices))
        {
            prices = (_pricing.GetEstimatedPrice(proto), MaterialValue(ConstructionMaterials(proto)));
            _estimates[proto.ID] = prices;
        }

        var price = Math.Max(prices.Estimate * comp.EntityPriceFactor, prices.Materials * comp.MaterialPriceFactor);
        return (int) Math.Min(price + comp.EntityLabor, int.MaxValue);
    }

    /// <summary>Applies one work item to the ship. Re-checks the item first, so repeats are harmless.</summary>
    public void ApplyItem(EntityUid ship, WFDrydockWorkItem item)
    {
        switch (item.Kind)
        {
            case WFDrydockWorkKind.Tile:
                ApplyTile(ship, item);
                break;
            case WFDrydockWorkKind.Entity:
                ApplyEntity(ship, item);
                break;
            case WFDrydockWorkKind.Heal:
                ApplyHeal(item);
                break;
            case WFDrydockWorkKind.Upgrade:
                ApplyUpgrade(item);
                break;
        }
    }

    private void ApplyTile(EntityUid ship, WFDrydockWorkItem item)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid) ||
            _map.GetTileRef(ship, grid, item.TileIndex).Tile.TypeId == item.TileType)
            return;

        _map.SetTile(ship, grid, item.TileIndex, new Tile(item.TileType));
    }

    private void ApplyEntity(EntityUid ship, WFDrydockWorkItem item)
    {
        if (!TryComp<ShipRepairDataComponent>(ship, out var data) ||
            !data.Chunks.TryGetValue(item.ChunkPos, out var chunk) ||
            !chunk.Entities.TryGetValue(item.SpecId, out var spec) ||
            !IsMissing(ship, spec))
            return;

        // Only a debris original can still be here; remove it before rebuilding.
        if (TryGetOriginal(spec, out var original))
            QueueDel(original);

        var entity = Spawn(data.EntityPalette[spec.ProtoIndex].Id, new EntityCoordinates(ship, spec.LocalPosition));
        _transform.SetLocalRotation(entity, spec.Rotation);
        // Rebuilds spawn stock; reapply the ship's owned refits, which the quote charged for.
        foreach (var (refit, owned) in _refits.Owned(ship))
        {
            _refits.Apply(refit, entity, owned.Tier);
        }
        spec.OriginalEntity = GetNetEntity(entity);
        Dirty(ship, data);
    }

    private void ApplyHeal(WFDrydockWorkItem item)
    {
        if (!TryComp<DamageableComponent>(item.Target, out var damageable) ||
            TerminatingOrDeleted(item.Target) ||
            damageable.TotalDamage <= FixedPoint2.Zero)
            return;

        _damageable.SetAllDamage(item.Target, damageable, FixedPoint2.Zero);
    }

    private void ApplyUpgrade(WFDrydockWorkItem item)
    {
        if (item.Upgrade == null ||
            !_prototype.TryIndex<WFShipRefitPrototype>(item.Upgrade, out var proto) ||
            TerminatingOrDeleted(item.Target))
            return;

        // A refit leaves units already at the tier alone.
        _refits.Apply(proto, item.Target, item.Tier);
    }

    /// <summary>
    /// Pushes the console state to viewers, skipping unchanged states unless forced (a viewer just opened the window).
    /// </summary>
    public void UpdateUi(EntityUid console, WFDrydockConsoleComponent? comp = null, bool force = false)
    {
        if (!Resolve(console, ref comp))
            return;

        var job = CompOrNull<WFDrydockJobComponent>(console);

        // During a job, report its ship even if it drifted, and count the queue instead of scanning the grid.
        var ship = job?.Ship ?? GetBerthedShip(console, comp);
        if (ship is { } shown && TerminatingOrDeleted(shown))
            ship = null;

        var state = BuildState(console, ship, comp, job);
        state.BerthLightsOn = comp.BerthLightsUntil != null;

        var key = StateKey(state);
        if (!force && key == comp.LastPushed)
            return;

        comp.LastPushed = key;
        _ui.SetUiState(console, WFDrydockConsoleUiKey.Key, state);
    }

    /// <summary>
    /// Re-sends open consoles' state once a second, since ships, crane power and material stock change off the console.
    /// </summary>
    private void RefreshOpenConsoles(TimeSpan curTime)
    {
        if (curTime < _nextConsoleRefresh)
            return;

        _nextConsoleRefresh = curTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<WFDrydockConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, WFDrydockConsoleUiKey.Key))
                UpdateUi(uid, comp);
        }
    }

    /// <summary>Flattens a console state into a string, so unchanged states compare equal.</summary>
    private static string StateKey(WFDrydockConsoleState s)
    {
        var parts = new List<string>
        {
            s.ShipName ?? "-",
            $"{s.MissingTiles}/{s.MissingParts}/{s.DamagedParts}/{s.RepairPrice}",
            $"{s.Working}/{s.PowerRatio}/{s.CraneCount}/{s.BlockedCranes}/{s.Deploying}/{s.Progress}/{s.RemainingSeconds}",
            $"{s.Booking}/{s.BookedBerth}/{s.BookingSeconds}/{s.Notice}/{s.PaysWithMaterials}/{s.BerthLightsOn}",
            $"{s.CanBreak}/{s.Breakable}/{s.Breaking}",
            DictKey(s.BreakYield),
            $"{s.Station}/{s.BerthConsole}/{s.BerthLabel}/{s.BerthArea}/{s.Approach}/{s.Ship}/{s.Marks.Count}",
            DictKey(s.RepairMaterials),
            DictKey(s.RepairParts),
            DictKey(s.StoredMaterials),
            string.Join(",", s.StoredParts.OrderBy(p => p.Name).Select(p => $"{p.Prototype}:{p.Name}={p.Count}")),
            string.Join(",", s.StoredChemicals.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")),
        };
        foreach (var u in s.Upgrades)
        {
            parts.Add($"{u.Id}:{u.Name}:{u.Units}:{u.Tier}:{u.MaxTier}:{u.NextTier}:{u.NextPrice}:{DictKey(u.NextMaterials)}:{DictKey(u.NextParts)}");
        }

        return string.Join("|", parts);
    }

    private static string DictKey(Dictionary<string, int> bill)
    {
        return string.Join(",", bill.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>
    /// Builds the console state for a ship in or booked to a berth: its cranes, quote, job progress and refits.
    /// </summary>
    private WFDrydockConsoleState BuildState(EntityUid console, EntityUid? ship, WFDrydockConsoleComponent comp, WFDrydockJobComponent? job)
    {
        var linked = LinkedCranes(console);
        var working = WorkingCranes(console, comp);
        var state = new WFDrydockConsoleState
        {
            PowerRatio = working.Count == 0 ? 1f : working.Min(CranePower),
            CraneCount = linked.Count,
            BlockedCranes = linked.Count(uid => Comp<WFDrydockCraneComponent>(uid).Blocked),
        };

        if (TryGetBerth(console, comp, out var berth))
        {
            state.Station = GetNetEntity(berth.Grid);
            state.BerthConsole = GetNetEntity(console);
            state.BerthLabel = berth.Label;
            state.BerthArea = berth.Area;
            state.Approach = berth.Approach;
        }

        var pay = CompOrNull<WFDrydockMaterialPaymentComponent>(console);
        if (pay != null)
        {
            state.PaysWithMaterials = true;
            // Same stock the job's charge draws from: the cranes' storage and their linked silos.
            state.StoredMaterials = Availability(working);
            state.StoredParts = SiloContents(working);
            state.StoredChemicals = StoredChemicals(working);
        }

        if (ship is { } berthed)
        {
            state.ShipName = MetaData(berthed).EntityName;
            state.Ship = GetNetEntity(berthed);

            var items = job != null ? UnfinishedItems(job) : BuildRepairItems(berthed, comp);
            CountWork(state, items);
            AddMarks(state, items);

            if (job == null && items.Count > 0)
            {
                if (pay != null)
                {
                    AssignMaterials(berthed, comp, pay, items, working);
                    state.RepairMaterials = SumBills(items);
                    state.RepairParts = SumParts(items);
                }
                else
                {
                    state.RepairPrice = WorkPrice(berthed, comp, null, items);
                }
            }
        }

        if (CanBreak(console, out var breaker))
        {
            state.CanBreak = true;
            if (job == null && ship is { } target && BreakRefusal(console, comp, target) == null)
            {
                state.Breakable = true;
                state.BreakYield = BreakPreview(target, breaker);
            }
        }

        if (job != null)
        {
            state.Working = true;
            state.Breaking = job.Break;
            state.Deploying = _timing.CurTime < job.WorkStarts;
            state.Progress = job.Total == 0 ? 0f : (float) job.Done / job.Total;
            state.RemainingSeconds = (float) Math.Max(0d, (job.EstimatedEnd - _timing.CurTime).TotalSeconds);
        }

        foreach (var id in comp.Refits)
        {
            if (_prototype.TryIndex(id, out var proto))
                state.Upgrades.Add(BuildRefitEntry(proto, ship, comp, pay, working, scan: job == null));
        }

        return state;
    }

    /// <summary>Tallies work items into the state's missing tile, missing part and damaged part counts.</summary>
    private static void CountWork(WFDrydockConsoleState state, List<WFDrydockWorkItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case WFDrydockWorkKind.Tile:
                case WFDrydockWorkKind.Break:
                    state.MissingTiles++;
                    break;
                case WFDrydockWorkKind.Entity:
                    state.MissingParts++;
                    break;
                case WFDrydockWorkKind.Heal:
                    state.DamagedParts++;
                    break;
            }
        }
    }

    /// <summary>Lists the work items' positions for the berth plot, up to its limit.</summary>
    private static void AddMarks(WFDrydockConsoleState state, List<WFDrydockWorkItem> items)
    {
        foreach (var item in items)
        {
            if (state.Marks.Count >= WFDrydockConsoleState.MaxMarks)
                break;

            var kind = item.Kind switch
            {
                WFDrydockWorkKind.Tile or WFDrydockWorkKind.Break => WFDrydockMarkKind.Tile,
                WFDrydockWorkKind.Entity => WFDrydockMarkKind.Part,
                WFDrydockWorkKind.Heal => WFDrydockMarkKind.Damage,
                _ => WFDrydockMarkKind.Refit,
            };
            state.Marks.Add(new WFDrydockPlotMark(item.LocalPos, kind));
        }
    }

    private void OnUiOpened(EntityUid uid, WFDrydockConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp, force: true);
    }

    private void OnRepair(EntityUid uid, WFDrydockConsoleComponent comp, WFDrydockRepairMessage args)
    {
        if (TryGetWorkableShip(uid, comp, args.Actor, out var berthed))
            TryStartRepair(uid, comp, berthed, args.Actor);
    }

    private void OnUpgrade(EntityUid uid, WFDrydockConsoleComponent comp, WFDrydockUpgradeMessage args)
    {
        // BuildRefitWork refuses a refit this console doesn't sell.
        if (TryGetWorkableShip(uid, comp, args.Actor, out var berthed))
            TryStartWork(uid, comp, berthed, args.Actor, args.Upgrade);
    }

    /// <summary>Starts a repair on the ship in this berth, paid by the actor.</summary>
    public bool TryStartRepair(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor)
    {
        return TryStartWork(console, comp, ship, actor, null);
    }

    /// <summary>Validates and charges a repair (null upgrade) or refit, then starts its job on the ship.</summary>
    private bool TryStartWork(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor, string? upgrade, bool booked = false)
    {
        if (BuildWork(ship, comp, upgrade, out var items) is { } denial)
            return Deny(console, comp, actor, denial);

        if (!CranesReady(console, comp, actor, out var cranes))
            return false;

        // Booked jobs are always paid in credits; walk-in material consoles pay from their cranes.
        var pay = booked ? null : CompOrNull<WFDrydockMaterialPaymentComponent>(console);
        if (pay != null)
        {
            AssignMaterials(ship, comp, pay, items, cranes);
            if (!TryPayMaterials(console, comp, actor, items))
                return false;

            StartJob(console, comp, ship, null, items, 0, paidInMaterials: true);
            _popup.PopupCursor(Loc.GetString("drydock-console-started-materials", ("count", items.Count)), actor);
            return true;
        }

        var price = WorkPrice(ship, comp, upgrade, items);
        if (!TryCharge(console, comp, actor, price))
            return false;

        StartJob(console, comp, ship, actor, items, price);
        return true;
    }

    /// <summary>Checks the console's working cranes can start a job: unblocked, at half power and free. Denies if not.</summary>
    private bool CranesReady(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, out List<EntityUid> cranes)
    {
        RefreshCranes(console);
        cranes = WorkingCranes(console, comp);
        if (cranes.Count == 0 && LinkedCranes(console).Count > 0)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-cranes-blocked"));

        // Every working crane needs half its power to start.
        if (cranes.Any(c => CranePower(c) < 0.5f))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-power"));

        if (cranes.Any(c => CraneBusyElsewhere(console, c)))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-crane-busy"));

        return true;
    }

    /// <summary>
    /// Finds the berthed ship and checks the drydock can start on it: idle, unreserved for others, and held still.
    /// Clears the console's booking on success.
    /// </summary>
    private bool TryGetWorkableShip(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, out EntityUid ship)
    {
        ship = EntityUid.Invalid;
        if (GetBerthedShip(console, comp) is not { } berthed)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-ship"));

        if (HasComp<WFDrydockJobComponent>(console))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-busy"));

        if (TryComp<WFDrydockBookingComponent>(console, out var booking) && booking.Ship != berthed)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-reserved", ("berth", BerthLabel(console, comp)), ("ship", Name(booking.Ship))));

        if (!IsHeldStill(console, comp, berthed))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-ship-moving"));

        if (IsShipyardGrid(console, comp, berthed))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-own-grid"));

        if (booking != null)
            RemoveBooking(console, booking);

        ship = berthed;
        return true;
    }

    /// <summary>Whether a ship moves with the drydock's station no faster than the console's start speed.</summary>
    private bool IsHeldStill(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship)
    {
        var stationVelocity = Transform(console).GridUid is { } station ? _physics.GetMapLinearVelocity(station) : Vector2.Zero;
        return (_physics.GetMapLinearVelocity(ship) - stationVelocity).Length() <= comp.MaxStartSpeed;
    }

    /// <summary>Withdraws the price from the actor's bank. Shows the reason and plays the deny sound on failure.</summary>
    private bool TryCharge(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, int price)
    {
        if (!TryComp<BankAccountComponent>(actor, out var bank))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-bank-account"));

        if (!HasCharacter(actor))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-character"));

        if (bank.Balance < price)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-insufficient-funds", ("price", price.ToString("N0"))));

        // The entity overload also updates the actor's cached balance, which the bank UI reads.
        if (!_bank.TryBankWithdraw(actor, price))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-transaction-failed"));

        return true;
    }

    /// <summary>Whether the actor is a connected player with a selected humanoid character.</summary>
    private bool HasCharacter(EntityUid actor)
    {
        return TryComp<ActorComponent>(actor, out var actorComp) &&
               _prefs.TryGetCachedPreferences(actorComp.PlayerSession.UserId, out var prefs) &&
               prefs.SelectedCharacter is HumanoidCharacterProfile;
    }

    /// <summary>True when the ship is the console's grid or the grid of one of its berth cranes.</summary>
    private bool IsShipyardGrid(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship)
    {
        if (Transform(console).GridUid == ship)
            return true;

        return TryGetBerth(console, comp, out var berth) && berth.Cranes.Any(c => Transform(c).GridUid == ship);
    }

    /// <summary>Starts a paid job: queues its items, then deploys the cranes over the ship.</summary>
    public void StartJob(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid? payer, List<WFDrydockWorkItem> items, int price, bool paidInMaterials = false)
    {
        if (IsShipyardGrid(console, comp, ship))
        {
            Log.Warning($"Refused a drydock job on its own grid {ToPrettyString(ship)} from {ToPrettyString(console)}.");
            return;
        }

        TurnOffBerthLights(console, comp);
        var job = AddComp<WFDrydockJobComponent>(console);
        job.Ship = ship;
        job.Payer = payer;
        job.PaidInMaterials = paidInMaterials;
        job.Total = items.Count;
        job.Break = items[0].Kind == WFDrydockWorkKind.Break;
        job.ShipName = Name(ship);
        job.Attacker = Transform(console).GridUid;

        AddSlots(console, comp, job);
        CollectSparkTiles(ship, job);

        // Boom paths are cached before the cranes swing out, so their first targets use them.
        ScanJobBooms(job);

        var curTime = _timing.CurTime;
        var deploy = DeployCranes(comp, job);
        var maxDuration = items[0].Kind switch
        {
            WFDrydockWorkKind.Upgrade => comp.MaxRefitDuration,
            WFDrydockWorkKind.Break => comp.MaxBreakDuration,
            _ => comp.MaxDuration,
        };
        job.WorkStarts = curTime + TimeSpan.FromSeconds(deploy);
        job.Deadline = curTime + TimeSpan.FromSeconds(maxDuration);
        QueueItems(comp, job, items, maxDuration - deploy);
        foreach (var item in items)
        {
            if (item.Kind == WFDrydockWorkKind.Upgrade && item.Upgrade is { } refit)
                job.Refits[refit] = Math.Max(job.Refits.GetValueOrDefault(refit), item.Tier);
        }
        job.NextSpark = job.WorkStarts;
        job.NextIndustry = curTime;
        job.NextBerthCheck = curTime;
        UpdateEstimate(job, curTime);

        if (payer is { } payerUid)
        {
            var message = items[0].Kind switch
            {
                WFDrydockWorkKind.Upgrade => Loc.GetString("drydock-console-refit-started", ("count", items.Count)),
                WFDrydockWorkKind.Break => Loc.GetString("drydock-console-break-started", ("ship", job.ShipName), ("count", items.Count)),
                _ => Loc.GetString("drydock-console-started", ("count", items.Count), ("price", price.ToString("N0"))),
            };
            _popup.PopupCursor(message, payerUid);
        }

        _audio.PlayPvs(comp.ConfirmSound, console);
        UpdateUi(console, comp);
    }

    /// <summary>One slot per berth crane; a berth without cranes still gets one slot.</summary>
    private void AddSlots(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job)
    {
        if (TryGetBerth(console, comp, out var berth))
        {
            foreach (var crane in berth.Cranes)
                job.Slots.Add(new WFDrydockJobSlot { Crane = crane });
        }

        if (job.Slots.Count == 0)
            job.Slots.Add(new WFDrydockJobSlot());
    }

    /// <summary>Sets each item's crane work time, scales the work to fit the budget in seconds, and queues it.</summary>
    private static void QueueItems(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, List<WFDrydockWorkItem> items, float budget)
    {
        var workTotal = 0f;
        foreach (var item in items)
        {
            item.WorkTime = item.Kind switch
            {
                WFDrydockWorkKind.Tile => comp.TileWorkTime,
                WFDrydockWorkKind.Entity => comp.EntityWorkTime,
                WFDrydockWorkKind.Heal => comp.HealWorkTime,
                WFDrydockWorkKind.Break => item.WorkTime,
                _ => comp.UpgradeWorkTime,
            };
            workTotal += item.WorkTime;
        }

        var span = workTotal / job.Slots.Count;
        var scale = span > budget ? Math.Max(0f, budget) / span : 1f;
        foreach (var item in items)
        {
            item.WorkTime *= scale;
            job.Pending.Add(item);
        }
    }

    /// <summary>Records the centers of the ship's tiles for ambient sparks, up to MaxSparkTiles.</summary>
    private void CollectSparkTiles(EntityUid ship, WFDrydockJobComponent job)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return;

        var tiles = _map.GetAllTilesEnumerator(ship, grid);
        while (job.SparkTiles.Count < MaxSparkTiles && tiles.MoveNext(out var tile))
        {
            var indices = tile!.Value.GridIndices;
            job.SparkTiles.Add(new Vector2(indices.X + 0.5f, indices.Y + 0.5f));
        }
    }

    /// <summary>Shows a refusal to the actor, plays the deny sound and returns false.</summary>
    private bool Deny(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, string message)
    {
        // At the cursor, so a caller working the console from their helm sees it too.
        _popup.PopupCursor(message, actor);
        _audio.PlayPvs(comp.DenySound, console);
        return false;
    }

    /// <summary>Whether a snapshot entity is gone, or its original sits on debris of the ship.</summary>
    private bool IsMissing(EntityUid ship, ShipRepairEntitySpecifier spec)
    {
        if (!TryGetOriginal(spec, out var original))
            return true;

        var grid = Transform(original).GridUid;
        return grid is { } onGrid && onGrid != ship && _hull.IsDebrisOf(onGrid, ship);
    }

    /// <summary>Resolves a spec's recorded original entity, or false if it no longer exists.</summary>
    private bool TryGetOriginal(ShipRepairEntitySpecifier spec, out EntityUid original)
    {
        original = EntityUid.Invalid;
        if (spec.OriginalEntity is not { } net ||
            !TryGetEntity(net, out var resolved) ||
            resolved is not { } uid ||
            TerminatingOrDeleted(uid))
            return false;

        original = uid;
        return true;
    }
}
