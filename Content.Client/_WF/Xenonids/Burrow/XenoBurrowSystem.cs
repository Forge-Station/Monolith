using Content.Shared._WF.Xenonids.Markers;
using Content.Shared._WF.Xenonids.Burrow;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client._WF.Xenonids.Burrow;

public sealed partial class XenoBurrowSystem : SharedXenoBurrowSystem
{
    [Dependency] private IPlayerManager _player = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var localEntity = _player.LocalEntity;

        var burrowQuery = EntityQueryEnumerator<XenoBurrowComponent, SpriteComponent, RMCNightVisionVisibleComponent>();

        while (burrowQuery.MoveNext(out var xeno, out var burrowed, out var sprite, out var nightVision))
        {
            if (localEntity != xeno)
                sprite.Visible = !burrowed.Active;
            else
                sprite.Color = burrowed.Active ? Color.White.WithAlpha(0.4f) : Color.White;

            if (burrowed.Active)
                nightVision.Transparency = 0.4f;
            else
                nightVision.Transparency = null;
        }
    }
}
