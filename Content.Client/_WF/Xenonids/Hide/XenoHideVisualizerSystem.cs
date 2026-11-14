using Content.Client._WF.RMCCore.Sprite;
using Content.Shared._WF.RMCCore.Sprite;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Hide;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WF.Xenonids.Hide;

public sealed partial class XenoHideVisualizerSystem : VisualizerSystem<XenoHideComponent>
{
    [Dependency] private RMCSpriteSystem _rmcSprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoHideComponent, GetDrawDepthEvent>(OnXenoHideGetDrawDepth,
            before: [typeof(XenoVisualizerSystem)]);
    }

    private void OnXenoHideGetDrawDepth(Entity<XenoHideComponent> ent, ref GetDrawDepthEvent args)
    {
        if (AppearanceSystem.TryGetData(ent, XenoVisualLayers.Hide, out bool hiding) &&
            hiding)
        {
            args.DrawDepth = DrawDepth.Walls;
        }
    }

    protected override void OnAppearanceChange(EntityUid uid, XenoHideComponent component, ref AppearanceChangeEvent args)
    {
        _rmcSprite.UpdateDrawDepth(uid);
    }
}
