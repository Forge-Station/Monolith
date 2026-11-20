using Content.Shared._WF.Xenonids.Eye;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._WF.Xenonids.Eye;

public sealed partial class QueenEyeOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<QueenEyeActionComponent, AfterAutoHandleStateEvent>(OnUpdated);
        SubscribeLocalEvent<QueenEyeActionComponent, QueenEyeActionUpdated>(OnUpdated);
        SubscribeLocalEvent<QueenEyeActionComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<QueenEyeActionComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnUpdated<T>(Entity<QueenEyeActionComponent> ent, ref T args)
    {
        Updated(ent);
    }

    private void OnAttached(Entity<QueenEyeActionComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        Updated(ent);
    }

    private void OnDetached(Entity<QueenEyeActionComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        _overlay.RemoveOverlay<QueenEyeOverlay>();
    }

    private void Updated(Entity<QueenEyeActionComponent> ent)
    {
        if (_player.LocalEntity != ent)
            return;

        if (ent.Comp.Eye == null)
        {
            _overlay.RemoveOverlay<QueenEyeOverlay>();
            return;
        }

        if (!_overlay.HasOverlay<QueenEyeOverlay>())
            _overlay.AddOverlay(new QueenEyeOverlay());
    }
}
