/// Forge-Change-Start
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.SprayPainter.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client.Atmos.EntitySystems;

/// <summary>
/// Used to change the appearance of gas canisters.
/// </summary>
public sealed partial class GasCanisterAppearanceSystem : VisualizerSystem<GasCanisterComponent>
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    protected override void OnAppearanceChange(EntityUid uid, GasCanisterComponent component, ref AppearanceChangeEvent args)
    {
        if (!AppearanceSystem.TryGetData<string>(uid, PaintableVisuals.Prototype, out var protoName, args.Component) || args.Sprite is null)
            return;

        if (!_prototypeManager.TryIndex(protoName, out EntityPrototype? proto))
            return;

        // Prototype sprite layers are not always fully resolved until constructed; spawn a temp client entity.
        var tempUid = Spawn(proto.ID);
        try
        {
            SpriteSystem.LayerSetRsiState(uid, 0, SpriteSystem.LayerGetRsiState(tempUid, 0));
        }
        finally
        {
            QueueDel(tempUid);
        }
    }
}
/// Forge-Change-End
///
