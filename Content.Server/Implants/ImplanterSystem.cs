using System.Linq;
using Content.Server.Popups;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Spawners; // Forge-Change

namespace Content.Server.Implants;

public sealed partial class ImplanterSystem : SharedImplanterSystem
{
    private const float ImplanterAutoDespawnSeconds = 10f * 60f; // Forge-Change

    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeImplanted();

        SubscribeLocalEvent<ImplanterComponent, AfterInteractEvent>(OnImplanterAfterInteract);

        SubscribeLocalEvent<ImplanterComponent, ImplantEvent>(OnImplant);
        SubscribeLocalEvent<ImplanterComponent, DrawEvent>(OnDraw);
    }

    private void OnImplanterAfterInteract(EntityUid uid, ImplanterComponent component, AfterInteractEvent args)
    {
        if (args.Target == null || !args.CanReach || args.Handled)
            return;

        var target = args.Target.Value;
        if (!CheckTarget(target, component.Whitelist, component.Blacklist))
            return;

        //TODO: Rework when surgery is in for implant cases
        if (component.CurrentMode == ImplanterToggleMode.Draw && !component.ImplantOnly)
        {
            TryDraw(component, args.User, target, uid);
        }
        else
        {
            if (!CanImplant(args.User, target, uid, component, out var implant, out _))
            {
                // no popup if implant doesn't exist
                if (implant == null)
                    return;

                // show popup to the user saying implant failed
                var name = Identity.Name(target, EntityManager, args.User);
                var msg = Loc.GetString("implanter-component-implant-failed", ("implant", implant), ("target", name));
                _popup.PopupEntity(msg, target, args.User);
                // prevent further interaction since popup was shown
                args.Handled = true;
                return;
            }

            //Implant self instantly, otherwise try to inject the target.
            if (args.User == target)
            {
                var hadImplantBefore = HasImplantLoaded(component); // Forge-Change
                Implant(target, target, uid, component);
                TryScheduleImplanterDespawn(uid, component, hadImplantBefore, usedForImplant: true); // Forge-Change
            }
            else
                TryImplant(component, args.User, target, uid);
        }

        args.Handled = true;
    }

    /// <summary>
    /// Attempt to implant someone else.
    /// </summary>
    /// <param name="component">Implanter component</param>
    /// <param name="user">The entity using the implanter</param>
    /// <param name="target">The entity being implanted</param>
    /// <param name="implanter">The implanter being used</param>
    public void TryImplant(ImplanterComponent component, EntityUid user, EntityUid target, EntityUid implanter)
    {
        var args = new DoAfterArgs(EntityManager, user, component.ImplantTime, new ImplantEvent(), implanter, target: target, used: implanter)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return;

        _popup.PopupEntity(Loc.GetString("injector-component-injecting-user"), target, user);

        var userName = Identity.Entity(user, EntityManager);
        _popup.PopupEntity(Loc.GetString("implanter-component-implanting-target", ("user", userName)), user, target, PopupType.LargeCaution);
    }

    /// <summary>
    /// Try to remove an implant and store it in an implanter
    /// </summary>
    /// <param name="component">Implanter component</param>
    /// <param name="user">The entity using the implanter</param>
    /// <param name="target">The entity getting their implant removed</param>
    /// <param name="implanter">The implanter being used</param>
    //TODO: Remove when surgery is in
    public void TryDraw(ImplanterComponent component, EntityUid user, EntityUid target, EntityUid implanter)
    {
        var args = new DoAfterArgs(EntityManager, user, component.DrawTime, new DrawEvent(), implanter, target: target, used: implanter)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupEntity(Loc.GetString("injector-component-injecting-user"), target, user);

    }

    private void OnImplant(EntityUid uid, ImplanterComponent component, ImplantEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target == null || args.Used == null)
            return;

        var hadImplantBefore = HasImplantLoaded(component); // Forge-Change
        Implant(args.User, args.Target.Value, args.Used.Value, component);
        TryScheduleImplanterDespawn(uid, component, hadImplantBefore, usedForImplant: true); // Forge-Change

        args.Handled = true;
    }

    private void OnDraw(EntityUid uid, ImplanterComponent component, DrawEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used == null || args.Target == null)
            return;

        var hadImplantBefore = HasImplantLoaded(component); // Forge-Change
        Draw(args.Used.Value, args.User, args.Target.Value, component);
        TryScheduleImplanterDespawn(uid, component, hadImplantBefore, usedForImplant: false); // Forge-Change

        args.Handled = true;
    }
    // Forge-Change-start
    private bool HasImplantLoaded(ImplanterComponent component)
    {
        return component.ImplanterSlot.ContainerSlot?.ContainedEntities.Count > 0;
    }

    private void TryScheduleImplanterDespawn(EntityUid uid, ImplanterComponent component, bool hadImplantBefore, bool usedForImplant)
    {
        var hasImplantAfter = HasImplantLoaded(component);
        var success = usedForImplant
            ? hadImplantBefore && !hasImplantAfter
            : !hadImplantBefore && hasImplantAfter;

        if (!success)
            return;

        EnsureComp<TimedDespawnComponent>(uid).Lifetime = ImplanterAutoDespawnSeconds;
    }
    // Forge-Change-end
}
