using System.Diagnostics.CodeAnalysis;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server._WF.Planets.Flight;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>Driveless shuttle-console hops into and out of planet orbit, and the console's orbit readout.</summary>
public sealed partial class WFOrbitEntrySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    // The countdown shows whole seconds.
    private const float DecaySecondsEpsilon = 0.5f;

    // Sector bodies with a live orbit layer, rebuilt each sweep.
    private readonly List<(EntityUid Body, EntityUid Orbit, float Range)> _bodies = new();

    private TimeSpan _nextRefresh;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFEnterPlanetOrbitMessage>(OnEnterOrbitMessage);
            subs.Event<WFLeavePlanetOrbitMessage>(OnLeaveOrbitMessage);
            subs.Event<WFEnterAtmosphereMessage>(OnEnterAtmosphereMessage);
            subs.Event<WFLiftoffMessage>(OnLiftoffMessage);
        });

        SubscribeLocalEvent<WFPlanetApproachComponent, FTLStartedEvent>(OnApproachStarted);
        SubscribeLocalEvent<WFPlanetApproachComponent, FTLCompletedEvent>(OnApproachCompleted);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        Refresh();
        SweepApproaches();
    }

    /// <summary>Recomputes every shuttle console's orbit readout.</summary>
    private void Refresh()
    {
        _bodies.Clear();

        if (_cfg.GetCVar(PlanetCVars.PlanetNetworks))
        {
            var bodies = AllEntityQuery<WFSectorPlanetComponent>();

            while (bodies.MoveNext(out var uid, out var comp))
            {
                if (comp.OrbitMap is not { } orbitNet
                    || !TryGetEntity(orbitNet, out var orbitUid)
                    || !TryComp<WFOrbitLayerComponent>(orbitUid, out var orbit))
                {
                    continue;
                }

                _bodies.Add((uid, orbitUid.Value, orbit.Range));
            }
        }

        var consoles = AllEntityQuery<ShuttleConsoleComponent, TransformComponent>();

        while (consoles.MoveNext(out var uid, out _, out var xform))
        {
            RefreshConsole(uid, xform);
        }
    }

    /// <summary>Writes one console's readout, removing the component when there is nothing to offer.</summary>
    private void RefreshConsole(EntityUid console, TransformComponent xform)
    {
        NetEntity? planet = null;
        var planetName = string.Empty;
        var inOrbit = false;
        var busy = false;
        var liftoffAvailable = false;
        var liftoffActive = false;
        var liftRatio = 0f;
        var atmospherePower = 0f;
        var powerDeficit = false;
        var decaySeconds = -1f;

        if (xform.GridUid is { } grid && HasComp<ShuttleComponent>(grid) && Transform(grid).MapUid is { } mapUid)
        {
            busy = HasComp<FTLComponent>(grid);
            liftoffAvailable = _zLevels.WfCanOfferLiftoff(grid);
            liftoffActive = HasComp<WFLiftoffComponent>(grid);

            if (TryComp<WFOrbitLayerComponent>(mapUid, out var orbit))
            {
                if (orbit.Planet is { } netPlanet
                    && TryGetEntity(netPlanet, out var body)
                    && HasComp<WFSectorPlanetComponent>(body))
                {
                    inOrbit = true;
                    planet = netPlanet;
                    planetName = Name(body.Value);

                    if (TryComp<WFOrbitDecayComponent>(grid, out var decay) && decay.Announced)
                        decaySeconds = MathF.Max(0f, (float) (decay.DecayAt - _timing.CurTime).TotalSeconds);
                }
            }
            else if ((liftoffAvailable || liftoffActive) && TryGetLiftoffPlanet(mapUid, out var liftoffPlanet))
            {
                planet = GetNetEntity(liftoffPlanet);
                planetName = Name(liftoffPlanet);
            }
            else if (TryGetNearestBody(grid, mapUid, out var nearest))
            {
                planet = GetNetEntity(nearest.Value);
                planetName = Name(nearest.Value);
            }

            if (inOrbit || liftoffAvailable || liftoffActive)
            {
                _zLevels.WfTryGetLiftRatio(grid, out liftRatio);
                _zLevels.WfGetAtmospherePower(grid, out atmospherePower, out powerDeficit);
            }
        }

        var unsanctioned = TryGetEntity(planet, out var planetUid)
            && TryComp<WFSectorPlanetComponent>(planetUid, out var planetSector)
            && !planetSector.Sanctioned;

        if (planet is null && !inOrbit && !liftoffAvailable && !liftoffActive)
        {
            RemCompDeferred<WFConsoleOrbitTargetComponent>(console);
            return;
        }

        var comp = EnsureComp<WFConsoleOrbitTargetComponent>(console);

        if (comp.Planet == planet
            && comp.PlanetName == planetName
            && comp.InOrbit == inOrbit
            && comp.Unsanctioned == unsanctioned
            && comp.Busy == busy
            && comp.LiftoffAvailable == liftoffAvailable
            && comp.LiftoffActive == liftoffActive
            && MathF.Abs(comp.AtmospherePowerDemand - atmospherePower) < 1f
            && comp.AtmospherePowerDeficit == powerDeficit
            && MathF.Abs(comp.LiftRatio - liftRatio) < LiftRatioEpsilon
            && MathF.Abs(comp.DecaySeconds - decaySeconds) < DecaySecondsEpsilon)
        {
            return;
        }

        comp.Planet = planet;
        comp.PlanetName = planetName;
        comp.InOrbit = inOrbit;
        comp.Unsanctioned = unsanctioned;
        comp.Busy = busy;
        comp.LiftoffAvailable = liftoffAvailable;
        comp.LiftoffActive = liftoffActive;
        comp.LiftRatio = liftRatio;
        comp.AtmospherePowerDemand = atmospherePower;
        comp.AtmospherePowerDeficit = powerDeficit;
        comp.DecaySeconds = decaySeconds;
        Dirty(console, comp);
    }

    /// <summary>The nearest sector body whose orbit range holds the hull; none on planet or transit maps.</summary>
    private bool TryGetNearestBody(EntityUid grid, EntityUid mapUid, [NotNullWhen(true)] out EntityUid? body)
    {
        body = null;

        if (HasComp<WFPlanetLayerComponent>(mapUid) || HasComp<CEZTransitMapComponent>(mapUid))
            return false;

        var gridPos = _transform.GetWorldPosition(grid);
        var best = float.MaxValue;

        foreach (var candidate in _bodies)
        {
            var bodyXform = Transform(candidate.Body);

            if (bodyXform.MapUid != mapUid)
                continue;

            var distance = (_transform.GetWorldPosition(bodyXform) - gridPos).LengthSquared();

            if (distance > candidate.Range * candidate.Range || distance >= best)
                continue;

            best = distance;
            body = candidate.Body;
        }

        return body != null;
    }

    /// <summary>Moves the console's hull onto a body's orbit layer at the same spot, re-checking every gate.</summary>
    public bool TryEnterOrbit(EntityUid console, EntityUid planetUid, [NotNullWhen(false)] out string? reason)
    {
        if (!TryGetHull(console, out var hull, out reason))
            return false;

        if (!TryComp<WFSectorPlanetComponent>(planetUid, out var sector)
            || sector.OrbitMap is not { } orbitNet
            || !TryGetEntity(orbitNet, out var orbitUid)
            || !TryComp<WFOrbitLayerComponent>(orbitUid, out var orbit))
        {
            reason = Loc.GetString("wf-orbit-no-network", ("planet", Name(planetUid)));
            return false;
        }

        var mapUid = Transform(hull.Value.Owner).MapUid;

        if (mapUid is null
            || mapUid != Transform(planetUid).MapUid
            || HasComp<WFPlanetLayerComponent>(mapUid.Value)
            || HasComp<CEZTransitMapComponent>(mapUid.Value))
        {
            reason = Loc.GetString("wf-orbit-not-in-sector", ("planet", Name(planetUid)));
            return false;
        }

        var worldPos = _transform.GetWorldPosition(hull.Value.Owner);

        if ((_transform.GetWorldPosition(planetUid) - worldPos).LengthSquared() > orbit.Range * orbit.Range)
        {
            reason = Loc.GetString("wf-orbit-out-of-range", ("planet", Name(planetUid)));
            return false;
        }

        if (!_shuttle.CanFTL(hull.Value.Owner, out reason))
            return false;

        if (!_shuttle.WfFTLToLayer(hull.Value, orbitUid.Value, worldPos))
        {
            reason = Loc.GetString("wf-orbit-refused");
            return false;
        }

        MarkApproach(hull.Value.Owner, planetUid, true);

        _nextRefresh = TimeSpan.Zero;
        return true;
    }

    /// <summary>Moves the console's hull from orbit back to the sector map at the same world spot.</summary>
    public bool TryLeaveOrbit(EntityUid console, [NotNullWhen(false)] out string? reason)
    {
        if (!TryGetHull(console, out var hull, out reason))
            return false;

        if (Transform(hull.Value.Owner).MapUid is not { } mapUid || !TryComp<WFOrbitLayerComponent>(mapUid, out var orbit))
        {
            reason = Loc.GetString("wf-orbit-not-in-orbit");
            return false;
        }

        if (orbit.Planet is not { } netPlanet
            || !TryGetEntity(netPlanet, out var body)
            || Transform(body.Value).MapUid is not { } sectorMap)
        {
            reason = Loc.GetString("wf-orbit-no-sector");
            return false;
        }

        if (!_shuttle.CanFTL(hull.Value.Owner, out reason))
            return false;

        if (!_shuttle.WfFTLToLayer(hull.Value, sectorMap, _transform.GetWorldPosition(hull.Value.Owner)))
        {
            reason = Loc.GetString("wf-orbit-refused");
            return false;
        }

        MarkApproach(hull.Value.Owner, body.Value, false);

        _nextRefresh = TimeSpan.Zero;
        return true;
    }

    /// <summary>The flyable hull a console sits on.</summary>
    private bool TryGetHull(EntityUid console, [NotNullWhen(true)] out Entity<ShuttleComponent>? hull, [NotNullWhen(false)] out string? reason)
    {
        hull = null;
        reason = null;

        if (Transform(console).GridUid is not { } grid || !TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            reason = Loc.GetString("wf-orbit-no-hull");
            return false;
        }

        hull = (grid, shuttle);
        return true;
    }

    /// <summary>Console request to enter orbit; the actor must pilot this console.</summary>
    private void OnEnterOrbitMessage(EntityUid uid, ShuttleConsoleComponent component, WFEnterPlanetOrbitMessage args)
    {
        if (GetEntity(args.Console) != uid || !IsPilot(args.Actor, uid) || !TryGetEntity(args.Planet, out var planet))
            return;

        // Unsanctioned worlds need a confirmed request.
        if (!args.Confirmed && TryComp<WFSectorPlanetComponent>(planet, out var sector) && !sector.Sanctioned)
        {
            _popup.PopupEntity(Loc.GetString("wf-orbit-unsanctioned-unconfirmed", ("planet", Name(planet.Value))), uid, args.Actor);
            return;
        }

        if (!TryEnterOrbit(uid, planet.Value, out var reason))
            _popup.PopupEntity(reason, uid, args.Actor);
    }

    /// <summary>Console request to leave orbit; the actor must pilot this console.</summary>
    private void OnLeaveOrbitMessage(EntityUid uid, ShuttleConsoleComponent component, WFLeavePlanetOrbitMessage args)
    {
        if (GetEntity(args.Console) != uid || !IsPilot(args.Actor, uid))
            return;

        if (!TryLeaveOrbit(uid, out var reason))
            _popup.PopupEntity(reason, uid, args.Actor);
    }

    private bool IsPilot(EntityUid actor, EntityUid console)
    {
        return TryComp<PilotComponent>(actor, out var pilot) && pilot.Console == console;
    }
}
