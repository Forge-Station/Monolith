using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>Wrenching a dislocated joint back into place, the only thing that fixes a dislocation.</summary>
// The penalty is ordinary WolfmedLimbPenaltyBehavior data; no dressing, suture, bone gel or time heals it. It hurts,
// twice as much when the patient does it to themselves.
public sealed class WolfmedDislocationSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WoundTargetResolver _targeting = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WoundHostComponent, WolfmedRelocateDoAfterEvent>(OnDoAfter);
    }

    private void OnGetVerbs(Entity<WoundHostComponent> body, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || FindDislocation(body, args.User) is not { } wound)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wolfmed-relocate-verb"),
            Act = () => TryStart(body, user, wound),
        });
    }

    private void TryStart(Entity<WoundHostComponent> body, EntityUid user, Entity<WoundComponent> wound)
    {
        if (!TryGetBehavior(wound, out var behavior))
            return;

        var self = body.Owner == user;
        var delay = self ? behavior.Delay * behavior.SelfMultiplier : behavior.Delay;
        _popup.PopupEntity(Loc.GetString(self ? "wolfmed-relocate-start-self" : "wolfmed-relocate-start",
            ("user", user), ("target", body.Owner)), body, user);
        _audio.PlayPredicted(behavior.BeginSound, body, user);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            delay,
            new WolfmedRelocateDoAfterEvent(GetNetEntity(wound.Owner)),
            body,
            body)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnDoAfter(Entity<WoundHostComponent> body, ref WolfmedRelocateDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !TryGetEntity(args.Wound, out var wound))
            return;

        args.Handled = TryRelocate(body, wound.Value, args.User);
    }

    /// <summary>
    /// Sets the joint, clearing the wound and charging the pain for it. Public so a test or a future
    /// surgery step can skip the do-after.
    /// </summary>
    public bool TryRelocate(EntityUid body, EntityUid wound, EntityUid user)
    {
        if (!_net.IsServer || !TryComp(wound, out WoundComponent? core) ||
            !TryGetBehavior((wound, core), out var behavior))
            return false;

        var pain = body == user ? behavior.Pain * behavior.SelfPainMultiplier : behavior.Pain;
        _pain.ChangePain(core.HoldingPart, pain);
        if (!_wounds.RemoveWound(wound))
            return false;

        _audio.PlayPvs(behavior.EndSound, body);
        _popup.PopupEntity(Loc.GetString("wolfmed-relocate-success", ("target", body)), body, user);
        return true;
    }

    /// <summary>
    /// The dislocation this user would set: the one on the part they have selected on the targeting doll,
    /// or the first one the body carries when they have not selected a dislocated part.
    /// </summary>
    public Entity<WoundComponent>? FindDislocation(EntityUid body, EntityUid user)
    {
        if (TryComp(user, out TargetingComponent? targeting) &&
            _targeting.TryResolveExact(body, targeting.Target, out var selected) &&
            FindOnPart(selected) is { } chosen)
            return chosen;

        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (FindOnPart(part) is { } any)
                return any;
        }

        return null;
    }

    private Entity<WoundComponent>? FindOnPart(EntityUid part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is not (WoundState.Healed or WoundState.Scarred) && TryGetBehavior(wound, out _))
                return wound;
        }

        return null;
    }

    private bool TryGetBehavior(Entity<WoundComponent> wound, out WolfmedDislocationBehavior behavior)
    {
        behavior = null!;
        return _prototypes.TryIndex(wound.Comp.Prototype, out var prototype) &&
               prototype.TryGetBehavior(wound.Comp.Severity, out behavior);
    }
}
