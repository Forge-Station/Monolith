using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.FtlEffects;
using Content.Server._WF.Planets.Bounds;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Stunnable;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets.Hop;

/// <summary>
/// Moves a hull and the grids docked to it into or out of planet orbit on its own clock, with no FTL component, drive or
/// cooldown: a warm-up on the old map, a stay on a private hold map while the crew's clients draw the planet approach,
/// then a landing inside the world's ring clear of other grids.
/// </summary>
public sealed partial class WFPlanetHopSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ThrusterSystem _thruster = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private StunSystem _stuns = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;
    [Dependency] private FtlDepartureSystem _departure = default!;
    [Dependency] private ShipShieldsSystem _shields = default!;

    /// <summary>Warm-up of a hop, in seconds.</summary>
    public const float StartupTime = 5f;

    /// <summary>Stay on the hold map, in seconds; the approach overlay runs over all of it.</summary>
    public const float TravelTime = 5f;

    private readonly List<EntityUid> _due = new();
    private readonly HashSet<EntityUid> _group = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        InitializeEffects();
        SubscribeLocalEvent<WFPlanetHopComponent, ComponentShutdown>(OnHopShutdown);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        _due.Clear();

        var hops = EntityQueryEnumerator<WFPlanetHopComponent>();

        while (hops.MoveNext(out var uid, out var hop))
        {
            if (hop.Hull != uid)
                continue;

            if (hop.Phase == WFPlanetHopPhase.Warm)
                KeepWarmConvoy(uid, hop);
            else
                Preload(uid, hop);

            if (now >= hop.PhaseEnd)
                _due.Add(uid);
        }

        foreach (var uid in _due)
        {
            if (!TryComp<WFPlanetHopComponent>(uid, out var hop))
                continue;

            if (hop.Phase == WFPlanetHopPhase.Warm)
                EnterTransit(uid, hop);
            else
                Land(uid, hop);
        }
    }

    /// <summary>True while a grid is warming up for a hop, waiting on the hold map or riding one.</summary>
    public bool IsHopping(EntityUid grid)
    {
        return HasComp<WFPlanetHopComponent>(grid);
    }

    /// <summary>True while a hull is hopping or in the middle of an FTL jump; a drive cooling down does not count.</summary>
    public bool IsBusy(EntityUid grid)
    {
        return IsHopping(grid)
               || TryComp<FTLComponent>(grid, out var ftl)
               && (ftl.State & (FTLState.Starting | FTLState.Travelling | FTLState.Arriving)) != 0;
    }

    /// <summary>True while a hull's hop is still warming up.</summary>
    public bool IsWarming(EntityUid grid)
    {
        return TryComp<WFPlanetHopComponent>(grid, out var hop) && hop.Phase == WFPlanetHopPhase.Warm;
    }

    /// <summary>
    /// Starts a hop of the hull and the grids docked to it onto another map, aimed at a spot in the world frame.
    /// Refuses for the physical reasons only; no drive, beacon, range or cooldown is asked.
    /// </summary>
    public bool TryStart(
        Entity<ShuttleComponent> hull,
        EntityUid targetMap,
        Vector2 worldPos,
        EntityUid? body,
        bool arriving,
        [NotNullWhen(false)] out string? reason)
    {
        reason = null;

        if (!HasComp<MapComponent>(targetMap) || HasComp<WFPlanetHopMapComponent>(targetMap))
        {
            reason = Loc.GetString("wf-orbit-refused");
            return false;
        }

        _group.Clear();
        _shuttle.GetAllDockedShuttles(hull.Owner, _group);
        _group.Add(hull.Owner);

        if (!CanHop(_group, out reason))
            return false;

        var group = new HashSet<EntityUid>(_group);
        UndockOutsiders(group);

        var angle = _transform.GetWorldRotation(hull.Owner);
        var riders = Offsets(hull.Owner, group);
        var origin = _transform.GetWorldPosition(hull.Owner);
        var now = _timing.CurTime;

        var hop = AddComp<WFPlanetHopComponent>(hull.Owner);
        hop.Hull = hull.Owner;
        hop.Phase = WFPlanetHopPhase.Warm;
        hop.Started = now;
        hop.PhaseEnd = now + TimeSpan.FromSeconds(StartupTime);
        hop.Body = body;
        hop.Arriving = arriving;
        hop.TargetMap = targetMap;
        hop.Angle = angle;
        hop.OriginMap = Transform(hull.Owner).MapUid ?? EntityUid.Invalid;
        hop.Origin = origin;
        hop.Target = FindSpot(hull.Owner, riders, targetMap, worldPos, angle);

        BeginWarmEffects(hull, hop);
        MarkApproach(hull.Owner, hop, origin);

        _console.RefreshShuttleConsoles(hull.Owner);
        return true;
    }

    /// <summary>Why a hull and the grids docked to it cannot hop now; each is something physical about the ships.</summary>
    public bool CanHop(IEnumerable<EntityUid> grids, [NotNullWhen(false)] out string? reason)
    {
        foreach (var grid in grids)
        {
            if (IsHopping(grid))
            {
                reason = Loc.GetString("wf-orbit-hopping");
                return false;
            }

            if (TryComp<FTLComponent>(grid, out var ftl)
                && (ftl.State & (FTLState.Starting | FTLState.Travelling | FTLState.Arriving)) != 0)
            {
                reason = Loc.GetString("wf-orbit-in-ftl");
                return false;
            }

            if (HasComp<PreventPilotComponent>(grid))
            {
                reason = Loc.GetString("wf-orbit-protected");
                return false;
            }

            if (HasComp<ForceAnchorComponent>(grid))
            {
                reason = Loc.GetString("wf-orbit-anchored");
                return false;
            }

            if (Transform(grid).MapUid is { } map && HasComp<CEZTransitMapComponent>(map))
            {
                reason = Loc.GetString("wf-shuttle-console-in-transit");
                return false;
            }

            var attempt = new WFPlanetHopAttemptEvent(grid, false, string.Empty);
            RaiseLocalEvent(ref attempt);

            if (attempt.Cancelled)
            {
                reason = attempt.Reason;
                return false;
            }
        }

        reason = null;
        return true;
    }

    /// <summary>Undocks every port that joins the hop's grids to something that is not riding along.</summary>
    private void UndockOutsiders(HashSet<EntityUid> group)
    {
        foreach (var grid in group)
        {
            foreach (var dock in _docking.GetDocks(grid))
            {
                if (!dock.Comp.Docked || dock.Comp.DockedWith is not { } other)
                    continue;

                var connected = _transform.GetParentUid(other);

                if (connected == EntityUid.Invalid || !group.Contains(connected))
                    _docking.Undock(dock);
            }
        }
    }

    /// <summary>The pose of each docked grid relative to the hull, so the convoy keeps its shape across maps.</summary>
    private List<WFHopRider> Offsets(EntityUid hull, HashSet<EntityUid> group)
    {
        var riders = new List<WFHopRider>();
        var mainPos = _transform.GetWorldPosition(hull);
        var mainRot = _transform.GetWorldRotation(hull);

        foreach (var grid in group)
        {
            if (grid == hull)
                continue;

            var offset = (-mainRot).RotateVec(_transform.GetWorldPosition(grid) - mainPos);
            riders.Add(new WFHopRider(grid, offset, _transform.GetWorldRotation(grid) - mainRot));
        }

        return riders;
    }

    /// <summary>A hop that outlived its hull releases the grids that rode with it and the hold map.</summary>
    private void OnHopShutdown(Entity<WFPlanetHopComponent> ent, ref ComponentShutdown args)
    {
        if (MetaData(ent).EntityLifeStage < EntityLifeStage.Terminating)
            return;

        if (ent.Comp.Hull != ent.Owner)
        {
            if (TryComp<WFPlanetHopComponent>(ent.Comp.Hull, out var hull))
                hull.Riders.RemoveAll(rider => rider.Grid == ent.Owner);

            return;
        }

        Abort(ent.Owner, ent.Comp);
    }
}
