using System.Numerics;

namespace Content.Shared._WF.RMCCore.Sprite;

public abstract partial class SharedRMCSpriteSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpriteSetRenderOrderComponent, MapInitEvent>(OnSetRenderOrderMapInit);
        // WOLFGATE: RMC resets the offset on pickup through ItemPickedUpEvent, an upstream hook that isn't ported
    }

    private void OnSetRenderOrderMapInit(Entity<SpriteSetRenderOrderComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.RenderOrder != null)
            _appearance.SetData(ent, SpriteSetRenderOrderComponent.Appearance.Key, ent.Comp.RenderOrder);

        if (ent.Comp.Offset != null)
            _appearance.SetData(ent, SpriteSetRenderOrderComponent.Appearance.Offset, ent.Comp.Offset);
    }

    public void SetOffset(EntityUid ent, Vector2 offset)
    {
        var sprite = EnsureComp<SpriteSetRenderOrderComponent>(ent);
        sprite.Offset = offset;
        Dirty(ent, sprite);
    }

    public void SetRenderOrder(EntityUid ent, int order)
    {
        var sprite = EnsureComp<SpriteSetRenderOrderComponent>(ent);
        sprite.RenderOrder = order;
        Dirty(ent, sprite);
    }

    public void SetColor(Entity<SpriteColorComponent?> ent, Color color)
    {
        ent.Comp = EnsureComp<SpriteColorComponent>(ent);
        ent.Comp.Color = color;
        Dirty(ent);
    }

    public DrawDepth.DrawDepth GetDrawDepth(EntityUid ent, DrawDepth.DrawDepth current = DrawDepth.DrawDepth.Mobs)
    {
        var ev = new GetDrawDepthEvent(current);
        RaiseLocalEvent(ent, ref ev);
        return ev.DrawDepth;
    }

    public virtual DrawDepth.DrawDepth UpdateDrawDepth(EntityUid sprite)
    {
        var depth = GetDrawDepth(sprite);
        // WOLFGATE: stored as an int; Wolfgate's DrawDepth enum is not NetSerializable and the server crashed serializing it
        _appearance.SetData(sprite, RMCSpriteDrawDepth.Key, (int) depth);
        return depth;
    }
}
