using Content.Shared._WF.Xenonids.Invisibility;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Xenonids.Invisibility;

public sealed class XenoInvisibilityVisualsSystem : EntitySystem
{
    private EntityQuery<XenoActiveInvisibleComponent> _activeInvisibleQuery;

    public override void Initialize()
    {
        _activeInvisibleQuery = GetEntityQuery<XenoActiveInvisibleComponent>();
    }

    public override void Update(float frameTime)
    {
        var invisible = EntityQueryEnumerator<XenoTurnInvisibleComponent, SpriteComponent>();
        while (invisible.MoveNext(out var uid, out var comp, out var sprite))
        {
            var opacity = _activeInvisibleQuery.HasComp(uid) ? comp.Opacity : 1;
            sprite.Color = Color.White.WithAlpha(opacity);
        }
    }
}
