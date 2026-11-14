using Content.Shared._Starlight.Shadekin;
using Content.Shared.Humanoid;
using Content.Shared.Zombies;
using Content.Shared.Eye;
using Content.Shared._Starlight.NullSpace;

namespace Content.Server._Starlight.Shadekin;

public sealed partial class ShadekinSystem : EntitySystem
{
    public void InitializeBrighteye()
    {
        SubscribeLocalEvent<BrighteyeComponent, ComponentStartup>(OnInit);
        SubscribeLocalEvent<BrighteyeComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BrighteyeComponent, NullSpaceShuntEvent>(NullSpaceShunt);
        SubscribeLocalEvent<BrighteyeComponent, GetVisMaskEvent>(OnGetVisMask);
        SubscribeLocalEvent<BrighteyeComponent, EntityZombifiedEvent>((uid, _, _) => RemComp<BrighteyeComponent>(uid));
    }

    private void OnGetVisMask(Entity<BrighteyeComponent> uid, ref GetVisMaskEvent args) =>
        args.VisibilityMask |= (int)VisibilityFlags.NullSpace;

    private void OnInit(EntityUid uid, BrighteyeComponent component, ComponentStartup args)
    {
        if (!HasComp<ShadekinComponent>(uid))
        {
            RemComp<BrighteyeComponent>(uid);
            return;
        }

        EnsureComp<NullPhaseComponent>(uid, out var nullphase);
        nullphase.ShuntCooldown = TimeSpan.FromSeconds(120);

        _actionsSystem.AddAction(uid, ref component.PortalAction, component.BrighteyePortalAction, uid);
        _actionsSystem.AddAction(uid, ref component.CreateShadeAction, component.BrighteyeCreateShadeAction, uid);

        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            SetBrighteyes(uid, humanoid);

        _eye.RefreshVisibilityMask(uid);
    }

    private void OnShutdown(EntityUid uid, BrighteyeComponent component, ComponentShutdown args)
    {
        RemComp<NullPhaseComponent>(uid);
        _actionsSystem.RemoveAction(uid, component.PortalAction);
        _actionsSystem.RemoveAction(uid, component.CreateShadeAction);

        if (component.Portal is not null)
        {
            SpawnAtPosition(component.ShadekinShadow, Transform(component.Portal.Value).Coordinates);
            QueueDel(component.Portal.Value);
        }

        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            SetBlackeyes(uid, humanoid);

        _eye.RefreshVisibilityMask(uid);
    }

    private void NullSpaceShunt(EntityUid uid, BrighteyeComponent component, NullSpaceShuntEvent args)
    {
        RemComp<ShadegenComponent>(uid);
        Dirty(uid, component);
    }

    /// <summary>
    /// Change the humanoid eye to be bright and glow!
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="humanoid"></param>
    public void SetBrighteyes(EntityUid uid, HumanoidAppearanceComponent humanoid)
    {
        humanoid.EyeColor = EyeColor.MakeBrighteyeValid(humanoid.EyeColor);
        Dirty(uid, humanoid);
    }

    /// <summary>
    /// Change the humanoid eye to be validated by HumanoidEyeColor.Shadekin (Blackeyes)
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="humanoid"></param>
    public void SetBlackeyes(EntityUid uid, HumanoidAppearanceComponent humanoid)
    {
        humanoid.EyeColor = EyeColor.MakeShadekinValid(humanoid.EyeColor);

        Dirty(uid, humanoid);
    }
}
