using Content.Shared._Starlight.NullSpace;
using Content.Shared._Starlight.Shadekin;
using Content.Shared.Teleportation.Components;

namespace Content.Server._Starlight.Shadekin;

public sealed partial class ShadekinSystem : EntitySystem
{
    public void InitializeAbilities()
    {
        SubscribeLocalEvent<BrighteyeComponent, BrighteyePortalActionEvent>(OnPortalAction);
        SubscribeLocalEvent<BrighteyeComponent, BrighteyeCreateShadeActionEvent>(OnCreateShadeAction);
    }

    #region Create Shade

    private void OnCreateShadeAction(EntityUid uid, BrighteyeComponent component, BrighteyeCreateShadeActionEvent args)
    {
        if (HasComp<NullSpaceDrainerComponent>(uid))
        {
            _popup.PopupEntity(Loc.GetString("shadekin-fail-generic"), uid, uid);
            return;
        }

        if (TryComp<ShadegenComponent>(uid, out var shadegen))
        {
            RemComp(uid, shadegen);
            args.Handled = true;
            return;
        }

        if (HasComp<NullSpaceComponent>(uid))
            return;

        EnsureComp<ShadegenComponent>(uid, out var newshadegen);
        newshadegen.Range = 8;
        _audio.PlayPvs(component.ShadegenSound, uid);

        args.Handled = true;
    }

    #endregion
    #region Portal

    private void OnPortalAction(EntityUid uid, BrighteyeComponent component, BrighteyePortalActionEvent args)
    {
        if (HasComp<NullSpaceDrainerComponent>(uid))
        {
            _popup.PopupEntity(Loc.GetString("shadekin-fail-generic"), uid, uid);
            return;
        }

        if (HasComp<NullSpaceComponent>(uid)) // No making portals while in nullspace!
        {
            args.Handled = true;
            return;
        }

        _actionsSystem.RemoveAction(uid, component.PortalAction);

        EnsureComp<PortalTimeoutComponent>(uid); // Lets not teleport as soon we put down the portal, duh.

        var newportal = SpawnAtPosition(component.PortalShadekin, Transform(uid).Coordinates);
        if (TryComp<DarkPortalComponent>(newportal, out var portal))
            portal.Brighteye = uid;

        component.Portal = newportal;

        args.Handled = true;
    }

    #endregion
}
