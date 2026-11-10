using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Lattice;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.Remade.Sector;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Sector;

/// <summary>
/// Puts Scions into the sector: a discovery site is a hidden hull with a dormant Scion aboard, an adrift site a freighter
/// deep in an outbreak. Sites are ordinary encounters; this puts the Scion aboard once the hull is real, keeps the count
/// of wild Scions to one and holds off the next after one is gone.
/// </summary>
public sealed partial class WFRemadeSiteSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private WFLatticeSystem _lattice = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;

    /// <summary>Shortest gap between wild Scions, and the wait after one is gone, where no rule sets its own.</summary>
    public const float DefaultMinGap = 2700f;
    public const float DefaultGoneCooldown = 1800f;

    /// <summary>How far a hint's position is thrown off, and the grid it is rounded to, in metres.</summary>
    private const float HintJitter = 900f;
    private const float HintRound = 1000f;

    private const float PlaceClearance = 600f;
    private const int PlaceTries = 16;

    private TimeSpan? _lastStarted;
    private TimeSpan? _lastGone;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEncounterStartedEvent>(OnStarted);
        SubscribeLocalEvent<WFEncounterShipSpawnedEvent>(OnShipSpawned);
        SubscribeLocalEvent<WFEncounterWeightEvent>(OnWeight);
        SubscribeLocalEvent<WFScionSignalComponent, EntityTerminatingEvent>(OnSignalTerminating);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _lastStarted = null;
            _lastGone = null;
        });
    }

    private void OnStarted(ref WFEncounterStartedEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter) || FindSite(encounter.Prototype.Id) is not { } site)
            return;

        var comp = EnsureComp<WFRemadeSiteComponent>(args.Encounter);
        comp.Site = site.ID;
        _lastStarted = _timing.CurTime;
        TryPopulate((args.Encounter, comp, encounter));
    }

    /// <summary>A simulated hull has just been built: the Scion goes aboard now that it is real.</summary>
    private void OnShipSpawned(ref WFEncounterShipSpawnedEvent args)
    {
        if (TryComp<WFRemadeSiteComponent>(args.Encounter, out var comp) && TryComp<WFEncounterComponent>(args.Encounter, out var encounter))
            TryPopulate((args.Encounter, comp, encounter));
    }

    /// <summary>The scheduler may not start a site while one Scion is already out there.</summary>
    private void OnWeight(ref WFEncounterWeightEvent args)
    {
        if (FindSite(args.Prototype.ID) == null)
            return;

        if (!Enabled || !CanStartWild(DefaultMinGap, DefaultGoneCooldown, out _))
            args.Weight = 0f;
    }

    private void OnSignalTerminating(Entity<WFScionSignalComponent> ent, ref EntityTerminatingEvent args)
    {
        _lastGone = _timing.CurTime;
    }

    /// <summary>Whether the sector places Scions and sends pirates after them: encounters are on and so is the Remade's own switch.</summary>
    public bool Enabled => _config.GetCVar(EncountersCVars.Enabled) && _config.GetCVar(WFRemadeSectorCVars.Enabled);

    private WFRemadeSitePrototype? FindSite(string encounter)
    {
        return _proto.EnumeratePrototypes<WFRemadeSitePrototype>().FirstOrDefault(site => site.Encounter.Id == encounter);
    }

    private void TryPopulate(Entity<WFRemadeSiteComponent, WFEncounterComponent> ent)
    {
        if (ent.Comp1.Populated || !_proto.TryIndex(ent.Comp1.Site, out var site))
            return;

        if (!ent.Comp2.Ships.TryGetValue(site.Ship, out var ship) || ship.Virtual || ship.Building || TerminatingOrDeleted(ship.Grid))
            return;

        ent.Comp1.Populated = true;
        // The hull is kept real from here: sent back to simulation it would be rebuilt without its Scion.
        ship.Touched = true;
        ent.Comp1.Scion = PlaceScion(ent, site, ship.Grid);
    }

    /// <summary>Lays the site's Scion on the hull's deck and wrenches it down, as a crew would find it.</summary>
    private EntityUid? PlaceScion(EntityUid encounter, WFRemadeSitePrototype site, EntityUid grid)
    {
        var tiles = _planner.HoldTiles(grid, int.MaxValue);
        if (tiles.Count == 0)
        {
            Log.Error($"Site {site.ID} has no free deck to put its Scion on.");
            return null;
        }

        var uid = EntityManager.CreateEntityUninitialized(site.Scion, tiles[tiles.Count / 4]);
        if (TryComp<WFMonolithComponent>(uid, out var monolith))
            monolith.Resonance = site.Resonance;

        var signal = EnsureComp<WFScionSignalComponent>(uid);
        signal.Wild = true;
        signal.Home = grid;
        signal.Encounter = encounter;
        EntityManager.InitializeAndStartEntity(uid);
        _transform.AnchorEntity(uid);
        return uid;
    }

    /// <summary>The Scions the sector holds.</summary>
    public int ScionCount()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFMonolithComponent>();
        while (query.MoveNext(out _, out var monolith))
        {
            if (!monolith.Local)
                count++;
        }

        return count;
    }

    /// <summary>Whether a Scion a site placed is still on the hull it was put on.</summary>
    public bool IsWild(EntityUid uid, WFScionSignalComponent? signal = null)
    {
        return Resolve(uid, ref signal, false) && signal.Wild && signal.Home != null && Transform(uid).GridUid == signal.Home;
    }

    /// <summary>Wild Scions: sites whose hull is not real yet, and Scions still lying on the hull a site put them on.</summary>
    public int WildCount()
    {
        var count = 0;
        var sites = EntityQueryEnumerator<WFRemadeSiteComponent, WFEncounterComponent>();
        while (sites.MoveNext(out _, out var site, out var encounter))
        {
            if (!site.Populated && encounter.Resolution == null)
                count++;
        }

        var signals = EntityQueryEnumerator<WFScionSignalComponent>();
        while (signals.MoveNext(out var uid, out var signal))
        {
            if (IsWild(uid, signal))
                count++;
        }

        return count;
    }

    /// <summary>Whether a new wild Scion may be placed: none out there, none gone lately, none placed lately and room in the sector.</summary>
    public bool CanStartWild(float minGap, float goneCooldown, out string reason)
    {
        var now = _timing.CurTime;
        if (WildCount() > 0)
            reason = Loc.GetString("wf-remade-sector-reason-wild");
        else if (ScionCount() >= _config.GetCVar(WFRemadeSectorCVars.MaxScions))
            reason = Loc.GetString("wf-remade-sector-reason-limit");
        else if (_lastStarted is { } started && now - started < TimeSpan.FromSeconds(minGap))
            reason = Loc.GetString("wf-remade-sector-reason-placed");
        else if (_lastGone is { } gone && now - gone < TimeSpan.FromSeconds(goneCooldown))
            reason = Loc.GetString("wf-remade-sector-reason-gone");
        else
        {
            reason = string.Empty;
            return true;
        }

        return false;
    }

    /// <summary>A weighted pick among the discovery sites.</summary>
    public WFRemadeSitePrototype? PickDiscovery()
    {
        var sites = _proto.EnumeratePrototypes<WFRemadeSitePrototype>()
            .Where(site => site.Kind == WFRemadeSiteKind.Discovery && site.Weight > 0f)
            .ToList();
        if (sites.Count == 0)
            return null;

        var roll = _random.NextFloat(sites.Sum(site => site.Weight));
        foreach (var site in sites)
        {
            roll -= site.Weight;
            if (roll <= 0f)
                return site;
        }

        return sites[^1];
    }

    /// <summary>
    /// Starts a site's encounter, at a point if one is given, else out in clear space a few kilometres from a player. A
    /// discovery site announces itself on the radio, in garbled hints.
    /// </summary>
    public bool TryStartSite(WFRemadeSitePrototype site, MapCoordinates? at, bool announce, out EntityUid encounter, EntityUid? spawner = null)
    {
        encounter = default;
        var prototype = _proto.Index(site.Encounter);
        MapCoordinates origin;
        if (at is { } given)
            origin = given;
        else if (!TryPlace(prototype, out origin))
            return false;

        if (!_encounters.TrySpawn(prototype, origin, out encounter, spawner, offBudget: true))
            return false;

        if (announce && site.Kind == WFRemadeSiteKind.Discovery)
            Hint(origin);

        return true;
    }

    /// <summary>A point in clear sector space at the encounter's distance from a random living player.</summary>
    private bool TryPlace(WFEncounterPrototype prototype, out MapCoordinates origin)
    {
        origin = MapCoordinates.Nullspace;
        var anchors = new List<MapCoordinates>();
        // A band-bound encounter measures from the Lattice's centre; with no Lattice, from players as usual.
        if (prototype.LatticeBand > 0)
            _lattice.GetCentres(anchors);

        var fromCentre = anchors.Count > 0;
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (!fromCentre && players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != MapId.Nullspace && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                anchors.Add(_transform.GetMapCoordinates(uid, xform));
        }

        for (var i = 0; anchors.Count > 0 && i < PlaceTries; i++)
        {
            var anchor = _random.Pick(anchors);
            var distance = _random.NextFloat(prototype.MinDistance, MathF.Max(prototype.MinDistance, prototype.MaxDistance));
            var point = anchor.Position + _random.NextAngle().ToVec() * distance;
            // Only sector space counts: a player on a planet, in a vehicle or in hyperspace is no place to measure from.
            if (!_scheduler.IsClearSpace(anchor.MapId, point, PlaceClearance, prototype.StationClearance)
                || fromCentre && _lattice.Locate(_maps.GetMapOrInvalid(anchor.MapId), point).Band < prototype.LatticeBand)
                continue;

            origin = new MapCoordinates(point, anchor.MapId);
            return true;
        }

        return false;
    }

    /// <summary>A garbled line on the common channel with a rough position, thrown off by up to a kilometre or so.</summary>
    private void Hint(MapCoordinates origin)
    {
        var map = _maps.GetMapOrInvalid(origin.MapId);
        var seed = _random.Next();
        // Out past the Lattice's hold the numbers come through broken, half as badly as names do.
        var x = _lattice.GarbleAt(Rough(origin.Position.X).ToString(), map, origin.Position, seed, 0.5f);
        var y = _lattice.GarbleAt(Rough(origin.Position.Y).ToString(), map, origin.Position, seed + 1, 0.5f);
        var text = Loc.GetString($"wf-remade-sector-hint-{_random.Next(1, 4)}", ("x", x), ("y", y));
        var voice = Spawn(null, new EntityCoordinates(_maps.GetMapOrInvalid(origin.MapId), origin.Position));
        _meta.SetEntityName(voice, Loc.GetString("wf-remade-sector-hint-sender"));
        EnsureComp<TelecomExemptComponent>(voice);
        _radio.SendRadioMessage(voice, text, "Common", voice);
        QueueDel(voice);
    }

    private int Rough(float value)
    {
        return (int) (MathF.Round((value + _random.NextFloat(-HintJitter, HintJitter)) / HintRound) * HintRound);
    }
}
