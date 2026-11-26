using Content.Shared.Teleportation.Systems;
using Content.Shared._Starlight.Shadekin;
using Content.Shared.Anomaly.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared.Verbs;
using Content.Shared.Anomaly;
using Content.Shared.Alert;
using Content.Shared.Actions;
using Robust.Shared.Random;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Examine;
using Content.Server.Anomaly;
using Content.Shared.Light.Components;
using Content.Shared.Throwing;
using Content.Shared.Teleportation.Components;

namespace Content.Server._Starlight.Shadekin;

public sealed class DarkPortalSystem : EntitySystem
{
    [Dependency] private readonly LinkedEntitySystem _link = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PoweredLightSystem _light = default!;
    [Dependency] private readonly ShadekinSystem _shadekin = default!;
    [Dependency] private readonly SharedAnomalySystem _sharedAnomalySystem = default!;
    [Dependency] private readonly AnomalySystem _anomalySystem = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DarkPortalComponent, ComponentStartup>(OnInit);
        SubscribeLocalEvent<DarkPortalComponent, NullSpaceShuntEvent>(NullSpaceShunt);
        SubscribeLocalEvent<DarkPortalComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<DarkPortalComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<DarkPortalComponent, OnAttemptPortalEvent>(OnAttemptPortal);
        SubscribeLocalEvent<DarkPortalComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInit(EntityUid uid, DarkPortalComponent component, ComponentStartup args)
    {
        var query = EntityQueryEnumerator<DarkHubComponent>();
        while (query.MoveNext(out var target, out var portal))
            if (portal.Hub)
                _link.TryLink(uid, target);
    }

    private void NullSpaceShunt(EntityUid uid, DarkPortalComponent component, NullSpaceShuntEvent args)
    {
        SpawnAtPosition(component.ShadekinShadow, Transform(uid).Coordinates);
        QueueDel(uid);
    }

    private void OnComponentShutdown(EntityUid uid, DarkPortalComponent component, ref ComponentShutdown args)
    {
        if (component.Brighteye is null || !TryComp<BrighteyeComponent>(component.Brighteye.Value, out var brighteye))
            return;

        OnPortalShutdown(component.Brighteye.Value, brighteye);
    }

    public void OnPortalShutdown(EntityUid uid, BrighteyeComponent component)
    {
        component.Portal = null;

        _actionsSystem.AddAction(uid, ref component.PortalAction, component.BrighteyePortalAction, uid);
        _actionsSystem.SetCooldown(component.PortalAction, TimeSpan.FromSeconds(60));
    }

    private void OnExamined(EntityUid uid, DarkPortalComponent component, ref ExaminedEvent args)
    {
        if (component.Brighteye != args.Examiner)
            return;

        args.PushMarkup(Loc.GetString("shadekin-portal-owner"));
    }

    // APPRENTLY... MOVING THIS TO SHARED IS NOT TRIGGERED? SO I HAVE TO FUCKING COPY/PASTE ON CLIENT? WTF?
    private void OnAttemptPortal(EntityUid uid, DarkPortalComponent component, OnAttemptPortalEvent args)
    {
        if (HasComp<BrighteyeComponent>(args.Subject))
            return;

        // TODO: Check if we have the Nullspace Suit? (also works for pull and thrown)

        if (TryComp<PullableComponent>(args.Subject, out var pullablea) && pullablea.BeingPulled && HasComp<BrighteyeComponent>(pullablea.Puller))
            return;

        if (TryComp<ThrownItemComponent>(args.Subject, out var thrown) && HasComp<BrighteyeComponent>(thrown.Thrower))
            return;

        args.Cancel();
    }

    private void OnGetInteractionVerbs(EntityUid uid, DarkPortalComponent component, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || component.Brighteye != args.User)
            return;

        var user = args.User;

        args.Verbs.Add(new()
        {
            Act = () =>
            {
                if (TryComp<BrighteyeComponent>(user, out var brighteye))
                    OnPortalShutdown(user, brighteye);

                SpawnAtPosition(component.ShadekinShadow, Transform(uid).Coordinates);
                QueueDel(uid);
            },
            Text = Loc.GetString("shadekin-portal-destroy"),
        });
    }
}
