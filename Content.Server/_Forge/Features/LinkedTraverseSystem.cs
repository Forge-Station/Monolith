using Content.Shared._Forge.Features.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Teleportation.Components;
using Content.Shared.Timing;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Features;

public sealed class LinkedTraverseSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LinkedTraverseComponent, InteractHandEvent>(OnInteract);
        SubscribeLocalEvent<LinkedTraverseComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
    }

    private void OnInteract(EntityUid uid, LinkedTraverseComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryTraverse(uid, component, args.User);
    }

    private void OnGetVerbs(EntityUid uid, LinkedTraverseComponent component, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Act = () => TryTraverse(uid, component, args.User),
            Text = Loc.GetString(component.VerbText),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/open.svg.192dpi.png"))
        });
    }

    private bool TryTraverse(EntityUid uid, LinkedTraverseComponent component, EntityUid user)
    {
        if (TryComp<UseDelayComponent>(uid, out var delay) &&
            !_useDelay.TryResetDelay((uid, delay), checkDelayed: true))
            return true;

        if (!TryComp<LinkedEntityComponent>(uid, out var linked) || linked.LinkedEntities.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("remnant-hatch-unlinked"), uid, user, PopupType.SmallCaution);
            return true;
        }

        EntityUid? destination = null;
        foreach (var target in linked.LinkedEntities)
        {
            if (!Deleted(target))
            {
                destination = target;
                break;
            }
        }

        if (destination == null)
        {
            _popup.PopupEntity(Loc.GetString("remnant-hatch-unlinked"), uid, user, PopupType.SmallCaution);
            return true;
        }

        var coords = Transform(destination.Value).Coordinates;
        if (HasComp<PortalComponent>(destination.Value))
        {
            var timeout = EnsureComp<PortalTimeoutComponent>(user);
            timeout.EnteredPortal = uid;
            Dirty(user, timeout);
        }

        _transform.SetCoordinates(user, coords);
        _audio.PlayPvs("/Audio/Effects/teleport_arrival.ogg", destination.Value, AudioParams.Default.WithVolume(-3f));
        return true;
    }
}
