using System.Linq;
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._NF.Station.Components;
using Content.Server.Atmos.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Materials;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Access.Systems;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Player;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Hull breaking at a mobile drydock: the cranes peel a ship tile by tile from its edges inward, and pay out a share
/// of what it was built from.
/// </summary>
public sealed partial class WFDrydockSystem
{
    [Dependency] private WFCrewAlertSystem _crewAlert = default!;
    [Dependency] private FlatpackSystem _flatpack = default!;
    [Dependency] private MaterialStorageSystem _sheets = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedIdCardSystem _idCards = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;

    /// <summary>Flatpack that hardware stripped off a hull comes out in.</summary>
    private static readonly EntProtoId SalvagedFlatpack = "WFSalvagedFlatpack";

    /// <summary>Container a computer keeps its board in.</summary>
    private const string ComputerBoardContainer = "board";

    /// <summary>Damage type salvaged hardware unpacks with.</summary>
    private static readonly ProtoId<DamageTypePrototype> SalvageDamage = "Blunt";

    /// <summary>Damage past which a barrier counts as indestructible, such as monolith tenebris.</summary>
    private const float IndestructibleDamage = 50000;

    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>How long a broken tile waits for others to lift with it.</summary>
    private static readonly TimeSpan LiftBatch = TimeSpan.FromSeconds(10);

    /// <summary>Tile lookups a job's connectivity checks may make a tick; a hull that would split checks over several.</summary>
    private const int LiftCheckBudget = 1024;

