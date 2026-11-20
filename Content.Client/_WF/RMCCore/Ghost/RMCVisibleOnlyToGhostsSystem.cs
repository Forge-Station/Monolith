using Content.Shared._WF.RMCCore.Ghost;
using Content.Shared.Ghost;
using Robust.Client.GameObjects;
using Robust.Client.Player;

namespace Content.Client._WF.RMCCore.Ghost;
public sealed partial class RMCVisibleOnlyToGhostsSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Update(float frameTime)
    {
        var local = _player.LocalEntity;

        var visibleQuery = EntityQueryEnumerator<RMCVisibleToGhostsOnlyComponent, SpriteComponent>();

        bool isGhost = HasComp<GhostComponent>(local);

        while (visibleQuery.MoveNext(out var uid, out var visible, out var sprite))
        {
            if (!_sprite.LayerMapTryGet((uid, sprite), RMCGhostVisibleOnlyVisualLayers.Base, out var layer, true))
                continue;

            _sprite.LayerSetVisible((uid, sprite), layer, isGhost);
        }
    }
}
