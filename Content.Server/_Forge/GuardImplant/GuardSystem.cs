using Content.Server.Administration.Logs;
using Content.Server.Mindshield;
using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Implants;
using Content.Shared._Forge.Guard.Components;
using Content.Shared.Revolutionary.Components;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;

namespace Content.Server._Forge.Guard;

/// <summary>
/// Adds or removes the guard overlay component when the empire guard implant is inserted or removed.
/// </summary>
public sealed class GuardSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogManager = default!;
    [Dependency] private readonly MindShieldSystem _mindShield = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GuardImplantComponent, ImplantImplantedEvent>(OnImplantImplanted);
        SubscribeLocalEvent<GuardImplantComponent, EntGotRemovedFromContainerMessage>(OnImplantDraw);
        SubscribeLocalEvent<GuardComponent, MapInitEvent>(OnGuardMapInit);
    }

    private void OnImplantImplanted(Entity<GuardImplantComponent> ent, ref ImplantImplantedEvent ev)
    {
        if (ev.Implanted == null)
            return;

        EnsureComp<GuardComponent>(ev.Implanted.Value);
        _mindShield.MindShieldRemovalCheck(ev.Implanted.Value, ev.Implant);
        _adminLogManager.Add(LogType.Mind, LogImpact.Medium, $"{ToPrettyString(ev.Implanted.Value)} was implanted with a guard implant.");
    }

    private void OnImplantDraw(Entity<GuardImplantComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        RemComp<GuardComponent>(args.Container.Owner);
    }

    private void OnGuardMapInit(EntityUid uid, GuardComponent comp, MapInitEvent args)
    {
        if (HasComp<HeadRevolutionaryComponent>(uid))
        {
            RemCompDeferred<GuardComponent>(uid);
            return;
        }

        if (!HasComp<RevolutionaryComponent>(uid))
            return;

        var stunTime = TimeSpan.FromSeconds(4);
        var name = Identity.Entity(uid, EntityManager);
        RemComp<RevolutionaryComponent>(uid);
        _stun.TryParalyze(uid, stunTime, true);
        _popupSystem.PopupEntity(Loc.GetString("rev-break-control", ("name", name)), uid);
    }
}
