using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>Asks the abductor holding a prisoner to take their ransom. Raised on the cell.</summary>
[ByRefEvent]
public readonly record struct WFAbductRansomOfferedEvent(EntityUid Payer);

/// <summary>An abductor's cell: its chair and door, and letting a prisoner go.</summary>
public sealed partial class WFAbductCellSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedCuffableSystem _cuffable = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;

    private readonly HashSet<Entity<AirlockComponent>> _doorsNear = new();
    private readonly HashSet<Entity<WFCrewComponent>> _crewNear = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFAbductCellComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    /// <summary>The prisoner pays from the chair: cuffed hands can still sign for it.</summary>
    private void OnGetVerbs(Entity<WFAbductCellComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (ent.Comp.Prisoner != args.User)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-encounter-abduct-verb-pay", ("ransom", ent.Comp.Ransom)),
            Act = () =>
            {
                var ev = new WFAbductRansomOfferedEvent(user);
                RaiseLocalEvent(ent, ref ev);
            },
        });
    }

    /// <summary>The first cell chair aboard a grid.</summary>
    public EntityUid? FindCell(EntityUid grid)
    {
        var cells = EntityQueryEnumerator<WFAbductCellComponent, TransformComponent>();
        while (cells.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return uid;
        }

        return null;
    }

    /// <summary>Whether someone sits buckled in a cell's chair.</summary>
    public bool IsHeld(EntityUid prisoner, EntityUid cell)
    {
        return TryComp<BuckleComponent>(prisoner, out var buckle) && buckle.BuckledTo == cell;
    }

    /// <summary>Whoever sits buckled in a cell's chair, if anyone.</summary>
    public EntityUid? Buckled(EntityUid cell)
    {
        return CompOrNull<StrapComponent>(cell)?.BuckledEntities.Cast<EntityUid?>().FirstOrDefault();
    }

    /// <summary>Whether someone is on their feet outside a cell: broken out, not just out of the chair.</summary>
    public bool IsLoose(EntityUid prisoner, EntityUid cell)
    {
        if (IsHeld(prisoner, cell))
            return false;

        var range = CompOrNull<WFAbductCellComponent>(cell)?.DoorRange ?? 2.5f;
        return !_transform.InRange(Transform(prisoner).Coordinates, Transform(cell).Coordinates, range);
    }

    /// <summary>Puts a cell on the hook for a prisoner, so they can buy their way out from the chair.</summary>
    public void Hold(Entity<WFAbductCellComponent> cell, EntityUid prisoner, int ransom)
    {
        cell.Comp.Prisoner = prisoner;
        cell.Comp.Ransom = ransom;
    }

    /// <summary>
    /// Shuts and bolts the cell's door once no living crewman is left inside. One look: called again until true.
    /// A door left bolted open is unbolted first.
    /// </summary>
    public bool Seal(EntityUid cell)
    {
        if (!TryComp<WFAbductCellComponent>(cell, out var comp))
            return false;

        var at = Transform(cell).Coordinates;
        _crewNear.Clear();
        _lookup.GetEntitiesInRange(at, comp.DoorRange, _crewNear);
        foreach (var crew in _crewNear)
        {
            if (_mobs.IsAlive(crew))
                return false;
        }

        var sealedUp = true;
        _doorsNear.Clear();
        _lookup.GetEntitiesInRange(at, comp.DoorRange, _doorsNear);
        foreach (var airlock in _doorsNear)
        {
            if (!TryComp<DoorComponent>(airlock, out var door) || !TryComp<DoorBoltComponent>(airlock, out var bolts))
                continue;

            if (door.State == DoorState.Closed)
            {
                _doors.SetBoltsDown((airlock, bolts), true);
                continue;
            }

            sealedUp = false;
            if (bolts.BoltsDown)
                _doors.SetBoltsDown((airlock, bolts), false);
            _doors.TryClose(airlock, door);
        }

        return sealedUp;
    }

    /// <summary>Leaves the cell's door bolted open and the chair off the hook: anyone can walk in and out.</summary>
    public void Open(EntityUid cell)
    {
        if (!TryComp<WFAbductCellComponent>(cell, out var comp))
            return;

        comp.Prisoner = null;
        _doorsNear.Clear();
        _lookup.GetEntitiesInRange(Transform(cell).Coordinates, comp.DoorRange, _doorsNear);
        foreach (var airlock in _doorsNear)
        {
            if (!TryComp<DoorComponent>(airlock, out var door) || !TryComp<DoorBoltComponent>(airlock, out var bolts))
                continue;

            _doors.SetBoltsDown((airlock, bolts), false);
            _doors.TryOpen(airlock, door);
            _doors.SetBoltsDown((airlock, bolts), true);
        }
    }

    /// <summary>Takes off a prisoner's cuffs and gets them out of the chair.</summary>
    public void Unbind(EntityUid prisoner)
    {
        if (TryComp<CuffableComponent>(prisoner, out var cuffable))
        {
            foreach (var cuffs in cuffable.Container.ContainedEntities.ToArray())
            {
                _cuffable.Uncuff(prisoner, null, cuffs, cuffable);
            }
        }

        _buckle.Unbuckle(prisoner, null);
    }

    /// <summary>Puts someone on safe deck aboard a grid. False if none was found.</summary>
    public bool SendAboard(EntityUid mob, EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var tiles = _maps.GetAllTiles(grid, gridComp).Select(tile => tile.GridIndices).ToList();
        _random.Shuffle(tiles);

        // Any open deck will do on a ship with no safe air left to offer: being home beats being held. Never inside a wall.
        Vector2i? at = null;
        foreach (var tile in tiles)
        {
            if (_planner.IsSafePost(grid, tile, gridComp))
            {
                at = tile;
                break;
            }

            if (at == null && _planner.IsStandable(grid, tile, gridComp))
                at = tile;
        }

        if (at is not { } spot)
            return false;

        _transform.SetCoordinates(mob, new EntityCoordinates(grid, new Vector2(spot.X + 0.5f, spot.Y + 0.5f)));
        // They left a ship under way: none of its speed comes with them.
        if (TryComp<PhysicsComponent>(mob, out var physics))
            _physics.ResetDynamics(mob, physics);
        return true;
    }
}
