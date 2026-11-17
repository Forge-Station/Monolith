using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Thick weeds used to block passage. Xenomorphs always pass hard fixtures,
/// and standing on weeds speeds them up while slowing everyone else via
/// <see cref="SpeedModifierContactsComponent"/>.
/// </summary>
public sealed class ForgeXenoWeedsSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoWeedsComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<ForgeXenoWeedsComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<ForgeXenoWeedsComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<ForgeXenoComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshXenoSpeed);
    }

    private void OnPreventCollide(Entity<ForgeXenoWeedsComponent> ent, ref PreventCollideEvent args)
    {
        if (HasComp<ForgeXenoComponent>(args.OtherEntity))
            args.Cancelled = true;
    }

    private void OnStartCollide(Entity<ForgeXenoWeedsComponent> ent, ref StartCollideEvent args)
    {
        if (!HasComp<ForgeXenoComponent>(args.OtherEntity))
            return;

        EnsureComp<ForgeXenoOnWeedsComponent>(args.OtherEntity);
        _movement.RefreshMovementSpeedModifiers(args.OtherEntity);
    }

    private void OnEndCollide(Entity<ForgeXenoWeedsComponent> ent, ref EndCollideEvent args)
    {
        if (!HasComp<ForgeXenoComponent>(args.OtherEntity) || !HasComp<PhysicsComponent>(args.OtherEntity))
            return;

        if (StillOnWeeds(args.OtherEntity))
            return;

        RemComp<ForgeXenoOnWeedsComponent>(args.OtherEntity);
        _movement.RefreshMovementSpeedModifiers(args.OtherEntity);
    }

    private void OnRefreshXenoSpeed(Entity<ForgeXenoComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!HasComp<ForgeXenoOnWeedsComponent>(ent))
            return;

        if (!TryComp<PhysicsComponent>(ent, out var physics))
            return;

        var walk = 1f;
        var sprint = 1f;
        var any = false;

        foreach (var other in _physics.GetContactingEntities(ent, physics))
        {
            if (!TryComp<ForgeXenoWeedsComponent>(other, out var weeds))
                continue;

            walk = MathF.Max(walk, weeds.XenoWalkSpeedModifier);
            sprint = MathF.Max(sprint, weeds.XenoSprintSpeedModifier);
            any = true;
        }

        if (!any)
            return;

        args.ModifySpeed(walk, sprint);
    }

    private bool StillOnWeeds(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return false;

        foreach (var other in _physics.GetContactingEntities(uid, physics))
        {
            if (HasComp<ForgeXenoWeedsComponent>(other))
                return true;
        }

        return false;
    }
}
