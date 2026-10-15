using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Materials;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Hull breaking at a mobile drydock: the cranes peel a ship tile by tile from its edges inward, and pay out a share
/// of what it was built from, within caps set by its size.
/// </summary>
public sealed partial class WFDrydockSystem
{
    [Dependency] private WFCrewAlertSystem _crewAlert = default!;
    [Dependency] private FlatpackSystem _flatpack = default!;
    [Dependency] private MaterialStorageSystem _sheets = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    /// <summary>Flatpack that hardware stripped off a hull comes out in.</summary>
    private static readonly EntProtoId SalvagedFlatpack = "WFSalvagedFlatpack";

    /// <summary>Container a computer keeps its board in.</summary>
    private const string ComputerBoardContainer = "board";

    /// <summary>Damage type salvaged hardware unpacks with.</summary>
    private static readonly ProtoId<DamageTypePrototype> SalvageDamage = "Blunt";

    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

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

        if (job.Attacker is { } attacker)
            _crewAlert.ReportBreaking(ship, attacker);

        return true;
    }

    /// <summary>Why a ship can't be broken at a console, or null. Stations, outposts and the drydock's own grid are refused.</summary>
    public string? BreakRefusal(EntityUid console, WFDrydockConsoleComponent comp, EntityUid ship)
    {
        if (IsShipyardGrid(console, comp, ship))
            return Loc.GetString("drydock-console-own-grid");

        // A bought ship is a station of its own but carries a deed; a station member without one is an outpost or POI.
        if (HasComp<StationMemberComponent>(ship) && !HasComp<ShuttleDeedComponent>(ship))
            return Loc.GetString("drydock-console-break-station");

        return null;
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
        var reach = new List<(int Index, float Distance)>();
        var stranded = -1;
        var strandedDistance = float.MaxValue;
        for (var i = 0; i < job.Pending.Count; i++)
        {
            var pos = job.Pending[i].LocalPos;
            var distance = (pos - slot.ToolPos).LengthSquared();
            if (CanReach(comp, job.Ship, slot.Crane, pos))
                reach.Add((i, distance));
            else if (distance < strandedDistance && !ReachableByJob(comp, job, pos))
            {
                stranded = i;
                strandedDistance = distance;
            }
        }

        // Only the nearest few usually need the costlier connectivity check.
        reach.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var best = stranded;
        foreach (var (index, _) in reach)
        {
            if (!Liftable(job, slot, job.Pending[index].TileIndex))
                continue;

            best = index;
            break;
        }

        if (best < 0)
            return null;

        var item = job.Pending[best];
        job.Pending[best] = job.Pending[^1];
        job.Pending.RemoveAt(job.Pending.Count - 1);
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

        var lifting = new HashSet<Vector2i> { tile };
        foreach (var other in job.Slots)
        {
            if (other != slot && other.Item is { Kind: WFDrydockWorkKind.Break } item)
                lifting.Add(item.TileIndex);
        }

        bool Present(Vector2i index) => !lifting.Contains(index) && !_map.GetTileRef(job.Ship, grid, index).Tile.IsEmpty;

        var neighbours = new List<Vector2i>();
        foreach (var step in Neighbours)
        {
            if (Present(tile + step))
                neighbours.Add(tile + step);
        }

        if (neighbours.Count == Neighbours.Length)
            return false;

        if (neighbours.Count <= 1)
            return true;

        // Its neighbours must still reach each other around it.
        var unfound = new HashSet<Vector2i>(neighbours.Skip(1));
        var seen = new HashSet<Vector2i> { neighbours[0] };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(neighbours[0]);
        while (queue.TryDequeue(out var at))
        {
            foreach (var step in Neighbours)
            {
                var next = at + step;
                if (!Present(next) || !seen.Add(next))
                    continue;

                if (unfound.Remove(next) && unfound.Count == 0)
                    return true;

                queue.Enqueue(next);
            }
        }

        return false;
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

    /// <summary>What breaking the whole ship would pay now, within its caps.</summary>
    private Dictionary<string, int> BreakPreview(EntityUid ship, WFDrydockMaterialPaymentComponent pay)
    {
        var total = new Dictionary<string, int>();
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return total;

        var count = 0;
        var tiles = _map.GetAllTilesEnumerator(ship, grid);
        while (tiles.MoveNext(out var tile))
        {
            count++;
            AddTileYield(ship, grid, tile.Value.GridIndices, pay, total);
        }

        var caps = BreakCaps(pay, count);
        var preview = new Dictionary<string, int>();
        foreach (var (id, amount) in total)
        {
            var capped = caps.TryGetValue(id, out var cap) ? Math.Min(amount, cap) : amount;
            if (capped > 0)
                preview[id] = capped;
        }

        return preview;
    }

    /// <summary>Most of each capped material breaking so many tiles may pay.</summary>
    private static Dictionary<string, int> BreakCaps(WFDrydockMaterialPaymentComponent pay, int tiles)
    {
        return pay.BreakCap.ToDictionary(c => c.Key.Id, c => (int) (c.Value * tiles));
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
                AddBreakYield(pay, ConstructionMaterials(proto), yield);
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

    /// <summary>Adds the share of a set of materials a break recovers.</summary>
    private static void AddBreakYield(WFDrydockMaterialPaymentComponent pay, Dictionary<string, int> materials, Dictionary<string, int> yield)
    {
        foreach (var (id, amount) in materials)
        {
            var units = (int) (amount * BreakShare(pay, id));
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
    /// the crane within the caps of the tiles lifted so far, reports the attack, then lifts the tile. Everyone and everything still aboard
    /// is set adrift before the last tile goes, so nothing is deleted with the hull.
    /// </summary>
    private void ApplyBreak(EntityUid console, WFDrydockJobComponent job, WFDrydockJobSlot slot, WFDrydockWorkItem item)
    {
        var ship = job.Ship;
        if (TerminatingOrDeleted(ship) || !TryComp<MapGridComponent>(ship, out var grid) ||
            _map.GetTileRef(ship, grid, item.TileIndex).Tile.IsEmpty)
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
                    AddBreakYield(pay, ConstructionMaterials(proto), yield);
            }

            QueueDel(uid);
        }

        if (pay != null)
            AddPerTileYield(pay, yield);

        EnsureComp<WFHulkComponent>(ship).Broken.Add(item.TileIndex);
        var credit = CreditBreak(ship, job, pay, yield);
        if (credit.Count > 0)
        {
            DepositYield(crane, drop, credit);
            AddBill(job.Yield, credit);
        }

        if (job.Attacker is { } attacker && !TerminatingOrDeleted(attacker))
            _crewAlert.ReportBreaking(ship, attacker);

        var last = IsLastTile(ship, grid);
        if (last)
        {
            job.HullGone = true;
            SetAdrift(ship);
        }

        // Lifting a tile casts off whoever stands on it; lifting the last one deletes the grid.
        _map.SetTile(ship, grid, item.TileIndex, Tile.Empty);
        if (last && !TerminatingOrDeleted(ship))
            QueueDel(ship);
    }

    /// <summary>
    /// Raises a broken item's bill to its full materials: a rebuild's construction materials, or a tile's own yield
    /// grossed up by the share a break recovers of it.
    /// </summary>
    private void BillBroken(WFDrydockMaterialPaymentComponent pay, WFDrydockWorkItem item)
    {
        if (item.Materials == null)
            return;

        var full = new Dictionary<string, int>();
        if (item.Kind == WFDrydockWorkKind.Tile)
        {
            foreach (var (id, amount) in pay.BreakTileYield)
            {
                var recovered = BreakShare(pay, id.Id);
                var share = recovered > 0 ? recovered : 1f;
                full[id.Id] = (int) Math.Ceiling(amount / share);
            }
        }
        else if (item.Prototype is { } protoId && _prototype.TryIndex(protoId, out var proto))
        {
            full = ConstructionMaterials(proto);
        }

        foreach (var (id, amount) in full)
        {
            item.Materials[id] = Math.Max(item.Materials.GetValueOrDefault(id), amount);
        }
    }

    /// <summary>Whether a grid has one tile left, or none.</summary>
    private bool IsLastTile(EntityUid ship, MapGridComponent grid)
    {
        var tiles = _map.GetAllTilesEnumerator(ship, grid);
        var count = 0;
        while (count < 2 && tiles.MoveNext(out _))
        {
            count++;
        }

        return count <= 1;
    }

    /// <summary>
    /// Adds a lifted tile's yield to the job and returns what it now owes: the yield so far, held to the caps of the
    /// tiles lifted so far. Yield past them waits for later tiles, so a whole hull pays its full caps, and each part of
    /// one broken in its own job pays only for its own tiles.
    /// </summary>
    private Dictionary<string, int> CreditBreak(EntityUid ship, WFDrydockJobComponent job,
        WFDrydockMaterialPaymentComponent? pay, Dictionary<string, int> yield)
    {
        EnsureComp<WFHulkComponent>(ship).LastWork = _timing.CurTime;
        job.Lifted++;
        AddBill(job.Recovered, yield);

        var caps = pay != null ? BreakCaps(pay, job.Lifted) : new Dictionary<string, int>();
        var credit = new Dictionary<string, int>();
        foreach (var (id, recovered) in job.Recovered)
        {
            var owed = caps.TryGetValue(id, out var cap) ? Math.Min(recovered, cap) : recovered;
            var take = owed - job.Yield.GetValueOrDefault(id);
            if (take > 0)
                credit[id] = take;
        }

        return credit;
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
        var yield = job.Yield.Count == 0 ? Loc.GetString("drydock-console-materials-none") : FormatMaterials(job.Yield);
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
