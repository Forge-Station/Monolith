using Content.Shared._WF.Xenonids.Doom;
using Robust.Client.Graphics;

namespace Content.Client._WF.Xenonids.Doom;
public sealed partial class XenoDoomSystem : SharedXenoDoomSystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    protected override void OnDoomedAdded(Entity<MobDoomedComponent> ent, ref ComponentStartup args)
    {
        _overlay.AddOverlay(new DoomOverlay());
    }

    protected override void OnDoomedRemoved(Entity<MobDoomedComponent> ent, ref ComponentShutdown args)
    {
        _overlay.RemoveOverlay<DoomOverlay>();
    }
}
