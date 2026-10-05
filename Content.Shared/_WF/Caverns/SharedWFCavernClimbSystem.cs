using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Shared._WF.Caverns;

/// <summary>Climbing between a cavern and its ground: the verbs, the DoAfter and its popups, and the climb-point examine.</summary>
// The move itself is the server's (WFCavernClimbSystem), because the exit depends on the server-only mouth registry.
public abstract partial class SharedWFCavernClimbSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Seconds to climb down a shaft, on any world.</summary>
    public const float ClimbDownSeconds = 3f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernClimbComponent, GetVerbsEvent<AlternativeVerb>>(OnClimbPointVerbs);
        SubscribeLocalEvent<WFCavernClimbComponent, ActivateInWorldEvent>(OnClimbPointActivate);
        SubscribeLocalEvent<WFCavernClimbComponent, ExaminedEvent>(OnClimbPointExamined);
        SubscribeLocalEvent<WFCavernShaftComponent, GetVerbsEvent<AlternativeVerb>>(OnShaftVerbs);
    }

    private void OnClimbPointVerbs(Entity<WFCavernClimbComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !OnSameFloor(args.User, ent))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-cavern-verb-climb-up"),
            Act = () => TryStartClimbUp(user, ent),
        });
    }

    private void OnClimbPointActivate(Entity<WFCavernClimbComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartClimbUp(args.User, ent);
    }

    private void OnClimbPointExamined(Entity<WFCavernClimbComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-cavern-climb-examine"));
    }

    private void OnShaftVerbs(Entity<WFCavernShaftComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !OnSameFloor(args.User, ent))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-cavern-verb-climb-down"),
            Act = () => TryStartClimbDown(user, ent),
        });
    }

    /// <summary>Starts climbing up a climb point, taking its gravity-scaled delay.</summary>
    public bool TryStartClimbUp(EntityUid user, Entity<WFCavernClimbComponent> climb)
    {
        return TryStartClimb(user, climb, climb.Comp.Delay, "wf-cavern-climb-up-start", "wf-cavern-climb-up-start-others");
    }

    /// <summary>Starts climbing down the shaft under a shade.</summary>
    public bool TryStartClimbDown(EntityUid user, Entity<WFCavernShaftComponent> shaft)
    {
        return TryStartClimb(user, shaft, ClimbDownSeconds, "wf-cavern-climb-down-start", "wf-cavern-climb-down-start-others");
    }

    /// <summary>Starts the climb DoAfter on a climb point or shade and shows who started it.</summary>
    private bool TryStartClimb(EntityUid user, EntityUid target, float seconds, string selfKey, string othersKey)
    {
        if (!OnSameFloor(user, target))
            return false;

        // Climbing needs no hands, so the hands' DoAfter multiplier doesn't shorten it: the delay is the delay.
        var args = new DoAfterArgs(EntityManager, user, seconds, new WFCavernClimbDoAfterEvent(), target, target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            BlockDuplicate = true,
            MultiplyDelay = false,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return false;

        _popup.PopupPredicted(Loc.GetString(selfKey),
            Loc.GetString(othersKey, ("user", Identity.Entity(user, EntityManager))),
            user,
            user);
        return true;
    }

    /// <summary>Whether the user stands on the same grid as the climb point or shade, not on a hull parked above it.</summary>
    private bool OnSameFloor(EntityUid user, EntityUid target)
    {
        var grid = Transform(target).GridUid;
        return grid != null && Transform(user).GridUid == grid;
    }
}
