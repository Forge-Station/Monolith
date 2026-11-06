using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Lattice;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The Coda is not a place to linger. A crewed ship out there is found by Remade hunters the longer it stays, has its
/// structure eaten unless a stabiliser runs, and has its thrusters, gyroscopes and power give out now and then.
/// </summary>
public sealed class WFCodaHazardSystem : EntitySystem
{
    [Dependency] private ApcSystem _apc = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrusterSystem _thrusters = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFLatticeSystem _lattice = default!;

    private static readonly ProtoId<DamageTypePrototype> Structural = "Structural";
    private static readonly SoundSpecifier BoardSound = new SoundCollectionSpecifier("WFDreadMenace");
    private static readonly SoundSpecifier GroanSound = new SoundCollectionSpecifier("MetalBreak");

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);
    private const float HunterRollSeconds = 60f;
    private const float RotSeconds = 20f;
    private const float FailureRollSeconds = 40f;

    /// <summary>Hunters keep this far from the crew when they board, in metres.</summary>
    private const float BoardDistance = 6f;

    private readonly Dictionary<EntityUid, Ship> _ships = new();
    private readonly List<(EntityUid Uid, TimeSpan Until, Failure Kind)> _failed = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _crew = new();
    private TimeSpan _next;

    private enum Failure : byte
    {
        Thruster,
        Breaker,
    }

    /// <summary>One crewed ship's time in the Coda.</summary>
    private sealed class Ship
    {
        public float Exposure;
        public float HunterClock;
        public float RotClock;
        public float FailureClock;
        public TimeSpan HuntersAfter;
        public readonly List<EntityUid> Hunters = new();
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ =>
        {
            _ships.Clear();
            _failed.Clear();
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        RestoreFailures(now);
        if (now < _next)
            return;

        _next = now + Tick;
        var seconds = (float) Tick.TotalSeconds;
        GatherCrew();

        foreach (var grid in _ships.Keys.ToList())
        {
            if (!_crew.ContainsKey(grid))
                _ships.Remove(grid);
        }

        foreach (var (grid, crew) in _crew)
        {
            var point = _transform.GetMapCoordinates(grid);
            if (_lattice.BandOf(point, out var profile) == null || profile == null
                || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var (band, depth) = _lattice.Locate(Transform(grid).MapUid!.Value, point.Position);
            if (band < profile.HazardBand)
            {
                _ships.Remove(grid);
                continue;
            }

            if (!_ships.TryGetValue(grid, out var ship))
            {
                ship = new Ship { HuntersAfter = now };
                _ships[grid] = ship;
            }

            ship.Exposure += seconds;
            var sheltered = TryComp<WFLatticeShelterComponent>(grid, out var shelter) && shelter.StopsTaking;

            ship.HunterClock += seconds;
            if (ship.HunterClock >= HunterRollSeconds)
            {
                ship.HunterClock = 0f;
                RollHunters((grid, gridComp), ship, crew, depth, profile, now);
            }

            ship.RotClock += seconds;
            if (ship.RotClock >= RotSeconds)
            {
                ship.RotClock = 0f;
                if (!sheltered)
                    Rot((grid, gridComp), crew, depth, profile);
            }

            ship.FailureClock += seconds;
            if (ship.FailureClock >= FailureRollSeconds)
            {
                ship.FailureClock = 0f;
                RollFailure(grid, crew, profile, now);
            }
        }
    }

    /// <summary>Puts a hunter pack aboard a ship now, away from whoever is on it. Returns how many boarded.</summary>
    public int Board(Entity<MapGridComponent> grid, int count, WFLatticeProfilePrototype profile)
    {
        var crew = _crew.TryGetValue(grid, out var list) ? list : new List<EntityUid>();
        if (profile.HunterMobs.Count == 0 || !TryFindBoarding(grid, crew, out var at))
            return 0;

        for (var i = 0; i < count; i++)
        {
            Spawn(_random.Pick(profile.HunterMobs), at);
        }

        return count;
    }

    /// <summary>One rot tick on a ship, at the given depth into the band.</summary>
    public void RotNow(Entity<MapGridComponent> grid, float depth, WFLatticeProfilePrototype profile)
    {
        Rot(grid, new List<EntityUid>(), depth, profile);
    }

    /// <summary>Makes one kind of system give out on a ship: 0 thrusters, 1 gyroscopes, 2 power.</summary>
    public bool FailNow(EntityUid grid, int kind, float seconds)
    {
        var until = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        return kind switch
        {
            0 => FailThrusters(grid, ThrusterType.Linear, until),
            1 => FailThrusters(grid, ThrusterType.Angular, until),
            _ => FailPower(grid, until),
        };
    }

    /// <summary>Which ships have living players aboard, and who.</summary>
    private void GatherCrew()
    {
        _crew.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } mob
                || HasComp<GhostComponent>(mob)
                || HasComp<WFChoirMemberComponent>(mob)
                || _mobState.IsDead(mob)
                || Transform(mob).GridUid is not { } grid
                || HasComp<MapComponent>(grid)
                || !HasComp<ShuttleComponent>(grid))
                continue;

            if (!_crew.TryGetValue(grid, out var list))
            {
                list = new List<EntityUid>();
                _crew[grid] = list;
            }

            list.Add(mob);
        }
    }

    /// <summary>The longer a ship stays, the likelier a pack of Remade comes aboard somewhere away from the crew.</summary>
    private void RollHunters(Entity<MapGridComponent> grid, Ship ship, List<EntityUid> crew, float depth,
        WFLatticeProfilePrototype profile, TimeSpan now)
    {
        ship.Hunters.RemoveAll(uid => TerminatingOrDeleted(uid) || _mobState.IsDead(uid));
        if (profile.HunterMobs.Count == 0 || now < ship.HuntersAfter || ship.Hunters.Count >= profile.HunterCap)
            return;

        var chance = Math.Clamp((ship.Exposure - profile.HunterGrace) / profile.HunterRamp, 0f, 1f) * profile.HunterMaxChance;
        if (!_random.Prob(chance))
            return;

        var size = (int) MathF.Round(MathHelper.Lerp(profile.HunterPack.X, profile.HunterPack.Y, depth));
        size = Math.Min(size, profile.HunterCap - ship.Hunters.Count);
        if (size <= 0 || !TryFindBoarding(grid, crew, out var at))
            return;

        for (var i = 0; i < size; i++)
        {
            ship.Hunters.Add(Spawn(_random.Pick(profile.HunterMobs), at));
        }

        ship.HuntersAfter = now + TimeSpan.FromSeconds(profile.HunterCooldown);
        _audio.PlayPvs(BoardSound, at);
        foreach (var mob in crew)
        {
            _popup.PopupEntity(Loc.GetString("wf-coda-hunters"), mob, mob, PopupType.LargeCaution);
        }
    }

    /// <summary>An open floor tile well away from the crew.</summary>
    private bool TryFindBoarding(Entity<MapGridComponent> grid, List<EntityUid> crew, out EntityCoordinates at)
    {
        at = default;
        var tiles = _maps.GetAllTiles(grid.Owner, grid.Comp).ToList();
        for (var attempt = 0; attempt < 40 && tiles.Count > 0; attempt++)
        {
            var tile = _random.Pick(tiles);
            if (tile.Tile.IsEmpty || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                continue;

            var coords = _maps.GridTileToLocal(grid.Owner, grid.Comp, tile.GridIndices);
            if (crew.Any(mob => _transform.InRange(Transform(mob).Coordinates, coords, BoardDistance)))
                continue;

            at = coords;
            return true;
        }

        return false;
    }

    /// <summary>The Unmooring eats a few of the ship's structures: walls, machines, anything bolted down.</summary>
    private void Rot(Entity<MapGridComponent> grid, List<EntityUid> crew, float depth, WFLatticeProfilePrototype profile)
    {
        var tiles = _maps.GetAllTiles(grid.Owner, grid.Comp).ToList();
        if (tiles.Count == 0)
            return;

        var targets = (int) MathF.Round(MathHelper.Lerp(profile.RotTargets.X, profile.RotTargets.Y, depth));
        var damage = new DamageSpecifier(_proto.Index(Structural), profile.RotDamage);
        var hit = 0;
        for (var attempt = 0; attempt < targets * 6 && hit < targets; attempt++)
        {
            var tile = _random.Pick(tiles);
            var anchored = _maps.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile.GridIndices);
            while (anchored.MoveNext(out var uid))
            {
                if (HasComp<MobStateComponent>(uid) || HasComp<WFLatticeAnchorComponent>(uid) || !HasComp<DamageableComponent>(uid))
                    continue;

                _damage.TryChangeDamage(uid.Value, damage, ignoreResistances: true);
                hit++;
                break;
            }
        }

        if (hit > 0 && crew.Count > 0 && _random.Prob(0.25f))
        {
            var mob = _random.Pick(crew);
            _audio.PlayPvs(GroanSound, mob, AudioParams.Default.WithVolume(-6f));
            _popup.PopupEntity(Loc.GetString("wf-coda-rot"), mob, mob, PopupType.MediumCaution);
        }
    }

    /// <summary>Now and then something aboard gives out: thrusters, gyroscopes or the power.</summary>
    private void RollFailure(EntityUid grid, List<EntityUid> crew, WFLatticeProfilePrototype profile, TimeSpan now)
    {
        if (!_random.Prob(profile.FailureChance))
            return;

        var until = now + TimeSpan.FromSeconds(_random.NextFloat(profile.FailureSeconds.X, profile.FailureSeconds.Y));
        var kind = _random.Next(3);
        var message = kind switch
        {
            0 => FailThrusters(grid, ThrusterType.Linear, until) ? "wf-coda-fail-thrusters" : null,
            1 => FailThrusters(grid, ThrusterType.Angular, until) ? "wf-coda-fail-gyros" : null,
            _ => FailPower(grid, until) ? "wf-coda-fail-power" : null,
        };

        if (message == null)
            return;

        foreach (var mob in crew)
        {
            _popup.PopupEntity(Loc.GetString(message), mob, mob, PopupType.MediumCaution);
        }
    }

    /// <summary>Cuts out about half the ship's thrusters of one type.</summary>
    private bool FailThrusters(EntityUid grid, ThrusterType type, TimeSpan until)
    {
        var any = false;
        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thruster, out var xform))
        {
            if (xform.GridUid != grid || thruster.Type != type || !thruster.IsOn || !_random.Prob(0.5f))
                continue;

            _thrusters.DisableThruster(uid, thruster, xform);
            _failed.Add((uid, until, Failure.Thruster));
            any = true;
        }

        return any;
    }

    /// <summary>Trips the ship's APC breakers.</summary>
    private bool FailPower(EntityUid grid, TimeSpan until)
    {
        var any = false;
        var query = EntityQueryEnumerator<ApcComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var apc, out var xform))
        {
            if (xform.GridUid != grid || !apc.MainBreakerEnabled)
                continue;

            _apc.ApcToggleBreaker(uid, apc);
            _failed.Add((uid, until, Failure.Breaker));
            any = true;
        }

        return any;
    }

    /// <summary>Brings back what gave out, once its time is up.</summary>
    private void RestoreFailures(TimeSpan now)
    {
        for (var i = _failed.Count - 1; i >= 0; i--)
        {
            var (uid, until, kind) = _failed[i];
            if (now < until)
                continue;

            _failed.RemoveAt(i);
            if (TerminatingOrDeleted(uid))
                continue;

            switch (kind)
            {
                case Failure.Thruster when TryComp<ThrusterComponent>(uid, out var thruster)
                                           && !thruster.IsOn && _thrusters.CanEnable(uid, thruster):
                    _thrusters.EnableThruster(uid, thruster);
                    break;
                case Failure.Breaker when TryComp<ApcComponent>(uid, out var apc) && !apc.MainBreakerEnabled:
                    _apc.ApcToggleBreaker(uid, apc);
                    break;
            }
        }
    }
}
