using System.Numerics;
using Content.Server.Stack;
using Content.Shared._WF.Tether;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Tether;

/// <summary>
/// Hand interaction: paying a coil out from one attach point to another, untying, reeling by a
/// metre at a time, cutting and examining.
/// </summary>
public sealed partial class RopeSystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private StackSystem _stack = default!;

    private const string CuttingQuality = "Cutting";

    /// <summary>Metres a single "take in slack" or "pay out" changes the rest length by.</summary>
    private const float ReelStep = 1f;

    /// <summary>Slack may not be taken in past this strain; the rope is already doing work.</summary>
    private const float ReelStrainLimit = 0.9f;

    private static readonly TimeSpan UntieDelay = TimeSpan.FromSeconds(2);

    private readonly List<EntityUid> _cancelledCarries = new();

    private void InitializeInteraction()
    {
        SubscribeLocalEvent<RopeCoilComponent, AfterInteractEvent>(OnCoilAfterInteract);
        SubscribeLocalEvent<RopeCoilComponent, UseInHandEvent>(OnCoilUseInHand);
        SubscribeLocalEvent<RopeCoilComponent, DroppedEvent>(OnCoilDropped);
        SubscribeLocalEvent<RopeAttachPointComponent, GetVerbsEvent<Verb>>(OnAttachPointVerbs);
        SubscribeLocalEvent<RopeAttachPointComponent, GetVerbsEvent<AlternativeVerb>>(OnAttachPointAltVerbs);
        SubscribeLocalEvent<RopeAttachPointComponent, InteractUsingEvent>(OnAttachPointInteractUsing);
        SubscribeLocalEvent<RopeAttachPointComponent, ExaminedEvent>(OnAttachPointExamined);
        SubscribeLocalEvent<RopeAttachPointComponent, RopeUntieDoAfterEvent>(OnUntieDoAfter);
    }

    #region Coil

    private void OnCoilAfterInteract(Entity<RopeCoilComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || target == args.User)
            return;

        if (!TryResolveAttachPoint(args.User, ent, target, out var point, out var handled))
        {
            args.Handled = handled;
            return;
        }

        args.Handled = true;
        if (TryComp<RopeCarrierComponent>(args.User, out var carrier) && GetEntity(carrier.Coil) == ent.Owner)
            FinishCarry(args.User, carrier, ent, point);
        else
            StartCarry(args.User, ent, point);
    }

    /// <summary>
    /// Uses the clicked entity's own attach point, otherwise lets another system supply or spawn
    /// one. Power cord clamps hook in here rather than duplicating the coil flow.
    /// </summary>
    private bool TryResolveAttachPoint(
        EntityUid user,
        Entity<RopeCoilComponent> coil,
        EntityUid target,
        out EntityUid point,
        out bool handled)
    {
        point = default;
        handled = false;
        if (HasComp<RopeAttachPointComponent>(target))
        {
            // WOLFGATE START: let a handler veto a coil that does not belong on this point, by
            // clearing the pre-set attach point. Power cord clamps refuse plain rope and vice versa.
            var veto = new RopeCoilTargetAttemptEvent(user, coil.Owner, target, coil.Comp.RopeType)
            {
                AttachPoint = target,
            };
            RaiseLocalEvent(target, ref veto);
            if (veto.AttachPoint != target)
            {
                handled = veto.Handled;
                return false;
            }
            // WOLFGATE END

            point = target;
            return true;
        }

        var ev = new RopeCoilTargetAttemptEvent(user, coil.Owner, target, coil.Comp.RopeType);
        RaiseLocalEvent(target, ref ev);
        handled = ev.Handled;
        if (ev.AttachPoint is not { } supplied || !HasComp<RopeAttachPointComponent>(supplied))
            return false;

        point = supplied;
        handled = true;
        return true;
    }

    /// <summary>
    /// Takes the loose end off the coil. The carried rope is visual only and does not occupy one of
    /// the point's rope slots until the second end is tied.
    /// </summary>
    private void StartCarry(EntityUid user, Entity<RopeCoilComponent> coil, EntityUid point)
    {
        if (!_protos.TryIndex(coil.Comp.RopeType, out var proto))
            return;

        if (IsFull(point))
        {
            _popup.PopupEntity(Loc.GetString("rope-popup-point-full"), point, user);
            return;
        }

        CancelCarry(user);
        var units = GetCoilUnits(coil);
        var reach = MathF.Min(units * MathF.Max(coil.Comp.MetresPerUnit, 0.1f), proto.MaxLength);
        var rope = Spawn(null, Transform(point).Coordinates);
        var comp = EnsureComp<RopeComponent>(rope);
        comp.EndA = GetNetEntity(point);
        comp.EndB = GetNetEntity(user);
        comp.RopeType = coil.Comp.RopeType;
        comp.Length = MathF.Max(reach, RopeMath.MinLength);
        comp.Carried = true;
        Dirty(rope, comp);
        _pvs.AddGlobalOverride(rope);

        var carrier = EnsureComp<RopeCarrierComponent>(user);
        carrier.Rope = GetNetEntity(rope);
        carrier.Anchor = GetNetEntity(point);
        carrier.Coil = GetNetEntity(coil.Owner);
        carrier.Reach = comp.Length;
        Dirty(user, carrier);
        _popup.PopupEntity(Loc.GetString("rope-popup-carry-start"), user, user);
    }

    /// <summary>Ties the carried end off at a second point, consuming whole units of rope.</summary>
    private void FinishCarry(EntityUid user, RopeCarrierComponent carrier, Entity<RopeCoilComponent> coil, EntityUid point)
    {
        if (!TryGetEntity(carrier.Anchor, out var anchor) || anchor == point ||
            !_protos.TryIndex(coil.Comp.RopeType, out var proto))
        {
            CancelCarry(user);
            return;
        }

        if (IsFull(point) || IsFull(anchor.Value))
        {
            _popup.PopupEntity(Loc.GetString("rope-popup-point-full"), point, user);
            return;
        }

        var metresPerUnit = MathF.Max(coil.Comp.MetresPerUnit, 0.1f);
        var distance = Vector2.Distance(GetAnchorPosition(anchor.Value), GetAnchorPosition(point));
        if (!float.IsFinite(distance))
        {
            CancelCarry(user);
            return;
        }

        var metres = MathF.Max(distance * MathF.Max(coil.Comp.Slack, 1f), 1f);
        var units = (int) MathF.Ceiling(metres / metresPerUnit);
        var length = units * metresPerUnit;
        if (length > proto.MaxLength)
        {
            _popup.PopupEntity(Loc.GetString("rope-popup-too-far"), point, user);
            return;
        }

        if (units > GetCoilUnits(coil))
        {
            _popup.PopupEntity(Loc.GetString("rope-popup-not-enough"), point, user);
            return;
        }

        // Cancel first: consuming the last unit deletes the coil entity the carry refers to. The
        // anchor is about to be tied, so it must not be swept up as an unused point.
        CancelCarry(user, keepAnchor: true);
        if (TryComp<StackComponent>(coil.Owner, out var stack) && !_stack.Use(coil.Owner, units, stack))
            return;

        if (!TryCreateRope(anchor.Value, point, coil.Comp.RopeType, length, out var rope) || rope is not { } created)
            return;

        var comp = Comp<RopeComponent>(created);
        comp.Units = units;
        comp.MetresPerUnit = metresPerUnit;
    }

    private void OnCoilUseInHand(Entity<RopeCoilComponent> ent, ref UseInHandEvent args)
    {
        if (!TryComp<RopeCarrierComponent>(args.User, out var carrier) || GetEntity(carrier.Coil) != ent.Owner)
            return;

        args.Handled = true;
        CancelCarry(args.User, true);
    }

    private void OnCoilDropped(Entity<RopeCoilComponent> ent, ref DroppedEvent args)
    {
        if (TryComp<RopeCarrierComponent>(args.User, out var carrier) && GetEntity(carrier.Coil) == ent.Owner)
            CancelCarry(args.User, true);
    }

    /// <summary>
    /// Drops the loose end when the coil leaves the hand or the carrier walks out of rope. The
    /// anchor hears about it so a point that only exists to hold this rope can clean itself up;
    /// <paramref name="keepAnchor"/> suppresses that for the hand-off inside <see cref="FinishCarry"/>,
    /// which cancels the carry only to tie the very same anchor off a line later.
    /// </summary>
    private void CancelCarry(EntityUid user, bool notify = false, bool keepAnchor = false)
    {
        if (!TryComp<RopeCarrierComponent>(user, out var carrier))
            return;

        RemComp<RopeCarrierComponent>(user);
        var ropeType = TryGetEntity(carrier.Rope, out var rope) && TryComp<RopeComponent>(rope, out var ropeComp)
            ? ropeComp.RopeType
            : default;

        if (rope != null && !TerminatingOrDeleted(rope.Value))
            Del(rope.Value);

        if (!keepAnchor && TryGetEntity(carrier.Anchor, out var anchor) && !TerminatingOrDeleted(anchor.Value))
        {
            var ev = new RopeCarryCancelledEvent(user, ropeType);
            RaiseLocalEvent(anchor.Value, ref ev);
        }

        if (notify)
            _popup.PopupEntity(Loc.GetString("rope-popup-carry-cancel"), user, user);
    }

    /// <summary>Keeps the carrier component from outliving its rope entity.</summary>
    private void CancelCarryFor(EntityUid rope)
    {
        var net = GetNetEntity(rope);
        var query = EntityQueryEnumerator<RopeCarrierComponent>();
        while (query.MoveNext(out var uid, out var carrier))
        {
            if (carrier.Rope != net)
                continue;

            RemComp<RopeCarrierComponent>(uid);
            return;
        }
    }

    private void UpdateCarriers(float frameTime)
    {
        _cancelledCarries.Clear();
        var query = EntityQueryEnumerator<RopeCarrierComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var carrier, out var xform))
        {
            if (!TryGetEntity(carrier.Rope, out var rope) || TerminatingOrDeleted(rope.Value) ||
                !TryGetEntity(carrier.Anchor, out var anchor) || TerminatingOrDeleted(anchor.Value) ||
                !TryGetEntity(carrier.Coil, out var coil) || TerminatingOrDeleted(coil.Value) ||
                !_hands.IsHolding(uid, coil.Value, out _) ||
                xform.MapID != Transform(anchor.Value).MapID ||
                Vector2.Distance(GetAnchorPosition(anchor.Value), TransformSystem.GetWorldPosition(uid)) > carrier.Reach)
                _cancelledCarries.Add(uid);
        }

        foreach (var uid in _cancelledCarries)
        {
            CancelCarry(uid, true);
        }

        _cancelledCarries.Clear();
    }

    private bool IsFull(EntityUid point)
    {
        return !TryComp<RopeAttachPointComponent>(point, out var attach) || attach.Ropes.Count >= attach.MaxRopes;
    }

    private int GetCoilUnits(Entity<RopeCoilComponent> coil)
    {
        return TryComp<StackComponent>(coil.Owner, out var stack) ? stack.Count : 1;
    }

    #endregion

    #region Verbs, cutting and examine

    private void OnAttachPointVerbs(Entity<RopeAttachPointComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || ent.Comp.Ropes.Count == 0)
            return;

        var user = args.User;
        foreach (var net in ent.Comp.Ropes.ToArray())
        {
            if (!TryGetEntity(net, out var rope) || !TryComp<RopeComponent>(rope, out var comp))
                continue;

            var label = GetRopeName(comp);
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("rope-verb-untie", ("rope", label)),
                Act = () => StartUntie(user, ent.Owner, net),
            });
        }
    }

    private void OnAttachPointAltVerbs(Entity<RopeAttachPointComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || ent.Comp.Ropes.Count == 0)
            return;

        var user = args.User;
        foreach (var net in ent.Comp.Ropes.ToArray())
        {
            if (!TryGetEntity(net, out var rope) || rope is not { } uid || !TryComp<RopeComponent>(uid, out var comp))
                continue;

            var label = GetRopeName(comp);
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("rope-verb-take-in", ("rope", label)),
                Act = () => Reel(user, uid, -ReelStep),
            });
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("rope-verb-pay-out", ("rope", label)),
                Act = () => Reel(user, uid, ReelStep),
            });
        }
    }

    private void StartUntie(EntityUid user, EntityUid point, NetEntity rope)
    {
        var ev = new RopeUntieDoAfterEvent(rope);
        var args = new DoAfterArgs(EntityManager, user, UntieDelay, ev, point, point)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            RequireCanInteract = true,
            BlockDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        _doAfter.TryStartDoAfter(args);
    }

    private void OnUntieDoAfter(Entity<RopeAttachPointComponent> ent, ref RopeUntieDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryGetEntity(args.Rope, out var rope) ||
            !ent.Comp.Ropes.Contains(args.Rope))
            return;

        args.Handled = true;
        BreakRope(rope.Value, true, args.User);
    }

    /// <summary>
    /// Moves the rest length by a metre. Paying out spends a unit from a held coil of the same
    /// type; taking in returns one, and is refused while the rope is already near its limit.
    /// </summary>
    private void Reel(EntityUid user, EntityUid rope, float delta)
    {
        if (!TryComp<RopeComponent>(rope, out var comp) || !_protos.TryIndex(comp.RopeType, out var proto))
            return;

        var metresPerUnit = comp.MetresPerUnit > 0.1f ? comp.MetresPerUnit : 1f;
        if (delta < 0f)
        {
            if (comp.Strain > ReelStrainLimit)
            {
                _popup.PopupEntity(Loc.GetString("rope-popup-taut"), rope, user);
                return;
            }

            if (comp.Length - metresPerUnit < RopeMath.MinLength)
                return;

            if (!SetLength(rope, comp.Length - metresPerUnit))
                return;

            // Only rope that was paid out of a coil comes back as one.
            if (comp.Refundable && comp.Units > 0)
            {
                comp.Units -= 1;
                GiveUnits(user, comp, proto, 1);
            }

            return;
        }

        if (comp.Length + metresPerUnit > proto.MaxLength)
        {
            _popup.PopupEntity(Loc.GetString("rope-popup-max-length"), rope, user);
            return;
        }

        if (!TryTakeUnit(user, comp.RopeType))
        {
            _popup.PopupEntity(Loc.GetString("rope-popup-need-coil"), rope, user);
            return;
        }

        if (SetLength(rope, comp.Length + metresPerUnit))
            comp.Units += 1;
    }

    /// <summary>Spends one unit from a held coil of the given rope type.</summary>
    private bool TryTakeUnit(EntityUid user, ProtoId<RopeTypePrototype> ropeType)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp<RopeCoilComponent>(held, out var coil) || coil.RopeType != ropeType ||
                !TryComp<StackComponent>(held, out var stack) || stack.Count < 1)
                continue;

            return _stack.Use(held, 1, stack);
        }

        return false;
    }

    /// <summary>Returns units to a held coil of the same type, or spawns a fresh one.</summary>
    private void GiveUnits(EntityUid user, RopeComponent rope, RopeTypePrototype proto, int units)
    {
        if (units <= 0 || proto.StackType is not { } stackType)
            return;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp<RopeCoilComponent>(held, out var coil) || coil.RopeType != rope.RopeType ||
                !TryComp<StackComponent>(held, out var stack))
                continue;

            _stack.SetCount(held, stack.Count + units, stack);
            return;
        }

        var spawned = _stack.Spawn(units, stackType, Transform(user).Coordinates);
        _hands.PickupOrDrop(user, spawned);
    }

    /// <summary>Untying returns exactly the units the rope was paid out with.</summary>
    private void RefundCoil(RopeComponent rope, RopeTypePrototype proto, EntityUid origin, EntityUid? user)
    {
        if (proto.StackType is not { } stackType)
            return;

        var metresPerUnit = rope.MetresPerUnit > 0.1f ? rope.MetresPerUnit : 1f;
        var units = rope.Units > 0 ? rope.Units : (int) MathF.Ceiling(rope.Length / metresPerUnit);
        if (units <= 0)
            return;

        var coordinates = TerminatingOrDeleted(origin) ? EntityCoordinates.Invalid : Transform(origin).Coordinates;
        if (!coordinates.IsValid(EntityManager))
            return;

        var spawned = _stack.Spawn(units, stackType, coordinates);
        if (user is { } holder && !TerminatingOrDeleted(holder))
            _hands.PickupOrDrop(holder, spawned);
    }

    private void OnAttachPointInteractUsing(Entity<RopeAttachPointComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Ropes.Count == 0 || !_tool.HasQuality(args.Used, CuttingQuality))
            return;

        args.Handled = true;
        foreach (var net in ent.Comp.Ropes.ToArray())
        {
            if (TryGetEntity(net, out var rope) && !TerminatingOrDeleted(rope.Value))
                BreakRope(rope.Value);
        }

        _popup.PopupEntity(Loc.GetString("rope-popup-cut"), ent.Owner, args.User);
    }

    private void OnAttachPointExamined(Entity<RopeAttachPointComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Ropes.Count == 0)
        {
            args.PushMarkup(Loc.GetString("rope-examine-empty"));
            return;
        }

        foreach (var net in ent.Comp.Ropes)
        {
            if (!TryGetEntity(net, out var rope) || !TryComp<RopeComponent>(rope, out var comp))
                continue;

            args.PushMarkup(Loc.GetString("rope-examine-entry",
                ("rope", GetRopeName(comp)),
                ("length", MathF.Round(comp.Length, 1)),
                ("strain", Loc.GetString(StrainWord(comp.Strain)))));
        }
    }

    private string GetRopeName(RopeComponent rope)
    {
        return _protos.TryIndex(rope.RopeType, out var proto) ? Loc.GetString(proto.Name) : rope.RopeType.Id;
    }

    private static string StrainWord(float strain)
    {
        if (strain <= 0.05f)
            return "rope-strain-slack";
        if (strain < 0.5f)
            return "rope-strain-taut";

        return strain < ReelStrainLimit ? "rope-strain-strained" : "rope-strain-critical";
    }

    #endregion
}
