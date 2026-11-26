using System.Numerics;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Sector;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Sector;

/// <summary>
/// The Scion's signal. A Scion that lies where it was found is silent. Once it is moved, pulled, unwrenched or drawn on it
/// shows on every radar and the sector map, wider as its resonance climbs; the Choir go after whoever takes it from their
/// site, and pirates after the ship that carries it.
/// </summary>
public sealed partial class WFRemadeSignalSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFRemadeSiteSystem _sites = default!;

    /// <summary>Draw at which a Scion counts as drawn on hard.</summary>
    public const float DrawVisible = 0.25f;

    /// <summary>The range a signal shows at with no resonance, and what each point of resonance adds, in metres.</summary>
    public const float BaseRange = 500f;
    public const float RangePerResonance = 50f;

    /// <summary>How long the Choir chase a carrier before they give up, and how near they close.</summary>
    private const float ChoirChase = 900f;
    private const float ChoirRange = 600f;

    private const string HuntPrototype = "WFEncounterPirateBoarders";
    private const float HuntMin = 1500f;
    private const float HuntMax = 2500f;
    private const float HuntStationClearance = 3000f;

    private static readonly Color SignalColor = Color.FromHex("#d6336c");
    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);

    private TimeSpan _next;
    private readonly List<EntityUid> _unmarked = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFEncounterCollectMarkersEvent>(OnCollectMarkers);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + TickRate;
        _unmarked.Clear();
        var monoliths = EntityQueryEnumerator<WFMonolithComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith))
        {
            if (!monolith.Local && !HasComp<WFScionSignalComponent>(uid))
                _unmarked.Add(uid);
        }

        foreach (var uid in _unmarked)
        {
            EnsureComp<WFScionSignalComponent>(uid);
        }

        var signals = EntityQueryEnumerator<WFScionSignalComponent, WFMonolithComponent, TransformComponent>();
        while (signals.MoveNext(out var uid, out var signal, out var monolith, out var xform))
        {
            Tick((uid, signal), monolith, xform, now);
        }
    }

    private void Tick(Entity<WFScionSignalComponent> ent, WFMonolithComponent monolith, TransformComponent xform, TimeSpan now)
    {
        var signal = ent.Comp;
        signal.Home ??= xform.GridUid;
        if (!signal.Disturbed)
        {
            if (!IsDisturbed(ent, monolith, xform))
                return;

            signal.Disturbed = true;
            signal.NextHunt = now + TimeSpan.FromSeconds(_config.GetCVar(WFRemadeSectorCVars.HuntInterval) / 2f);
            Log.Info($"{ToPrettyString(ent)} has been disturbed and shows on every radar.");
        }

        // Carried off to another ship, which is where the Choir and the pirates come for it. A main station is safe.
        if (xform.GridUid is not { } grid || grid == signal.Home || _corruption.IsSanctuary(grid))
            return;

        if (signal.Hunted != grid)
            SendChoir(ent, grid);

        TryHunt(ent, grid, now);
    }

    private bool IsDisturbed(Entity<WFScionSignalComponent> ent, WFMonolithComponent monolith, TransformComponent xform)
    {
        if (monolith.Stage != WFMonolithStage.Quiet || monolith.Draw >= DrawVisible)
            return true;

        return xform.GridUid != ent.Comp.Home
               || ent.Comp.Wild && !xform.Anchored
               || TryComp<PullableComponent>(ent, out var pull) && pull.BeingPulled;
    }

    /// <summary>How far a signal reaches, in metres.</summary>
    public static float RangeFor(float resonance)
    {
        return BaseRange + RangePerResonance * Math.Clamp(resonance, 0f, WFMonolithMath.MaxResonance);
    }

    /// <summary>The threat rank a stage shows as on the map: chevrons for Hymn to Harvest, a star at Rapture, three at Crescendo.</summary>
    public static int RankFor(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Hymn => 1,
            WFMonolithStage.Growth => 2,
            WFMonolithStage.Harvest => 3,
            WFMonolithStage.Rapture => 4,
            WFMonolithStage.Crescendo => WFEncounterThreat.Max,
            _ => 0,
        };
    }

    private void OnCollectMarkers(ref WFEncounterCollectMarkersEvent args)
    {
        var query = EntityQueryEnumerator<WFScionSignalComponent, WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var signal, out var monolith, out var xform))
        {
            if (!signal.Disturbed || xform.MapID == MapId.Nullspace)
                continue;

            var position = _transform.GetMapCoordinates(uid, xform);
            var grid = xform.GridUid;
            var net = GetNetEntity(uid);
            var rank = RankFor(monolith.Stage);
            args.Markers.Add(new WFEncounterMarker
            {
                Encounter = net,
                Name = Loc.GetString("wf-remade-signal-name", ("stage", Loc.GetString($"wf-remade-signal-stage-{monolith.Stage}"))),
                Ship = grid is { } ship ? Name(ship) : Loc.GetString("wf-remade-signal-adrift"),
                Category = WFEncounterCategory.Threat,
                Icon = WFEncounterIcon.Category,
                Grid = grid is { } carrier ? GetNetEntity(carrier) : NetEntity.Invalid,
                Color = SignalColor,
                Map = position.MapId,
                Position = position.Position,
                Velocity = CompOrNull<PhysicsComponent>(grid)?.LinearVelocity ?? Vector2.Zero,
                WarnRange = RangeFor(monolith.Resonance),
                Threat = rank > 0 ? rank : null,
            });
        }
    }

    /// <summary>The Choir of the Scion's site, if it has them, set on the ship that has it.</summary>
    private void SendChoir(Entity<WFScionSignalComponent> ent, EntityUid carrier)
    {
        ent.Comp.Hunted = carrier;
        if (ent.Comp.Encounter is not { } uid || !TryComp<WFRemadeSiteComponent>(uid, out var site)
            || !TryComp<WFEncounterComponent>(uid, out var encounter) || encounter.Resolution != null
            || !_proto.TryIndex(site.Site, out var prototype) || prototype.Choir is not { } key)
            return;

        var queue = new List<WFCrewObjective>
        {
            new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(carrier), Range = ChoirRange, Duration = ChoirChase },
        };
        if (_encounters.SetOrders((uid, encounter), key, queue))
            Log.Info($"The Choir of {ToPrettyString(uid)} are after {ToPrettyString(carrier)}.");
    }

    /// <summary>Pirates come for a ship with a Scion and people aboard, now and then, one band at a time.</summary>
    private void TryHunt(Entity<WFScionSignalComponent> ent, EntityUid carrier, TimeSpan now)
    {
        var interval = _config.GetCVar(WFRemadeSectorCVars.HuntInterval);
        if (interval <= 0f || !_sites.Enabled || now < ent.Comp.NextHunt)
            return;

        ent.Comp.NextHunt = now + TimeSpan.FromSeconds(interval);
        if (!_random.Prob(_config.GetCVar(WFRemadeSectorCVars.HuntChance)) || !PlayersAboard(carrier)
            || !_proto.TryIndex<WFEncounterPrototype>(HuntPrototype, out var hunt) || _encounters.IsRunning(HuntPrototype))
            return;

        var here = _transform.GetMapCoordinates(carrier);
        for (var i = 0; i < 8; i++)
        {
            var point = here.Position + _random.NextAngle().ToVec() * _random.NextFloat(HuntMin, HuntMax);
            if (!_scheduler.IsClearSpace(here.MapId, point, 600f, HuntStationClearance))
                continue;

            if (_encounters.TrySpawn(hunt, new MapCoordinates(point, here.MapId), out _, offBudget: true))
                Log.Info($"Pirates sent after {ToPrettyString(carrier)} for the Scion {ToPrettyString(ent)}.");

            return;
        }
    }

    private bool PlayersAboard(EntityUid grid)
    {
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                return true;
        }

        return false;
    }
}
