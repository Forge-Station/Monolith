using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.Planets;

/// <summary>Shuttle-console surface liftoff and its planet-name lookup while the hull crosses transit maps.</summary>
public sealed partial class WFOrbitEntrySystem
{
    /// <summary>Engages or cancels liftoff; the actor must currently pilot the console that sent the message.</summary>
    private void OnLiftoffMessage(EntityUid uid, ShuttleConsoleComponent component, WFLiftoffMessage args)
    {
        if (GetEntity(args.Console) != uid || !IsPilot(args.Actor, uid) || !TryGetHull(uid, out var hull, out _))
            return;

        var grid = hull.Value.Owner;
        if (HasComp<WFLiftoffComponent>(grid))
        {
            _zLevels.WfCancelLiftoff(grid);
            _nextRefresh = TimeSpan.Zero;
            return;
        }

        if (!_zLevels.WfTryBeginLiftoff(grid, uid, args.Actor, out var reason))
            _popup.PopupEntity(reason, uid, args.Actor);
        else
            _flight.PlayTakeoff(grid);

        _nextRefresh = TimeSpan.Zero;
    }

    /// <summary>Resolves the sector body owning a planet layer or either side of a transit gap.</summary>
    private bool TryGetLiftoffPlanet(EntityUid map, out EntityUid planet)
    {
        planet = EntityUid.Invalid;

        if (TryGetPlanetFromLayer(map, out planet))
            return true;

        if (!TryComp<CEZTransitMapComponent>(map, out var transit))
            return false;

        return transit.LowerMap is { } lower && TryGetPlanetFromLayer(lower, out planet)
               || transit.UpperMap is { } upper && TryGetPlanetFromLayer(upper, out planet);
    }

    /// <summary>Resolves one planet layer's server-only network owner.</summary>
    private bool TryGetPlanetFromLayer(EntityUid map, out EntityUid planet)
    {
        planet = EntityUid.Invalid;

        if (!TryComp<WFPlanetLayerComponent>(map, out var layer)
            || layer.Network is not { } netNetwork
            || !TryGetEntity(netNetwork, out var network)
            || !TryComp<WFPlanetNetworkComponent>(network, out var networkComp)
            || networkComp.Planet is not { } body
            || TerminatingOrDeleted(body))
        {
            return false;
        }

        planet = body;
        return true;
    }
}