    /// <summary>The eight tiles around a tile in ring order; even entries are its sides.</summary>
    private static readonly Vector2i[] Ring =
    {
        new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1), new(0, -1), new(1, -1),
    };

    // Scratch collections for the connectivity checks, reused so a busy break allocates nothing per check.
    private readonly HashSet<Vector2i> _liftExcluded = new();
    private readonly List<Vector2i> _liftNeighbours = new();
    private readonly Dictionary<Vector2i, int> _liftOwner = new();
    private readonly Queue<Vector2i>[] _liftQueues = { new(), new(), new(), new() };
    private readonly List<(int Index, float Distance)> _breakReach = new();
    private readonly List<CraneReach?> _breakJobReach = new();

    private void InitializeBreak()
    {
        SubscribeLocalEvent<WFDrydockConsoleComponent, WFDrydockBreakMessage>(OnBreak);
        SubscribeLocalEvent<WFSalvagedHardwareComponent, WFFlatpackUnpackedEvent>(OnSalvagedUnpacked);
        SubscribeLocalEvent<WFSalvagedHardwareComponent, PriceCalculationEvent>(OnSalvagedPrice);
    }

    private void OnBreak(EntityUid uid, WFDrydockConsoleComponent comp, WFDrydockBreakMessage args)
    {
        if (TryGetWorkableShip(uid, comp, args.Actor, out var ship))
            TryStartBreak(uid, comp, ship, args.Actor);
    }

    /// <summary>Whether a console breaks hulls: it pays in materials and has break yields.</summary>
    private bool CanBreak(EntityUid console, out WFDrydockMaterialPaymentComponent pay)
    {
        return TryComp(console, out pay!) && (pay.BreakDefaultYield > 0 || pay.BreakYield.Count > 0);
    }

    /// <summary>Starts breaking up the ship in this berth. Breaking a crewed ship is an attack on it.</summary>
    public bool TryStartBreak(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship, EntityUid actor)
    {
        if (!CanBreak(console, out var pay))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-break-unavailable"));

        if (BreakRefusal(console, comp, ship) is { } refusal)
            return Deny(console, comp, actor, refusal);

        if (!CranesReady(console, comp, actor, out var cranes))
            return false;

        if (cranes.Count == 0)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-cranes-blocked"));

        var items = BuildBreakItems(ship, comp);
        if (items.Count == 0)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-nothing-to-break"));

        EnsureComp<WFHulkComponent>(ship).LastWork = _timing.CurTime;
        StartJob(console, comp, ship, actor, items, 0, paidInMaterials: true);
        if (!TryComp<WFDrydockJobComponent>(console, out var job))
            return false;

        job.Attacker = BreakerVessel(console, ship, actor);
        job.Breaker = actor;
        _crewAlert.ReportBreaking(ship, job.Attacker, job.Breaker);

        if (TryComp<WFDrydockChopShopComponent>(console, out var chop))
        {
            SetUpChopPay(chop, job, items);
            if (IsLawfulProperty(chop, ship, actor))
                PostBreakWarrant(console, chop, ship, actor);
        }

        return true;
    }

    /// <summary>
    /// The vessel a break is blamed on: the console's own grid when it stands on a ship, as a mobile drydock does, else
    /// the nearest ship registered to the breaker's player, else the ship on the deed of their ID. Null when none is a
    /// ship other than the one being broken.
    /// </summary>
    private EntityUid? BreakerVessel(EntityUid console, EntityUid ship, EntityUid breaker)
    {
        if (Transform(console).GridUid is { } yard && yard != ship && HasComp<ShuttleDeedComponent>(yard))
            return yard;

        if (TryComp<ActorComponent>(breaker, out var actor))
        {
            var at = _transform.GetMapCoordinates(breaker);
            EntityUid? nearest = null;
            var best = float.MaxValue;
            var owned = EntityQueryEnumerator<ShipOwnershipComponent, TransformComponent>();
            while (owned.MoveNext(out var uid, out var ownership, out var xform))
            {
                if (uid == ship || ownership.OwnerUserId != actor.PlayerSession.UserId || xform.MapID != at.MapId)
                    continue;

                var distance = (_transform.GetWorldPosition(xform) - at.Position).LengthSquared();
                if (distance < best)
                {
                    best = distance;
                    nearest = uid;
                }
            }

            if (nearest != null)
                return nearest;
        }

        if (_idCards.TryFindIdCard(breaker, out var card) && _shipyard.TryGetDeedShip(card, out var deedShip) && deedShip != ship)
            return deedShip;

        return null;
    }

    /// <summary>Why a ship can't be broken at a console, or null.</summary>
    public string? BreakRefusal(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship)
    {
        if (IsShipyardGrid(console, comp, ship))
            return Loc.GetString("drydock-console-own-grid");

        if (IsOutpost(ship))
            return Loc.GetString("drydock-console-break-station");

        // A company hangar ship is relaunched free each round, so breaking it would print money.
        if (HasComp<Content.Server._WF.Companies.WFCompanyHangarShipComponent>(ship))
            return Loc.GetString("wf-company-break-refused");

        if (HasIndestructibleBarrier(ship))
            return Loc.GetString("drydock-console-break-indestructible");

        return null;
    }

    /// <summary>
    /// Whether a grid is part of a station or outpost rather than a ship. A ship can be a station of its own: one bought
    /// carries a deed, and one spawned without an owner still has a vessel station.
    /// </summary>
    private bool IsOutpost(EntityUid grid)
    {
        if (!TryComp<StationMemberComponent>(grid, out var member) || HasComp<ShuttleDeedComponent>(grid))
            return false;

        return !HasComp<ExtraShuttleInformationComponent>(member.Station);
    }

    /// <summary>Whether a grid has a damageable wall, window or door that can't be destroyed, such as a vault's.</summary>
    private bool HasIndestructibleBarrier(EntityUid ship)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return false;

        var barriers = new HashSet<Entity<AirtightComponent>>();
        _lookup.GetLocalEntitiesIntersecting(ship, grid.LocalAABB, barriers);
        return barriers.Any(b => HasComp<DamageableComponent>(b) && _destructible.DestroyedAt(b).Float() >= IndestructibleDamage);
    }

    /// <summary>Lists a break item per hull tile, timed by the entities its crane strips off it.</summary>
    public List<WFDrydockWorkItem> BuildBreakItems(EntityUid ship, WFDrydockConsoleComponent comp)
    {
        var items = new List<WFDrydockWorkItem>();
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return items;

        var tiles = _map.GetAllTilesEnumerator(ship, grid);
        while (tiles.MoveNext(out var tile))
        {
            var index = tile.Value.GridIndices;
            var strip = 0;
            var anchored = _map.GetAnchoredEntitiesEnumerator(ship, grid, index);
            while (anchored.MoveNext(out var uid))
            {
                if (HasComp<ShipRepairableComponent>(uid))
                    strip++;
            }

            items.Add(new WFDrydockWorkItem
            {
                Kind = WFDrydockWorkKind.Break,
                TileIndex = index,
                LocalPos = TileCentre(index),
                WorkTime = comp.BreakEntityWorkTime * strip + comp.BreakTileWorkTime,
            });
        }

        return items;
    }

    /// <summary>
    /// Removes and returns a slot's next break tile: the nearest its crane reaches that can be lifted now, else the
    /// nearest no crane reaches, to be given up. Null when neither is left.
    /// </summary>
    private WFDrydockWorkItem? TakeBreakTile(WFDrydockConsoleComponent comp, WFDrydockJobComponent job, WFDrydockJobSlot slot)
    {
        var own = ReachOf(job.Ship, slot.Crane);
        var all = _breakJobReach;
        JobReach(job, all);
        var reach = _breakReach;
        reach.Clear();
        var stranded = -1;
        var strandedDistance = float.MaxValue;
        for (var i = 0; i < job.Pending.Count; i++)
        {
            var pos = job.Pending[i].LocalPos;
            var distance = (pos - slot.ToolPos).LengthSquared();
            if (Reaches(comp, own, pos))
                reach.Add((i, distance));
            else if (distance < strandedDistance && !ReachableByAny(comp, all, pos))
            {
                stranded = i;
                strandedDistance = distance;
            }
        }

        // Only the nearest few usually need the costlier connectivity check. Tiles found to split the hull are skipped
        // until another tile comes off, and once this tick's checks are spent the rest wait for the next.
        reach.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var best = stranded;
        foreach (var (index, _) in reach)
        {
            var tile = job.Pending[index].TileIndex;
            if (job.Unliftable.Contains(tile))
                continue;

            if (job.LiftCheckBudget <= 0)
            {
                job.LiftChecksDeferred = true;
                return null;
            }

            if (!Liftable(job, slot, tile))
            {
                job.Unliftable.Add(tile);
                continue;
            }

            best = index;
            break;
        }

        if (best < 0)
            return null;

        var item = job.Pending[best];
        job.Pending[best] = job.Pending[^1];
        job.Pending.RemoveAt(job.Pending.Count - 1);
        job.Unliftable.Clear();
        return item;
    }

    /// <summary>
    /// Whether a tile can be lifted now: it has an open side, so the hull peels from its edges inward, and the hull stays
    /// in one piece without it and the tiles other cranes are lifting.
    /// </summary>
    private bool Liftable(WFDrydockJobComponent job, WFDrydockJobSlot slot, Vector2i tile)
    {
        if (!TryComp<MapGridComponent>(job.Ship, out var grid))
            return true;

        var lifting = _liftExcluded;
        lifting.Clear();
        lifting.Add(tile);
        foreach (var other in job.Slots)
        {
            if (other != slot && other.Item is { Kind: WFDrydockWorkKind.Break } item)
                lifting.Add(item.TileIndex);
        }

        var neighbours = _liftNeighbours;
        neighbours.Clear();
        foreach (var step in Neighbours)
        {
            if (LiftPresent(job, grid, tile + step))
                neighbours.Add(tile + step);
        }

        if (neighbours.Count == Neighbours.Length)
            return false;

        if (neighbours.Count <= 1)
            return true;

        // Most edge tiles have their neighbours joined through the ring of eight around them.
        if (RingJoins(job, grid, tile))
            return true;

        return NeighboursJoin(job, grid, neighbours);
    }

    /// <summary>Whether a tile is still standing for a connectivity check, spending one of the job's lookups.</summary>
    private bool LiftPresent(WFDrydockJobComponent job, MapGridComponent grid, Vector2i index)
    {
        job.LiftCheckBudget--;
        return !_liftExcluded.Contains(index) && !job.Lifting.Contains(index) &&
               !_map.GetTileRef(job.Ship, grid, index).Tile.IsEmpty;
    }

    /// <summary>Whether a tile's present side neighbours lie on one unbroken arc of its eight surrounding tiles.</summary>
    private bool RingJoins(WFDrydockJobComponent job, MapGridComponent grid, Vector2i tile)
    {
        Span<bool> ring = stackalloc bool[Ring.Length];
        var start = -1;
        for (var i = 0; i < Ring.Length; i++)
        {
            ring[i] = LiftPresent(job, grid, tile + Ring[i]);
            if (!ring[i] && i % 2 == 0)
                start = i;
        }

        // Walks the ring from an empty side, counting arcs that hold a side neighbour.
        var arcs = 0;
        var inArc = false;
        var arcHasSide = false;
        for (var n = 1; n <= Ring.Length; n++)
        {
            var i = (start + n) % Ring.Length;
            if (ring[i])
            {
                inArc = true;
                arcHasSide |= i % 2 == 0;
                continue;
            }

            if (inArc && arcHasSide)
                arcs++;
            inArc = false;
            arcHasSide = false;
        }

        return arcs <= 1;
    }

    /// <summary>
    /// Whether the given tiles still reach each other. Floods from each in turn and stops as soon as one flood closes
    /// without meeting the rest, so a split costs the smaller piece rather than the whole hull.
    /// </summary>
    private bool NeighboursJoin(WFDrydockJobComponent job, MapGridComponent grid, List<Vector2i> starts)
    {
        var owner = _liftOwner;
        owner.Clear();
        Span<int> parent = stackalloc int[Neighbours.Length];
        for (var i = 0; i < starts.Count; i++)
        {
            parent[i] = i;
            owner[starts[i]] = i;
            _liftQueues[i].Clear();
            _liftQueues[i].Enqueue(starts[i]);
        }

        int Find(Span<int> p, int i)
        {
            while (p[i] != i)
                i = p[i];
            return i;
        }

        var groups = starts.Count;
        Span<bool> open = stackalloc bool[Neighbours.Length];
        while (true)
        {
            for (var i = 0; i < starts.Count; i++)
            {
                if (!_liftQueues[i].TryDequeue(out var at))
                    continue;

                foreach (var step in Neighbours)
                {
                    var next = at + step;
                    if (owner.TryGetValue(next, out var other))
                    {
                        var a = Find(parent, i);
                        var b = Find(parent, other);
                        if (a != b)
                        {
                            parent[b] = a;
                            if (--groups == 1)
                                return true;
                        }

                        continue;
                    }

                    if (!LiftPresent(job, grid, next))
                        continue;

                    owner[next] = i;
                    _liftQueues[i].Enqueue(next);
                }
            }

            // A joined group whose floods have all run dry is cut off from the others.
            open.Clear();
            for (var i = 0; i < starts.Count; i++)
            {
                if (_liftQueues[i].Count > 0)
                    open[Find(parent, i)] = true;
            }

            for (var i = 0; i < starts.Count; i++)
            {
                if (Find(parent, i) == i && !open[i])
                    return false;
            }
        }
    }

    /// <summary>Gives up a break's tiles that no crane can lift without splitting the hull, leaving them standing.</summary>
    private static void LeaveStanding(WFDrydockJobComponent job)
    {
        job.Done += job.Pending.Count;
        job.Skipped += job.Pending.Count;
        job.Pending.Clear();
    }

    /// <summary>
    /// Whether a job's ship is still in its berth. A hull being broken only has to overlap the berth, since peeling it
    /// moves its centre.
    /// </summary>
    private bool StillBerthed(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job)
    {
        if (!job.Break)
            return GetBerthedShip(console, comp) == job.Ship;

        if (!TryGetBerth(console, comp, out var berth) || !TryComp<MapGridComponent>(job.Ship, out var grid))
            return false;

        var berthArea = _transform.GetWorldMatrix(berth.Grid).TransformBox(berth.Area);
        return berthArea.Intersects(_transform.GetWorldMatrix(job.Ship).TransformBox(grid.LocalAABB));
    }

    /// <summary>What breaking the whole ship would pay now.</summary>
    private Dictionary<string, int> BreakPreview(EntityUid ship, WFDrydockMaterialPaymentComponent pay)
    {
        var total = new Dictionary<string, int>();
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return total;

        var tiles = _map.GetAllTilesEnumerator(ship, grid);
        while (tiles.MoveNext(out var tile))
        {
            AddTileYield(ship, grid, tile.Value.GridIndices, pay, total);
        }

        return total;
    }

    /// <summary>
    /// Adds what breaking one tile recovers: a share of the construction materials of everything anchored on it, except
    /// hardware that comes off whole, and the tile's own yield.
    /// </summary>
    private void AddTileYield(EntityUid ship, MapGridComponent grid, Vector2i index, WFDrydockMaterialPaymentComponent pay,
        Dictionary<string, int> yield)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(ship, grid, index);
        while (anchored.MoveNext(out var uid))
        {
            if (!IsSalvagedWhole(uid.Value) && MetaData(uid.Value).EntityPrototype is { } proto)
                AddBreakYield(pay, proto, yield);
        }

        AddPerTileYield(pay, yield);
    }

    /// <summary>Adds what every lifted tile pays whatever stood on it: its own yield and its salvage.</summary>
    private static void AddPerTileYield(WFDrydockMaterialPaymentComponent pay, Dictionary<string, int> yield)
    {
        foreach (var (id, amount) in pay.BreakTileYield.Concat(pay.BreakTileSalvage))
        {
            yield[id] = yield.GetValueOrDefault(id) + amount;
        }
    }

    /// <summary>Adds the share of an entity's construction materials a break recovers.</summary>
    private void AddBreakYield(WFDrydockMaterialPaymentComponent pay, EntityPrototype proto, Dictionary<string, int> yield)
    {
        var wall = IsWall(proto);
        foreach (var (id, amount) in ConstructionMaterials(proto))
        {
            var units = (int) (amount * (wall ? pay.StructureYield : BreakShare(pay, id)));
            if (units > 0)
                yield[id] = yield.GetValueOrDefault(id) + units;
        }
    }

    /// <summary>Share of a material a break recovers: its own, or the default.</summary>
    private static float BreakShare(WFDrydockMaterialPaymentComponent pay, string material)
    {
        return pay.BreakYield.TryGetValue(material, out var share) ? share : pay.BreakDefaultYield;
    }

    /// <summary>Whether a stripped entity comes off whole as salvaged hardware: a gun on a hardpoint.</summary>
    private bool IsSalvagedWhole(EntityUid uid)
    {
        return HasComp<HardpointAnchorableOnlyComponent>(uid);
    }

    /// <summary>
    /// Breaks one tile: packs its hardware for the crane, strips everything else anchored on it, credits the yield to
    /// the crane, reports the attack, then lifts the tile. Everyone and everything still aboard is set adrift before the
    /// last tile goes, so nothing is deleted with the hull.
    /// </summary>
    private void ApplyBreak(EntityUid console, WFDrydockJobComponent job, WFDrydockJobSlot slot, WFDrydockWorkItem item)
    {
        var ship = job.Ship;
        if (TerminatingOrDeleted(ship) || !TryComp<MapGridComponent>(ship, out var grid) ||
            job.Lifting.Contains(item.TileIndex) || _map.GetTileRef(ship, grid, item.TileIndex).Tile.IsEmpty)
            return;

        var pay = CompOrNull<WFDrydockMaterialPaymentComponent>(console);
        // The slot's crane may have been destroyed mid-job.
        var crane = slot.Crane is { } own && !TerminatingOrDeleted(own)
            ? own
            : JobCranes(job).FirstOrDefault(c => !TerminatingOrDeleted(c));
        var drop = DropSpot(crane.Valid ? crane : console);

        var yield = new Dictionary<string, int>();
        var anchored = new List<EntityUid>();
        _map.GetAnchoredEntities((ship, grid), item.TileIndex, anchored);
        foreach (var uid in anchored)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            ReleaseMobs(uid);
            if (IsSalvagedWhole(uid))
            {
                SalvageHardware(uid, crane, drop);
            }
            else
            {
                SalvageParts(uid, crane, drop);
                if (pay != null && MetaData(uid).EntityPrototype is { } proto)
                    AddBreakYield(pay, proto, yield);
            }

            // Deleted now, not queued: lifting the tile below unanchors what's left, and a cable unanchored alive
            // drops a coil as if cut.
            Del(uid);
        }

        if (pay != null)
            AddPerTileYield(pay, yield);

        EnsureComp<WFHulkComponent>(ship).LastWork = _timing.CurTime;
        // A chop shop pays in thalers, not materials.
        var chop = HasComp<WFDrydockChopShopComponent>(console);
        if (yield.Count > 0 && !chop)
        {
            DepositYield(crane, drop, yield);
            AddBill(job.Yield, yield);
        }

        _crewAlert.ReportBreaking(ship, job.Attacker, job.Breaker);

        var last = IsLastTile(job, grid);
        if (last)
        {
            job.HullGone = true;
            SetAdrift(ship);
        }

        if (chop)
            PrintChopCash(console, job, item);

        // Lifting a tile casts off whoever stands on it; lifting the last one deletes the grid.
        if (job.Lifting.Count == 0)
            job.LiftAt = _timing.CurTime + LiftBatch;
        job.Lifting.Add(item.TileIndex);
        if (!last)
            return;

        LiftTiles(job);
        if (!TerminatingOrDeleted(ship))
            QueueDel(ship);
    }

    /// <summary>Lifts a break's finished tiles in one change.</summary>
    private void LiftTiles(WFDrydockJobComponent job)
    {
        if (job.Lifting.Count == 0)
            return;

        if (!TerminatingOrDeleted(job.Ship) && TryComp<MapGridComponent>(job.Ship, out var grid))
        {
            var tiles = new List<(Vector2i, Tile)>(job.Lifting.Count);
            foreach (var index in job.Lifting)
            {
                tiles.Add((index, Tile.Empty));
            }

            _map.SetTiles(job.Ship, grid, tiles);
        }

        job.Lifting.Clear();
    }

    /// <summary>Whether a grid has one tile left, or none, not counting tiles waiting to lift.</summary>
    private bool IsLastTile(WFDrydockJobComponent job, MapGridComponent grid)
    {
        var tiles = _map.GetAllTilesEnumerator(job.Ship, grid);
        var count = 0;
        while (count < 2 && tiles.MoveNext(out var tile))
        {
            if (!job.Lifting.Contains(tile.Value.GridIndices))
                count++;
        }

        return count <= 1;
    }

    /// <summary>Puts materials in a crane's silo or its own storage, or drops them as sheets at it when neither takes them.</summary>
    private void DepositYield(EntityUid crane, EntityCoordinates drop, Dictionary<string, int> credit)
    {
        if (crane.Valid && (CraneSilo(crane) ?? crane) is var store && TryChangeLocal(store, credit))
            return;

        foreach (var (id, amount) in credit)
        {
            _sheets.SpawnMultipleFromMaterial(amount, id, drop);
        }
    }

    /// <summary>Takes every mob out of a stripped entity's containers, so nobody is deleted with it.</summary>
    private void ReleaseMobs(EntityUid uid)
    {
        if (!TryComp<ContainerManagerComponent>(uid, out var manager))
            return;

        foreach (var container in _container.GetAllContainers(uid, manager).ToList())
        {
            foreach (var contained in container.ContainedEntities.ToList())
            {
                if (HasComp<MobStateComponent>(contained))
                    _container.Remove(contained, container);
            }
        }
    }

    /// <summary>Packs a gun stripped off a hull into a salvaged flatpack, put in the crane's parts silo or left at it.</summary>
    private void SalvageHardware(EntityUid uid, EntityUid crane, EntityCoordinates drop)
    {
        if (MetaData(uid).EntityPrototype is not { } proto)
            return;

        var pack = Spawn(SalvagedFlatpack, drop);
        _flatpack.SetupSalvagedFlatpack(pack, proto.ID,
            Loc.GetString("drydock-salvaged-flatpack-name", ("name", proto.Name)),
            Loc.GetString("drydock-salvaged-flatpack-description", ("name", proto.Name)));

        if (crane.Valid)
            _fabricationSilo.ReturnPart(crane, pack);
    }

    /// <summary>
    /// Takes a stripped machine's board and parts, or a computer's board, out to the crane: into its parts silo when the
    /// silo takes them, else onto the floor beside it.
    /// </summary>
    private void SalvageParts(EntityUid uid, EntityUid crane, EntityCoordinates drop)
    {
        var containers = new List<BaseContainer>();
        if (TryComp<MachineComponent>(uid, out var machine))
        {
            containers.Add(machine.BoardContainer);
            containers.Add(machine.PartContainer);
        }

        if (HasComp<ComputerComponent>(uid) && _container.TryGetContainer(uid, ComputerBoardContainer, out var board))
            containers.Add(board);

        foreach (var container in containers)
        {
            foreach (var part in container.ContainedEntities.ToList())
            {
                // Sheets are paid in the yield.
                if (HasComp<MaterialComponent>(part))
                    continue;

                if (_container.Remove(part, container, destination: drop) && crane.Valid)
                    _fabricationSilo.ReturnPart(crane, part);
            }
        }
    }

    /// <summary>Moves everything still aboard a hull off it where it stands, so deleting the hull deletes none of it.</summary>
    private void SetAdrift(EntityUid ship)
    {
        var xform = Transform(ship);
        if (xform.MapUid is not { } map)
            return;

        var aboard = new List<EntityUid>();
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            aboard.Add(child);
        }

        foreach (var child in aboard)
        {
            if (TerminatingOrDeleted(child) || EntityManager.IsQueuedForDeletion(child))
                continue;

            var childXform = Transform(child);
            if (childXform.Anchored)
                _transform.Unanchor(child, childXform);

            _transform.SetParent(child, childXform, map);
        }
    }

    /// <summary>Ends a break and reports what the cranes recovered.</summary>
    private void EndBreak(EntityUid console, WFDrydockConsoleComponent comp, WFDrydockJobComponent job, JobEnd end)
    {
        var yield = HasComp<WFDrydockChopShopComponent>(console)
            ? Loc.GetString("drydock-chop-yield", ("cash", job.CashPaid))
            : job.Yield.Count == 0 ? Loc.GetString("drydock-console-materials-none") : FormatMaterials(job.Yield);
        var message = end switch
        {
            JobEnd.Finished when job.HullGone || job.Skipped == 0 =>
                Loc.GetString("drydock-console-break-finished", ("ship", job.ShipName), ("yield", yield)),
            JobEnd.Finished =>
                Loc.GetString("drydock-console-break-finished-skipped", ("ship", job.ShipName), ("count", job.Skipped), ("yield", yield)),
            JobEnd.Unpowered => Loc.GetString("drydock-console-break-aborted-power", ("yield", yield)),
            _ => Loc.GetString("drydock-console-break-aborted", ("yield", yield)),
        };

        Notify(console, comp, job, message);
        _audio.PlayPvs(end == JobEnd.Finished ? comp.ConfirmSound : comp.DenySound, console);
    }

    /// <summary>Unpacks salvaged hardware with its share of the damage that would destroy it, for a drydock to patch.</summary>
    private void OnSalvagedUnpacked(Entity<WFSalvagedHardwareComponent> ent, ref WFFlatpackUnpackedEvent args)
    {
        var destroyedAt = _destructible.DestroyedAt(args.Spawned);
        if (destroyedAt == FixedPoint2.MaxValue || !_prototype.TryIndex(SalvageDamage, out var type))
            return;

        _damageable.TryChangeDamage(args.Spawned, new DamageSpecifier(type, destroyedAt * ent.Comp.Damage), ignoreResistances: true);
    }

    /// <summary>A salvaged flatpack sells for its share of the hardware's price.</summary>
    private void OnSalvagedPrice(Entity<WFSalvagedHardwareComponent> ent, ref PriceCalculationEvent args)
    {
        if (args.Handled || CompOrNull<FlatpackComponent>(ent)?.Entity is not { } proto || !_prototype.TryIndex(proto, out var hardware))
            return;

        args.Price = _pricing.GetEstimatedPrice(hardware) * ent.Comp.Resale;
        args.Handled = true;
    }

    private static Vector2 TileCentre(Vector2i index)
    {
        return new Vector2(index.X + 0.5f, index.Y + 0.5f);
    }
}
