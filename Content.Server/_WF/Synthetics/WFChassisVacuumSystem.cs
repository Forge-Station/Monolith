using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._WF.Synthetics;
using Content.Shared.Atmos;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// A chassis on a vacuum tile loses heat as it would in open space. An empty tile has almost no heat capacity, so the
/// temperature exchange alone left a synthetic on a holed deck at room temperature forever.
/// </summary>
public sealed partial class WFChassisVacuumSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private TemperatureSystem _temperature = default!;

    private static readonly GasMixture Space = GasMixture.SpaceGas;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFChassisComponent, AtmosExposedUpdateEvent>(OnAtmosExposed);
    }

    private void OnAtmosExposed(Entity<WFChassisComponent> ent, ref AtmosExposedUpdateEvent args)
    {
        // Open space already exchanges with the space heat capacity.
        if (args.GasMixture.Immutable || args.Transform.MapUid == null ||
            !TryComp(ent, out TemperatureComponent? temperature))
            return;

        var vacuum = 1f - args.GasMixture.Pressure / Atmospherics.HazardLowPressure;
        if (vacuum <= 0f)
            return;

        // The same exchange TemperatureSystem runs, against space, scaled by how close the tile is to empty.
        var delta = Atmospherics.TCMB - temperature.CurrentTemperature;
        var spaceCapacity = _atmosphere.GetHeatCapacity(Space, false);
        var bodyCapacity = _temperature.GetHeatCapacity(ent, temperature);
        var heat = delta * (spaceCapacity * bodyCapacity / (spaceCapacity + bodyCapacity));
        _temperature.ChangeHeat(ent, heat * temperature.AtmosTemperatureTransferEfficiency * vacuum, temperature: temperature);
    }
}
