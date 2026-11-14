using Content.Server._NF.Shipyard;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetExpeditions;

/// <summary>
/// A ship bought with an expedition console aboard is bought for the worlds: every linear thruster on it is converted
/// for landing, as the kit converts one.
/// </summary>
public sealed class WFExpeditionShipSystem : EntitySystem
{
    [Dependency] private ThrusterSystem _thrusters = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MapGridComponent, ShipBoughtEvent>(OnShipBought);
    }

    private void OnShipBought(EntityUid ship, MapGridComponent grid, ShipBoughtEvent args)
    {
        if (!HasExpeditionConsole(ship))
            return;

        var converted = 0;
        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thruster, out var xform))
        {
            if (xform.GridUid != ship || thruster.Type != ThrusterType.Linear || HasComp<WFLandingThrusterComponent>(uid))
                continue;

            EnsureComp<WFLandingThrusterComponent>(uid);
            _thrusters.WfRefreshAtmosphereThruster(uid, thruster);
            converted++;
        }

        if (converted > 0)
            Log.Info($"Converted {converted} thrusters on {ToPrettyString(ship)} for landing: it carries an expedition console.");
    }

    private bool HasExpeditionConsole(EntityUid ship)
    {
        var query = EntityQueryEnumerator<SalvageExpeditionConsoleComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == ship)
                return true;
        }

        return false;
    }
}
