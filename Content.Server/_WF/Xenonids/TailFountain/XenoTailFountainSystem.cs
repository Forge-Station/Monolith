using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.Xenonids.TailFountain;

namespace Content.Server._WF.Xenonids.TailFountain;

public sealed partial class XenoTailFountainSystem : SharedXenoTailFountainSystem
{
    [Dependency] private FlammableSystem _flammable = default!;

    protected override void Extinguish(EntityUid target)
    {
        _flammable.Extinguish(target);
    }
}
