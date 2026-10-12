using Robust.Shared.Physics.Events;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Thick weeds block passage. Xenomorphs walk through them.
/// </summary>
public sealed class ForgeXenoWeedsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoWeedsComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<ForgeXenoWeedsComponent> ent, ref PreventCollideEvent args)
    {
        if (HasComp<ForgeXenoComponent>(args.OtherEntity))
            args.Cancelled = true;
    }
}
