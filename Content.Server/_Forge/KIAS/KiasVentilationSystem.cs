using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Shared._Forge.KIAS;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Forge.KIAS;

public sealed class KiasVentilationSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private IGameTiming _timing = default!;

    public bool Restore(EntityUid target)
    {
        if (!TryComp<GasVentPumpComponent>(target, out var vent) || !_kias.IsOnline(target)
            || Transform(target).GridUid is not { } grid || !TryComp<GridAtmosphereComponent>(grid, out var atmos)
            || !TryComp<MapGridComponent>(grid, out var map)) return false;
        var tile = _maps.TileIndicesFor(grid, map, Transform(target).Coordinates);
        if (!_atmos.IsSealedSafeRoom(atmos, tile)) return false;
        vent.IsPressureLockoutManuallyDisabled = true;
        vent.ManualLockoutReenabledAt = _timing.CurTime + vent.ManualLockoutDisabledDuration;
        return true;
    }
}
