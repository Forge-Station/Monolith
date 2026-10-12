using System.Numerics;
using Content.Server.Administration.Commands;
using Content.Server.Power.Components;
using Content.Shared._WF.Hailing;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Hailing;

/// <summary>
/// The far end for trying hailing alone: a small ship whose pilot hails on command, answers any hail and
/// sends back whatever is said to them. It removes itself after a few idle minutes.
/// </summary>
public sealed class HailTestSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedGodmodeSystem _godmode = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private HailingSystem _hailing = default!;

    private static readonly EntProtoId ConsolePrototype = "ComputerShuttle";
    private static readonly EntProtoId OperatorPrototype = "MobHuman";
    private static readonly ProtoId<StartingGearPrototype> OperatorGear = "CaptainGear";
    private static readonly ProtoId<ContentTileDefinition> Floor = "Plating";

    /// <summary>Metres of open space kept between the test ship and the hull it hails.</summary>
    private const float Distance = 250f;

    /// <summary>Side of the box that must be free of other grids where the test ship appears.</summary>
    private const float Clearance = 40f;

    private const int Size = 3;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>The power update is throttled, so a new console takes a moment to count as powered.</summary>
    private static readonly TimeSpan PowerUpDelay = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan HailPatience = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan AnswerDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReplyDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);

    private TimeSpan _nextUpdate;
    private List<Entity<MapGridComponent>> _grids = new();

    /// <summary>
    /// Has a test ship hail the given ship, bringing one in nearby if none is around. Returns the test ship.
    /// </summary>
    public EntityUid Hail(EntityUid target)
    {
        var now = _timing.CurTime;
        var map = Transform(target).MapID;
        var position = _transform.GetWorldPosition(target);
        var reach = Reach(target);

        var query = EntityQueryEnumerator<HailTestShipComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var existing, out var xform))
        {
            if (!existing.Console.IsValid())
                continue;

            // One left behind by an earlier test is no use from across the sector.
            if (xform.MapID != map || (_transform.GetWorldPosition(xform) - position).Length() > reach * 2f)
            {
                QueueDel(uid);
                continue;
            }

            _hailing.End(existing.Console);
            Aim(existing, target, now);
            return uid;
        }

        var ship = Build(map, position, reach);
        Aim(ship.Comp, target, now + PowerUpDelay);
        return ship;
    }

    /// <summary>
    /// Removes every test ship. Returns how many there were.
    /// </summary>
    public int Clear()
    {
        var count = 0;
        var query = EntityQueryEnumerator<HailTestShipComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
            count++;
        }

        return count;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;

        var query = EntityQueryEnumerator<HailTestShipComponent>();

        while (query.MoveNext(out var uid, out var ship))
        {
            if (!ship.Console.IsValid())
                continue;

            if (TerminatingOrDeleted(ship.Console) || TerminatingOrDeleted(ship.Operator))
            {
                QueueDel(uid);
                continue;
            }

            // A call only lasts while its operator has the console open, and this one never leaves the helm.
            _ui.OpenUi(ship.Console, ShuttleConsoleUiKey.Key, ship.Operator);

            if (!TryComp<HailConsoleComponent>(ship.Console, out var hail))
                Idle(uid, ship, now);
            else
                OnCall(ship, hail, now);
        }
    }

    private void Idle(EntityUid uid, HailTestShipComponent ship, TimeSpan now)
    {
        ship.Greeted = false;
        ship.NextLine = 0;
        ship.ReplyAt = null;

        if (ship.Target is not { } target)
        {
            if (now >= ship.IdleSince + Lifetime)
                QueueDel(uid);

            return;
        }

        ship.IdleSince = now;

        if (now < ship.HailAt)
            return;

        if (_hailing.TryHail(ship.Console, ship.Operator, target) || now >= ship.GiveUpAt)
            ship.Target = null;
    }

    private void OnCall(HailTestShipComponent ship, HailConsoleComponent hail, TimeSpan now)
    {
        ship.IdleSince = now;

        switch (hail.State)
        {
            case HailState.Ringing:
                if (now >= hail.StateTime + AnswerDelay)
                    _hailing.Answer(ship.Console, ship.Operator, true);

                break;

            case HailState.Connected:
                if (!ship.Greeted)
                {
                    ship.Greeted = true;
                    _hailing.Say(ship.Console, ship.Operator, Loc.GetString("hail-test-greeting"));
                }

                foreach (var line in hail.Log)
                {
                    if (line.Sequence < ship.NextLine)
                        continue;

                    ship.NextLine = line.Sequence + 1;

                    if (line.Own)
                        continue;

                    ship.Heard = line.Text;
                    ship.ReplyAt = now + ReplyDelay;
                }

                if (ship.ReplyAt is { } at && now >= at)
                {
                    ship.ReplyAt = null;
                    _hailing.Say(ship.Console, ship.Operator, Loc.GetString("hail-test-reply", ("text", ship.Heard)));
                }

                break;
        }
    }

    private void Aim(HailTestShipComponent ship, EntityUid target, TimeSpan at)
    {
        ship.Target = target;
        ship.HailAt = at;
        ship.GiveUpAt = at + HailPatience;
        ship.IdleSince = at;

        // A tester wants every run to ring and page, not the waits a real caller gets.
        _hailing.ClearCooldowns(ship.Console, target);
    }

    /// <summary>
    /// How far from a ship's origin a test ship sits: clear of its hull, well inside hailing range.
    /// </summary>
    private float Reach(EntityUid target)
    {
        var hull = TryComp<MapGridComponent>(target, out var grid) ? grid.LocalAABB.Size.Length() : 0f;
        return hull + Distance;
    }

    private Entity<HailTestShipComponent> Build(MapId map, Vector2 around, float reach)
    {
        var position = around;

        for (var i = 0; i < 8; i++)
        {
            position = around + _random.NextAngle().ToVec() * reach;
            _grids.Clear();
            _mapManager.FindGridsIntersecting(map, Box2.CenteredAround(position, new Vector2(Clearance)), ref _grids, false, false);

            if (_grids.Count == 0)
                break;
        }

        var grid = _mapManager.CreateGridEntity(map);
        var tile = new Tile(_tiles[Floor].TileId);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                tiles.Add((new Vector2i(x, y), tile));
            }
        }

        _map.SetTiles(grid.Owner, grid.Comp, tiles);
        _transform.SetWorldPosition(grid.Owner, position);
        _meta.SetEntityName(grid, Loc.GetString("hail-test-ship-name"));

        var console = Spawn(ConsolePrototype, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));

        if (TryComp<ApcPowerReceiverComponent>(console, out var power))
            power.NeedsPower = false;

        var pilot = Spawn(OperatorPrototype, new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
        var profile = HumanoidCharacterProfile.RandomWithSpecies();
        _humanoid.LoadProfile(pilot, profile);
        _meta.SetEntityName(pilot, profile.Name);
        SetOutfitCommand.SetOutfit(pilot, OperatorGear, EntityManager);

        // The ship is bare plating in vacuum.
        _godmode.EnableGodmode(pilot);

        var ship = AddComp<HailTestShipComponent>(grid);
        ship.Console = console;
        ship.Operator = pilot;
        ship.IdleSince = _timing.CurTime;
        return (grid.Owner, ship);
    }
}
