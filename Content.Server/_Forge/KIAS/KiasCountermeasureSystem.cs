using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Shared._Forge.KIAS;
using Robust.Shared.Map;

namespace Content.Server._Forge.KIAS;

public sealed class KiasCountermeasureSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private FireControlSystem _fire = default!;

    public bool Deploy(EntityUid launcher)
    {
        if (!HasComp<KiasDecoyLauncherComponent>(launcher) || !_kias.IsOnline(launcher)
            || Transform(launcher).GridUid is not { } grid || !_kias.HasRole(grid, KiasDeviceRole.Defence)) return false;
        var direction = Transform(launcher).LocalRotation.ToWorldVec();
        var target = new EntityCoordinates(grid, Transform(launcher).LocalPosition + direction * 10);
        return _fire.AttemptFire(launcher, launcher, target, noServer: true);
    }
}
